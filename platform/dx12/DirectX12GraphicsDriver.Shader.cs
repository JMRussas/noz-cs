//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
using System.Text;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.Direct3D.Compilers;
using Silk.NET.DXGI;

namespace NoZ.Platform;

public unsafe partial class DirectX12GraphicsDriver
{
    private D3DCompiler _d3dCompiler = null!;
    private bool _d3dCompilerInitialized;

    public nuint CreateShader(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings)
    {
        if (!_d3dCompilerInitialized)
        {
            _d3dCompiler = D3DCompiler.GetApi();
            _d3dCompilerInitialized = true;
        }

        // Compile vertex shader
        var vsBlob = CompileShader(name, vertexSource, "main", "vs_5_1");

        // Compile pixel shader
        ComPtr<ID3D10Blob> psBlob;
        try
        {
            psBlob = CompileShader(name, fragmentSource, "main", "ps_5_1");
        }
        catch
        {
            vsBlob.Dispose();
            throw;
        }

        // Create root signature
        ComPtr<ID3D12RootSignature> rootSignature;
        try
        {
            rootSignature = CreateRootSignature();
        }
        catch
        {
            vsBlob.Dispose();
            psBlob.Dispose();
            throw;
        }

        var handle = _nextShaderId++;
        _shaders[handle] = new ShaderInfo
        {
            RootSignature = rootSignature,
            VertexShaderBlob = vsBlob,
            PixelShaderBlob = psBlob,
            PsoCache = new Dictionary<PsoKey, ComPtr<ID3D12PipelineState>>()
        };

        return (nuint)handle;
    }

    public void DestroyShader(nuint handle)
    {
        ref var shader = ref _shaders[(int)handle];

        if (shader.PsoCache != null)
        {
            foreach (var pso in shader.PsoCache.Values)
                pso.Dispose();
            shader.PsoCache.Clear();
        }

        shader.RootSignature.Dispose();
        shader.VertexShaderBlob.Dispose();
        shader.PixelShaderBlob.Dispose();
        shader = default;
    }

    public void BindShader(nuint handle)
    {
        ref var shader = ref _shaders[(int)handle];
        if (shader.RootSignature.Handle == null) return;

        _boundShader = handle;
        _commandList.SetGraphicsRootSignature(shader.RootSignature);
    }

    public void SetBlendMode(BlendMode mode)
    {
        _currentBlendMode = mode;
        // PSO will be selected/created in DrawElements based on current state
    }

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        if (_boundShader == 0 || _boundMesh == 0) return;

        // Get or create PSO for current state
        ref var shader = ref _shaders[(int)_boundShader];
        ref var mesh = ref _meshes[(int)_boundMesh];

        var psoKey = new PsoKey
        {
            ShaderHandle = _boundShader,
            BlendMode = _currentBlendMode,
            VertexStride = mesh.Stride
        };

        if (!shader.PsoCache.TryGetValue(psoKey, out var pso))
        {
            pso = CreatePso(ref shader, mesh.Stride);
            shader.PsoCache[psoKey] = pso;
        }

