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
        // Create vertex shader module
        var vertexModule = CreateShaderModule(vertexSource, $"{name}_vertex");

        // Create fragment shader module
        var fragmentModule = CreateShaderModule(fragmentSource, $"{name}_fragment");

        // Create bind group layout
        var bindGroupLayout = CreateBindGroupLayout();

        // Create pipeline layout
        var pipelineLayout = CreatePipelineLayout(bindGroupLayout);

        var handle = (nuint)_nextShaderId++;
        _shaders[(int)handle] = new ShaderInfo
        {
            VertexModule = vertexModule,
            FragmentModule = fragmentModule,
            BindGroupLayout0 = bindGroupLayout,
            PipelineLayout = pipelineLayout,
            PsoCache = new Dictionary<PsoKey, nint>()
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

    private BindGroupLayout* CreateBindGroupLayout()
    {
        // Bind group layout for sprite shader:
        // @binding(0) - Globals uniform buffer
        // @binding(1) - Bone texture
        // @binding(2) - Bone sampler
        // @binding(3) - Texture array
        // @binding(4) - Texture sampler
        var entries = stackalloc BindGroupLayoutEntry[5];

        // Binding 0: Globals uniform buffer
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

        // Binding 1: Bone texture
        entries[1] = new BindGroupLayoutEntry
        {
            Binding = 1,
            Visibility = ShaderStage.Vertex,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2D,
            },
        };

        // Binding 2: Bone sampler
        entries[2] = new BindGroupLayoutEntry
        {
            Binding = 2,
            Visibility = ShaderStage.Vertex,
            Sampler = new SamplerBindingLayout
            {
                Type = SamplerBindingType.Filtering,
            },
        };

        // Binding 3: Texture array
        entries[3] = new BindGroupLayoutEntry
        {
            Binding = 3,
            Visibility = ShaderStage.Fragment,
            Texture = new TextureBindingLayout
            {
                SampleType = TextureSampleType.Float,
                ViewDimension = TextureViewDimension.Dimension2DArray,
            },
        };

        // Binding 4: Texture sampler
        entries[4] = new BindGroupLayoutEntry
        {
            Binding = 4,
            Visibility = ShaderStage.Fragment,
            Sampler = new SamplerBindingLayout
            {
                Type = SamplerBindingType.Filtering,
            },
        };

        var bindGroupLayoutDesc = new BindGroupLayoutDescriptor
        {
            EntryCount = 5,
            Entries = entries,
        };

        return _wgpu.DeviceCreateBindGroupLayout(_device, &bindGroupLayoutDesc);
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

        // Depth/stencil state (disabled for sprite rendering)
        var depthStencilState = new DepthStencilState
        {
            Format = Silk.NET.WebGPU.TextureFormat.Depth24PlusStencil8,
            DepthWriteEnabled = false,
            DepthCompare = CompareFunction.Always,
        };

        // Create render pipeline
        var pipelineDesc = new RenderPipelineDescriptor
        {
            Layout = shaderInfo.PipelineLayout,
            Vertex = vertexState,
            Fragment = &fragmentState,
            Primitive = primitiveState,
            Multisample = multisampleState,
            DepthStencil = &depthStencilState,
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
