//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using NoZ.Platform;
using WGPUVertexAttribute = Silk.NET.WebGPU.VertexAttribute;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    public nuint CreateShader(string name, string vertexSource, string fragmentSource)
    {
        // Legacy path: Parse WGSL to detect bindings (used when no metadata available)
        var vertexModule = CreateShaderModule(vertexSource, $"{name}_vertex");
        var fragmentModule = CreateShaderModule(fragmentSource, $"{name}_fragment");

        // Detect shader type from both vertex and fragment source to create appropriate bind group layout
        int bindingCount;
        var bindGroupLayout = CreateBindGroupLayoutForShader(vertexSource + fragmentSource, out bindingCount);

        var pipelineLayout = CreatePipelineLayout(bindGroupLayout);
        var handle = (nuint)_nextShaderId++;

        Log.Debug($"CreateShader (legacy): name={name}, bindingCount={bindingCount}");

        _shaders[(int)handle] = new ShaderInfo
        {
            VertexModule = vertexModule,
            FragmentModule = fragmentModule,
            BindGroupLayout0 = bindGroupLayout,
            PipelineLayout = pipelineLayout,
            PsoCache = new Dictionary<PsoKey, nint>(),
            BindGroupEntryCount = bindingCount
        };

        return handle;
    }

    internal nuint CreateShaderFromMetadata(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings)
    {
        // New path: Use pre-computed metadata from asset pipeline
        var vertexModule = CreateShaderModule(vertexSource, $"{name}_vertex");
        var fragmentModule = CreateShaderModule(fragmentSource, $"{name}_fragment");

        // Create bind group layout from metadata instead of parsing
        var bindGroupLayout = CreateBindGroupLayoutFromMetadata(bindings, out int bindingCount);
        var pipelineLayout = CreatePipelineLayout(bindGroupLayout);

        var handle = (nuint)_nextShaderId++;

        Log.Debug($"CreateShader (metadata): name={name}, bindingCount={bindingCount}");

        _shaders[(int)handle] = new ShaderInfo
        {
            VertexModule = vertexModule,
            FragmentModule = fragmentModule,
            BindGroupLayout0 = bindGroupLayout,
            PipelineLayout = pipelineLayout,
            PsoCache = new Dictionary<PsoKey, nint>(),
            BindGroupEntryCount = bindingCount
        };

        return handle;
    }

    private ShaderModule* CreateShaderModule(string source, string label)
    {
        fixed (byte* sourcePtr = System.Text.Encoding.UTF8.GetBytes(source))
        {
            var wgslDesc = new ShaderModuleWGSLDescriptor
            {
                Chain = new ChainedStruct
                {
                    SType = SType.ShaderModuleWgslDescriptor,
                },
                Code = sourcePtr,
            };

            var shaderModuleDesc = new ShaderModuleDescriptor
            {
                Label = (byte*)Marshal.StringToHGlobalAnsi(label),
                NextInChain = (ChainedStruct*)(&wgslDesc),
            };

            return _wgpu.DeviceCreateShaderModule(_device, &shaderModuleDesc);
        }
    }

    private enum BindingType
    {
        UniformBuffer,
        Texture2D,
        Texture2DArray,
        Sampler
    }

    private BindGroupLayout* CreateBindGroupLayoutFromMetadata(List<ShaderBinding> bindings, out int bindingCount)
    {
        // Use metadata to create bind group layout
        bindingCount = bindings.Count;

        var entries = stackalloc BindGroupLayoutEntry[bindingCount];

        for (int i = 0; i < bindingCount; i++)
        {
            var binding = bindings[i];
            var bindingType = binding.Type switch
            {
                ShaderBindingType.UniformBuffer => BindingType.UniformBuffer,
                ShaderBindingType.Texture2D => BindingType.Texture2D,
                ShaderBindingType.Texture2DArray => BindingType.Texture2DArray,
                ShaderBindingType.Sampler => BindingType.Sampler,
                _ => throw new NotSupportedException($"Binding type {binding.Type} not supported")
            };

            entries[i] = CreateBindGroupLayoutEntry(binding.Binding, bindingType);

            Log.Debug($"Binding {binding.Binding}: type={bindingType}, name={binding.Name}");
        }

        var bindGroupLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = (uint)bindingCount,
            Entries = entries,
        };

        return _wgpu.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDesc);
    }

    private BindGroupLayout* CreateBindGroupLayoutForShader(string shaderSource, out int bindingCount)
    {
        // Legacy path: Detect what bindings the shader actually uses by parsing
        bool hasBinding1 = shaderSource.Contains("@binding(1)");
        bool hasBinding2 = shaderSource.Contains("@binding(2)");
        bool hasBinding3 = shaderSource.Contains("@binding(3)");
        bool hasBinding4 = shaderSource.Contains("@binding(4)");

        // Count how many bindings we need
        bindingCount = 1; // Always have binding 0 (globals)
        if (hasBinding1) bindingCount = 2;
        if (hasBinding2) bindingCount = 3;
        if (hasBinding3) bindingCount = 4;
        if (hasBinding4) bindingCount = 5;

        // Detect binding types by parsing WGSL declarations
        var binding1Type = DetectBindingType(shaderSource, 1);
        var binding2Type = DetectBindingType(shaderSource, 2);
        var binding3Type = DetectBindingType(shaderSource, 3);
        var binding4Type = DetectBindingType(shaderSource, 4);

        var entries = stackalloc BindGroupLayoutEntry[bindingCount];

        // Binding 0: Globals uniform buffer (required by all shaders)
        entries[0] = new BindGroupLayoutEntry
        {
            Binding = 0,
            Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
            Buffer = new BufferBindingLayout
            {
                Type = BufferBindingType.Uniform,
                MinBindingSize = 0,
            },
        };

        // Binding 1
        if (bindingCount >= 2)
        {
            entries[1] = CreateBindGroupLayoutEntry(1, binding1Type);
        }

        // Binding 2
        if (bindingCount >= 3)
        {
            entries[2] = CreateBindGroupLayoutEntry(2, binding2Type);
        }

        // Binding 3
        if (bindingCount >= 4)
        {
            entries[3] = CreateBindGroupLayoutEntry(3, binding3Type);
        }

        // Binding 4
        if (bindingCount >= 5)
        {
            entries[4] = CreateBindGroupLayoutEntry(4, binding4Type);
        }

        Log.Debug($"CreateBindGroupLayout (legacy): bindingCount={bindingCount}, b1={binding1Type}, b2={binding2Type}, b3={binding3Type}, b4={binding4Type}");

        var bindGroupLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = (uint)bindingCount,
            Entries = entries,
        };

        return _wgpu.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDesc);
    }

    private BindGroupLayoutEntry CreateBindGroupLayoutEntry(uint binding, BindingType type)
    {
        return type switch
        {
            BindingType.UniformBuffer => new BindGroupLayoutEntry
            {
                Binding = binding,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Buffer = new BufferBindingLayout
                {
                    Type = BufferBindingType.Uniform,
                    MinBindingSize = 0,
                },
            },
            BindingType.Texture2D => new BindGroupLayoutEntry
            {
                Binding = binding,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2D,
                },
            },
            BindingType.Texture2DArray => new BindGroupLayoutEntry
            {
                Binding = binding,
                Visibility = ShaderStage.Fragment,
                Texture = new TextureBindingLayout
                {
                    SampleType = TextureSampleType.Float,
                    ViewDimension = TextureViewDimension.Dimension2DArray,
                },
            },
            BindingType.Sampler => new BindGroupLayoutEntry
            {
                Binding = binding,
                Visibility = ShaderStage.Vertex | ShaderStage.Fragment,
                Sampler = new SamplerBindingLayout
                {
                    Type = SamplerBindingType.Filtering,
                },
            },
            _ => throw new NotSupportedException($"Binding type {type} not supported"),
        };
    }

    private BindingType DetectBindingType(string shaderSource, int bindingNumber)
    {
        var bindingMarker = $"@binding({bindingNumber})";
        var bindingIndex = shaderSource.IndexOf(bindingMarker);

        if (bindingIndex == -1)
            return BindingType.UniformBuffer; // Default fallback

        // Find the line containing this binding (search forward until semicolon or newline)
        var searchStart = bindingIndex;
        var searchEnd = shaderSource.IndexOf(';', searchStart);
        if (searchEnd == -1)
            searchEnd = shaderSource.IndexOf('\n', searchStart);
        if (searchEnd == -1)
            searchEnd = shaderSource.Length;

        var bindingDeclaration = shaderSource.Substring(searchStart, searchEnd - searchStart);

        // Detect binding type from declaration
        if (bindingDeclaration.Contains("texture_2d_array"))
            return BindingType.Texture2DArray;
        if (bindingDeclaration.Contains("texture_2d"))
            return BindingType.Texture2D;
        if (bindingDeclaration.Contains("sampler"))
            return BindingType.Sampler;
        if (bindingDeclaration.Contains("var<uniform>"))
            return BindingType.UniformBuffer;

        // Default fallback
        return BindingType.UniformBuffer;
    }

    private PipelineLayout* CreatePipelineLayout(BindGroupLayout* bindGroupLayout)
    {
        var pipelineLayoutDesc = new PipelineLayoutDescriptor
        {
            BindGroupLayoutCount = 1,
            BindGroupLayouts = &bindGroupLayout,
        };

        return _wgpu.DeviceCreatePipelineLayout(_device, &pipelineLayoutDesc);
    }

    private RenderPipeline* GetOrCreatePipeline(nuint shaderHandle, BlendMode blendMode, int vertexStride)
    {
        ref var shaderInfo = ref _shaders[(int)shaderHandle];
        var key = new PsoKey { ShaderHandle = shaderHandle, BlendMode = blendMode, VertexStride = vertexStride };

        if (shaderInfo.PsoCache.TryGetValue(key, out var pipelinePtr))
            return (RenderPipeline*)pipelinePtr;

        // Get mesh info for vertex format
        ref var meshInfo = ref _meshes[(int)_state.BoundMesh];

        // Create render pipeline
        var pipeline = CreateRenderPipeline(shaderInfo, blendMode, meshInfo.Descriptor);
        shaderInfo.PsoCache[key] = (nint)pipeline;

        return pipeline;
    }

    private RenderPipeline* CreateRenderPipeline(ShaderInfo shaderInfo, BlendMode blendMode, VertexFormatDescriptor vertexDescriptor)
    {
        // Build vertex attributes from descriptor
        var attributeCount = vertexDescriptor.Attributes.Length;
        var attributes = stackalloc WGPUVertexAttribute[attributeCount];
        for (int i = 0; i < attributeCount; i++)
        {
            attributes[i] = MapVertexAttribute(vertexDescriptor.Attributes[i]);
        }

        // Vertex buffer layout
        var vertexBufferLayout = new VertexBufferLayout
        {
            ArrayStride = (ulong)vertexDescriptor.Stride,
            StepMode = VertexStepMode.Vertex,
            AttributeCount = (uint)attributeCount,
            Attributes = attributes,
        };

        // Vertex state
        var vertexState = new VertexState
        {
            Module = shaderInfo.VertexModule,
            EntryPoint = (byte*)Marshal.StringToHGlobalAnsi("vs_main"),
            BufferCount = 1,
            Buffers = &vertexBufferLayout,
        };

        // Blend state
        var blendState = MapBlendMode(blendMode);
        var colorTargetState = new ColorTargetState
        {
            Format = _surfaceFormat,
            Blend = &blendState,
            WriteMask = ColorWriteMask.All,
        };

        // Fragment state
        var fragmentState = new FragmentState
        {
            Module = shaderInfo.FragmentModule,
            EntryPoint = (byte*)Marshal.StringToHGlobalAnsi("fs_main"),
            TargetCount = 1,
            Targets = &colorTargetState,
        };

        // Primitive state
        var primitiveState = new PrimitiveState
        {
            Topology = PrimitiveTopology.TriangleList,
            FrontFace = FrontFace.Ccw,
            CullMode = CullMode.None,
        };

        // Multisample state
        var multisampleState = new MultisampleState
        {
            Count = (uint)_msaaSamples,
            Mask = ~0u,
            AlphaToCoverageEnabled = false,
        };

        // Create render pipeline
        var pipelineDesc = new RenderPipelineDescriptor
        {
            Layout = shaderInfo.PipelineLayout,
            Vertex = vertexState,
            Fragment = &fragmentState,
            Primitive = primitiveState,
            Multisample = multisampleState,
            DepthStencil = null, // No depth buffer for now
        };

        return _wgpu.DeviceCreateRenderPipeline(_device, &pipelineDesc);
    }

    private WGPUVertexAttribute MapVertexAttribute(NoZ.VertexAttribute attr)
    {
        var format = attr.Type switch
        {
            VertexAttribType.Float when attr.Components == 1 => Silk.NET.WebGPU.VertexFormat.Float32,
            VertexAttribType.Float when attr.Components == 2 => Silk.NET.WebGPU.VertexFormat.Float32x2,
            VertexAttribType.Float when attr.Components == 3 => Silk.NET.WebGPU.VertexFormat.Float32x3,
            VertexAttribType.Float when attr.Components == 4 => Silk.NET.WebGPU.VertexFormat.Float32x4,
            VertexAttribType.Int when attr.Components == 1 => Silk.NET.WebGPU.VertexFormat.Sint32,
            VertexAttribType.Int when attr.Components == 2 => Silk.NET.WebGPU.VertexFormat.Sint32x2,
            VertexAttribType.Int when attr.Components == 3 => Silk.NET.WebGPU.VertexFormat.Sint32x3,
            VertexAttribType.Int when attr.Components == 4 => Silk.NET.WebGPU.VertexFormat.Sint32x4,
            VertexAttribType.UByte when attr.Components == 4 && attr.Normalized => Silk.NET.WebGPU.VertexFormat.Unorm8x4,
            VertexAttribType.UByte when attr.Components == 4 => Silk.NET.WebGPU.VertexFormat.Uint8x4,
            _ => throw new NotSupportedException($"Vertex attribute type {attr.Type} with {attr.Components} components not supported"),
        };

        return new WGPUVertexAttribute
        {
            Format = format,
            Offset = (ulong)attr.Offset,
            ShaderLocation = (uint)attr.Location,
        };
    }

    private BlendState MapBlendMode(BlendMode mode)
    {
        return mode switch
        {
            BlendMode.None => new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.Zero,
                    Operation = BlendOperation.Add,
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.Zero,
                    Operation = BlendOperation.Add,
                },
            },
            BlendMode.Alpha => new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.SrcAlpha,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add,
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add,
                },
            },
            BlendMode.Additive => new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.SrcAlpha,
                    DstFactor = BlendFactor.One,
                    Operation = BlendOperation.Add,
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.SrcAlpha,
                    DstFactor = BlendFactor.One,
                    Operation = BlendOperation.Add,
                },
            },
            BlendMode.Multiply => new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.Dst,
                    DstFactor = BlendFactor.Zero,
                    Operation = BlendOperation.Add,
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.Dst,
                    DstFactor = BlendFactor.Zero,
                    Operation = BlendOperation.Add,
                },
            },
            BlendMode.Premultiplied => new BlendState
            {
                Color = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add,
                },
                Alpha = new BlendComponent
                {
                    SrcFactor = BlendFactor.One,
                    DstFactor = BlendFactor.OneMinusSrcAlpha,
                    Operation = BlendOperation.Add,
                },
            },
            _ => throw new NotSupportedException($"Blend mode {mode} not supported"),
        };
    }

    public void DestroyShader(nuint handle)
    {
        ref var shaderInfo = ref _shaders[(int)handle];

        // Release all cached pipelines
        foreach (var pipeline in shaderInfo.PsoCache.Values)
        {
            _wgpu.RenderPipelineRelease((RenderPipeline*)pipeline);
        }
        shaderInfo.PsoCache.Clear();

        // Release pipeline layout and bind group layout
        if (shaderInfo.PipelineLayout != null)
            _wgpu.PipelineLayoutRelease(shaderInfo.PipelineLayout);

        if (shaderInfo.BindGroupLayout0 != null)
            _wgpu.BindGroupLayoutRelease(shaderInfo.BindGroupLayout0);

        // Release shader modules
        if (shaderInfo.VertexModule != null)
            _wgpu.ShaderModuleRelease(shaderInfo.VertexModule);

        if (shaderInfo.FragmentModule != null)
            _wgpu.ShaderModuleRelease(shaderInfo.FragmentModule);

        _shaders[(int)handle] = default;
    }

    public void BindShader(nuint handle)
    {
        if (_state.BoundShader == handle)
            return;

        _state.BoundShader = handle;
        _state.PipelineDirty = true;
    }
}