        _commandList.SetPipelineState(pso);
        _commandList.DrawIndexedInstanced((uint)indexCount, 1, (uint)firstIndex, baseVertex, 0);
    }

    // === Fence Management ===

    public nuint CreateFence()
    {
        ComPtr<ID3D12Fence> fence = default;
        var hr = _device.CreateFence(0, FenceFlags.None,
            SilkMarshal.GuidPtrOf<ID3D12Fence>(), (void**)&fence);

        if (hr < 0)
            throw new Exception($"Failed to create fence: 0x{hr:X8}");

        var fenceEvent = CreateEventW(null, 0, 0, null);

        var handle = _nextFenceId++;
        _fences[handle] = new FenceInfo
        {
            Fence = fence,
            Value = 0,
            Event = fenceEvent
        };

        return (nuint)handle;
    }

    public void WaitFence(nuint fence)
    {
        ref var fenceInfo = ref _fences[(int)fence];
        if (fenceInfo.Fence.Handle == null) return;

        if (fenceInfo.Fence.GetCompletedValue() < fenceInfo.Value)
        {
            fenceInfo.Fence.SetEventOnCompletion(fenceInfo.Value, (void*)fenceInfo.Event);
            WaitForSingleObject(fenceInfo.Event, 0xFFFFFFFF);
        }
    }

    public void DeleteFence(nuint fence)
    {
        ref var fenceInfo = ref _fences[(int)fence];
        fenceInfo.Fence.Dispose();
        if (fenceInfo.Event != nint.Zero)
            CloseHandle(fenceInfo.Event);
        fenceInfo = default;
    }

    // === Private Helpers ===

    private ComPtr<ID3D10Blob> CompileShader(string name, string source, string entryPoint, string target)
    {
        var sourceBytes = Encoding.UTF8.GetBytes(source);

        ID3D10Blob* shaderBlobPtr = null;
        ID3D10Blob* errorBlobPtr = null;

        fixed (byte* pSource = sourceBytes)
        fixed (byte* pEntryPoint = Encoding.ASCII.GetBytes(entryPoint + "\0"))
        fixed (byte* pTarget = Encoding.ASCII.GetBytes(target + "\0"))
        {
            var hr = _d3dCompiler.Compile(
                pSource,
                (nuint)sourceBytes.Length,
                (byte*)null, // Source name
                null, // Defines
                (ID3DInclude*)null, // Include handler
                pEntryPoint,
                pTarget,
                0, // Flags1
                0, // Flags2
                &shaderBlobPtr,
                &errorBlobPtr);

            if (hr < 0)
            {
                var errorMessage = "";
                if (errorBlobPtr != null)
                {
                    var errorPtr = errorBlobPtr->GetBufferPointer();
                    var errorSize = errorBlobPtr->GetBufferSize();
                    errorMessage = Encoding.UTF8.GetString((byte*)errorPtr, (int)errorSize);
                    errorBlobPtr->Release();
                }
                throw new Exception($"[{name}] Shader compilation failed ({target}): {errorMessage}");
            }

            if (errorBlobPtr != null)
                errorBlobPtr->Release();
        }

        return new ComPtr<ID3D10Blob>(shaderBlobPtr);
    }

    private ComPtr<ID3D12RootSignature> CreateRootSignature()
    {
        // Root signature layout:
        // [0] Root constants (24 DWORDs = 96 bytes): projection matrix (16) + misc uniforms (8)
        // [1] Descriptor table: SRV (textures)
        // [2] Descriptor table: CBV (uniform buffers)
        // [3] Descriptor table: Samplers

        var rootParams = stackalloc RootParameter[4];

        // Parameter 0: Root constants
        rootParams[0] = new RootParameter
        {
            ParameterType = RootParameterType.Type32BitConstants,
            Anonymous = new RootParameterUnion
            {
                Constants = new RootConstants
                {
                    ShaderRegister = 0,
                    RegisterSpace = 0,
                    Num32BitValues = 24 // 16 for matrix + 8 for other uniforms
                }
            },
            ShaderVisibility = ShaderVisibility.All
        };

        // Parameter 1: SRV descriptor table (textures)
        var srvRange = new DescriptorRange
        {
            RangeType = DescriptorRangeType.Srv,
            NumDescriptors = 8, // Up to 8 textures
            BaseShaderRegister = 0,
            RegisterSpace = 0,
            OffsetInDescriptorsFromTableStart = 0xFFFFFFFF // D3D12_DESCRIPTOR_RANGE_OFFSET_APPEND
        };

        rootParams[1] = new RootParameter
        {
            ParameterType = RootParameterType.TypeDescriptorTable,
            Anonymous = new RootParameterUnion
            {
                DescriptorTable = new RootDescriptorTable
                {
                    NumDescriptorRanges = 1,
                    PDescriptorRanges = &srvRange
                }
            },
            ShaderVisibility = ShaderVisibility.Pixel
        };

        // Parameter 2: CBV descriptor table (uniform buffers)
        var cbvRange = new DescriptorRange
        {
            RangeType = DescriptorRangeType.Cbv,
            NumDescriptors = 4, // Up to 4 CBVs
            BaseShaderRegister = 1, // b1, b2, b3, b4 (b0 is root constants)
            RegisterSpace = 0,
            OffsetInDescriptorsFromTableStart = 0xFFFFFFFF
        };

        rootParams[2] = new RootParameter
        {
            ParameterType = RootParameterType.TypeDescriptorTable,
            Anonymous = new RootParameterUnion
            {
                DescriptorTable = new RootDescriptorTable
                {
                    NumDescriptorRanges = 1,
                    PDescriptorRanges = &cbvRange
                }
            },
            ShaderVisibility = ShaderVisibility.All
        };

        // Parameter 3: Sampler descriptor table
        var samplerRange = new DescriptorRange
        {
            RangeType = DescriptorRangeType.Sampler,
            NumDescriptors = 2, // Linear and point samplers
            BaseShaderRegister = 0,
            RegisterSpace = 0,
            OffsetInDescriptorsFromTableStart = 0xFFFFFFFF
        };

        rootParams[3] = new RootParameter
        {
            ParameterType = RootParameterType.TypeDescriptorTable,
            Anonymous = new RootParameterUnion
            {
                DescriptorTable = new RootDescriptorTable
                {
                    NumDescriptorRanges = 1,
                    PDescriptorRanges = &samplerRange
                }
            },
            ShaderVisibility = ShaderVisibility.Pixel
        };

        var rootSigDesc = new RootSignatureDesc
        {
            NumParameters = 4,
            PParameters = rootParams,
            NumStaticSamplers = 0,
            PStaticSamplers = null,
            Flags = RootSignatureFlags.AllowInputAssemblerInputLayout
        };

        ID3D10Blob* signatureBlobPtr = null;
        ID3D10Blob* errorBlobPtr = null;

        var hr = _d3d12.SerializeRootSignature(&rootSigDesc, D3DRootSignatureVersion.Version1,
            &signatureBlobPtr, &errorBlobPtr);

        if (hr < 0)
        {
            var error = errorBlobPtr != null
                ? Encoding.UTF8.GetString((byte*)errorBlobPtr->GetBufferPointer(), (int)errorBlobPtr->GetBufferSize())
                : "";
            if (errorBlobPtr != null)
                errorBlobPtr->Release();
            throw new Exception($"Failed to serialize root signature: {error}");
        }

        if (errorBlobPtr != null)
            errorBlobPtr->Release();

        ComPtr<ID3D12RootSignature> rootSignature = default;
        hr = _device.CreateRootSignature(
            0,
            signatureBlobPtr->GetBufferPointer(),
            signatureBlobPtr->GetBufferSize(),
            SilkMarshal.GuidPtrOf<ID3D12RootSignature>(),
            (void**)&rootSignature);

        signatureBlobPtr->Release();

        if (hr < 0)
            throw new Exception($"Failed to create root signature: 0x{hr:X8}");

        return rootSignature;
    }

    private ComPtr<ID3D12PipelineState> CreatePso(ref ShaderInfo shader, int vertexStride)
    {
        // Input layout - matches MeshVertex structure
        var inputElements = stackalloc InputElementDesc[10];

        inputElements[0] = CreateInputElement("POSITION", 0, Format.FormatR32G32Float, 0);
        inputElements[1] = CreateInputElement("TEXCOORD", 0, Format.FormatR32G32Float, 8);
        inputElements[2] = CreateInputElement("NORMAL", 0, Format.FormatR32G32Float, 16);
        inputElements[3] = CreateInputElement("COLOR", 0, Format.FormatR32G32B32A32Float, 24);
        inputElements[4] = CreateInputElement("BLENDINDICES", 0, Format.FormatR32Sint, 40);
        inputElements[5] = CreateInputElement("BLENDINDICES", 1, Format.FormatR32Sint, 44);
        inputElements[6] = CreateInputElement("BLENDINDICES", 2, Format.FormatR32Sint, 48);
        inputElements[7] = CreateInputElement("TEXCOORD", 1, Format.FormatR32Float, 52);
        inputElements[8] = CreateInputElement("TEXCOORD", 2, Format.FormatR32Float, 56);
        inputElements[9] = CreateInputElement("TEXCOORD", 3, Format.FormatR32Float, 60);

        // Blend state
        var blendDesc = new BlendDesc
        {
            AlphaToCoverageEnable = 0,
            IndependentBlendEnable = 0
        };

        ref var rt0 = ref blendDesc.RenderTarget[0];
        rt0.RenderTargetWriteMask = 0xF; // D3D12_COLOR_WRITE_ENABLE_ALL

        switch (_currentBlendMode)
        {
            case BlendMode.None:
                rt0.BlendEnable = 0;
                break;

            case BlendMode.Alpha:
                rt0.BlendEnable = 1;
                rt0.SrcBlend = Blend.SrcAlpha;
                rt0.DestBlend = Blend.InvSrcAlpha;
                rt0.BlendOp = BlendOp.Add;
                rt0.SrcBlendAlpha = Blend.One;
                rt0.DestBlendAlpha = Blend.InvSrcAlpha;
                rt0.BlendOpAlpha = BlendOp.Add;
                break;

            case BlendMode.Additive:
                rt0.BlendEnable = 1;
                rt0.SrcBlend = Blend.SrcAlpha;
                rt0.DestBlend = Blend.One;
                rt0.BlendOp = BlendOp.Add;
                rt0.SrcBlendAlpha = Blend.One;
                rt0.DestBlendAlpha = Blend.One;
                rt0.BlendOpAlpha = BlendOp.Add;
                break;

            case BlendMode.Multiply:
                rt0.BlendEnable = 1;
                rt0.SrcBlend = Blend.DestColor;
                rt0.DestBlend = Blend.Zero;
                rt0.BlendOp = BlendOp.Add;
                rt0.SrcBlendAlpha = Blend.One;
                rt0.DestBlendAlpha = Blend.Zero;
                rt0.BlendOpAlpha = BlendOp.Add;
                break;

            case BlendMode.Premultiplied:
                rt0.BlendEnable = 1;
                rt0.SrcBlend = Blend.One;
                rt0.DestBlend = Blend.InvSrcAlpha;
                rt0.BlendOp = BlendOp.Add;
                rt0.SrcBlendAlpha = Blend.One;
                rt0.DestBlendAlpha = Blend.InvSrcAlpha;
                rt0.BlendOpAlpha = BlendOp.Add;
                break;
        }

        // Rasterizer state
        var rasterizerDesc = new RasterizerDesc
        {
            FillMode = FillMode.Solid,
            CullMode = CullMode.None,
            FrontCounterClockwise = 0,
            DepthBias = 0,
            DepthBiasClamp = 0.0f,
            SlopeScaledDepthBias = 0.0f,
            DepthClipEnable = 1,
            MultisampleEnable = 0,
            AntialiasedLineEnable = 0,
            ForcedSampleCount = 0,
            ConservativeRaster = ConservativeRasterizationMode.Off
        };

        // Depth stencil state (disabled for 2D)
        var depthStencilDesc = new DepthStencilDesc
        {
            DepthEnable = 0,
            DepthWriteMask = DepthWriteMask.Zero,
            DepthFunc = ComparisonFunc.Always,
            StencilEnable = 0
        };

        var psoDesc = new GraphicsPipelineStateDesc
        {
            PRootSignature = shader.RootSignature,
            VS = new ShaderBytecode
            {
                PShaderBytecode = shader.VertexShaderBlob.GetBufferPointer(),
                BytecodeLength = shader.VertexShaderBlob.GetBufferSize()
            },
            PS = new ShaderBytecode
            {
                PShaderBytecode = shader.PixelShaderBlob.GetBufferPointer(),
                BytecodeLength = shader.PixelShaderBlob.GetBufferSize()
            },
            BlendState = blendDesc,
            SampleMask = uint.MaxValue,
            RasterizerState = rasterizerDesc,
            DepthStencilState = depthStencilDesc,
            InputLayout = new InputLayoutDesc
            {
                PInputElementDescs = inputElements,
                NumElements = 10
            },
            PrimitiveTopologyType = PrimitiveTopologyType.Triangle,
            NumRenderTargets = 1,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 }
        };

        psoDesc.RTVFormats[0] = Format.FormatR8G8B8A8Unorm;
        psoDesc.DSVFormat = Format.FormatUnknown;

        ComPtr<ID3D12PipelineState> pso = default;
        var hr = _device.CreateGraphicsPipelineState(&psoDesc, SilkMarshal.GuidPtrOf<ID3D12PipelineState>(), (void**)&pso);

        if (hr < 0)
            throw new Exception($"Failed to create PSO: 0x{hr:X8}");

        return pso;
    }

    // Pre-allocated semantic name strings (must be persistent)
    private static readonly byte[] s_positionSemantic = "POSITION\0"u8.ToArray();
    private static readonly byte[] s_texcoordSemantic = "TEXCOORD\0"u8.ToArray();
    private static readonly byte[] s_normalSemantic = "NORMAL\0"u8.ToArray();
    private static readonly byte[] s_colorSemantic = "COLOR\0"u8.ToArray();
    private static readonly byte[] s_blendindicesSemantic = "BLENDINDICES\0"u8.ToArray();

    private static InputElementDesc CreateInputElement(string semantic, uint index, Format format, uint offset)
    {
        byte* semanticName = semantic switch
        {
            "POSITION" => (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(s_positionSemantic, 0),
            "TEXCOORD" => (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(s_texcoordSemantic, 0),
            "NORMAL" => (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(s_normalSemantic, 0),
            "COLOR" => (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(s_colorSemantic, 0),
            "BLENDINDICES" => (byte*)Marshal.UnsafeAddrOfPinnedArrayElement(s_blendindicesSemantic, 0),
            _ => null
        };

        return new InputElementDesc
        {
            SemanticName = semanticName,
            SemanticIndex = index,
            Format = format,
            InputSlot = 0,
            AlignedByteOffset = offset,
            InputSlotClass = InputClassification.PerVertexData,
            InstanceDataStepRate = 0
        };
    }
}
