//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WGPUBuffer = Silk.NET.WebGPU.Buffer;
using WGPUTexture = Silk.NET.WebGPU.Texture;
using WGPUTextureFormat = Silk.NET.WebGPU.TextureFormat;
using WGPUBufferUsage = Silk.NET.WebGPU.BufferUsage;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver
{
    // Mesh Management
    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        var descriptor = T.GetFormatDescriptor();
        var vertexSize = descriptor.Stride * maxVertices;
        var indexSize = sizeof(ushort) * maxIndices;

        // Create vertex buffer
        var vertexBufferDesc = new BufferDescriptor
        {
            Label = (byte*)Marshal.StringToHGlobalAnsi($"{name}_vertices"),
            Size = (ulong)vertexSize,
            Usage = WGPUBufferUsage.Vertex | WGPUBufferUsage.CopyDst,
            MappedAtCreation = false,
        };
        var vertexBuffer = _wgpu.DeviceCreateBuffer(_device, &vertexBufferDesc);

        // Create index buffer
        var indexBufferDesc = new BufferDescriptor
        {
            Label = (byte*)Marshal.StringToHGlobalAnsi($"{name}_indices"),
            Size = (ulong)indexSize,
            Usage = WGPUBufferUsage.Index | WGPUBufferUsage.CopyDst,
            MappedAtCreation = false,
        };
        var indexBuffer = _wgpu.DeviceCreateBuffer(_device, &indexBufferDesc);

        var handle = (nuint)_nextMeshId++;
        _meshes[(int)handle] = new MeshInfo
        {
            VertexBuffer = vertexBuffer,
            IndexBuffer = indexBuffer,
            Stride = descriptor.Stride,
            MaxVertices = maxVertices,
            MaxIndices = maxIndices,
            Descriptor = descriptor,
        };

        return handle;
    }

    public void DestroyMesh(nuint handle)
    {
        ref var mesh = ref _meshes[(int)handle];

        if (mesh.VertexBuffer != null)
        {
            _wgpu.BufferRelease(mesh.VertexBuffer);
            mesh.VertexBuffer = null;
        }

        if (mesh.IndexBuffer != null)
        {
            _wgpu.BufferRelease(mesh.IndexBuffer);
            mesh.IndexBuffer = null;
        }
    }

    public void BindMesh(nuint handle)
    {
        if (_state.BoundMesh == handle)
            return;

        _state.BoundMesh = handle;
        _state.PipelineDirty = true; // Vertex format affects pipeline
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        ref var mesh = ref _meshes[(int)handle];

        // Update vertex buffer
        if (vertexData.Length > 0)
        {
            fixed (byte* dataPtr = vertexData)
            {
                _wgpu.QueueWriteBuffer(_queue, mesh.VertexBuffer, 0, dataPtr, (nuint)vertexData.Length);
            }
        }

        // Update index buffer
        if (indexData.Length > 0)
        {
            fixed (ushort* dataPtr = indexData)
            {
                _wgpu.QueueWriteBuffer(_queue, mesh.IndexBuffer, 0, dataPtr, (nuint)(indexData.Length * sizeof(ushort)));
            }
        }
    }

    // Buffer Management
    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        var bufferDesc = new BufferDescriptor
        {
            Label = (byte*)Marshal.StringToHGlobalAnsi(name),
            Size = (ulong)sizeInBytes,
            Usage = WGPUBufferUsage.Uniform | WGPUBufferUsage.CopyDst,
            MappedAtCreation = false,
        };
        var buffer = _wgpu.DeviceCreateBuffer(_device, &bufferDesc);

        var handle = (nuint)_nextBufferId++;
        _buffers[(int)handle] = new BufferInfo
        {
            Buffer = buffer,
            SizeInBytes = sizeInBytes,
            Usage = usage,
        };

        return handle;
    }

    public void DestroyBuffer(nuint handle)
    {
        ref var buffer = ref _buffers[(int)handle];

        if (buffer.Buffer != null)
        {
            _wgpu.BufferRelease(buffer.Buffer);
            buffer.Buffer = null;
        }
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        ref var bufferInfo = ref _buffers[(int)buffer];

        fixed (byte* dataPtr = data)
        {
            _wgpu.QueueWriteBuffer(_queue, bufferInfo.Buffer, (ulong)offsetBytes, dataPtr, (nuint)data.Length);
        }
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        if (slot < 0 || slot >= 4)
        {
            Log.Error($"Uniform buffer slot {slot} out of range (0-3)!");
            return;
        }

        if (_state.BoundUniformBuffers[slot] == buffer)
            return;

        _state.BoundUniformBuffers[slot] = buffer;
        _state.BindGroupDirty = true;
    }

    // Texture Management
    private WGPUTextureFormat MapTextureFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8 => WGPUTextureFormat.Rgba8Unorm,
            TextureFormat.RGB8 => WGPUTextureFormat.Rgba8Unorm, // WebGPU doesn't support RGB8, use RGBA8
            TextureFormat.R8 => WGPUTextureFormat.R8Unorm,
            TextureFormat.RG8 => WGPUTextureFormat.Rgba8Unorm, // Use RGBA8 as fallback for RG8
            TextureFormat.RGBA32F => WGPUTextureFormat.Rgba32float,
            TextureFormat.BGRA8 => WGPUTextureFormat.Bgra8Unorm,
            _ => WGPUTextureFormat.Rgba8Unorm,
        };
    }

    private FilterMode MapFilterMode(TextureFilter filter)
    {
        return filter switch
        {
            TextureFilter.Point => FilterMode.Nearest,
            TextureFilter.Linear => FilterMode.Linear,
            _ => FilterMode.Linear,
        };
    }

    public nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name = null)
    {
        var wgpuFormat = MapTextureFormat(format);

        // Create texture
        var textureDesc = new TextureDescriptor
        {
            Label = (byte*)(name != null ? Marshal.StringToHGlobalAnsi(name) : IntPtr.Zero),
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = wgpuFormat,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
        };
        var texture = _wgpu.DeviceCreateTexture(_device, &textureDesc);

        // Upload texture data if provided
        if (data.Length > 0)
        {
            var bytesPerPixel = format switch
            {
                TextureFormat.RGBA8 => 4,
                TextureFormat.RGB8 => 3,
                TextureFormat.R8 => 1,
                TextureFormat.RG8 => 2,
                TextureFormat.RGBA32F => 16,
                _ => 4,
            };

            fixed (byte* dataPtr = data)
            {
                var layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = (uint)(width * bytesPerPixel),
                    RowsPerImage = (uint)height,
                };

                var copySize = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
                var destination = new ImageCopyTexture
                {
                    Texture = texture,
                    MipLevel = 0,
                    Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
                    Aspect = TextureAspect.All,
                };

                _wgpu.QueueWriteTexture(_queue, &destination, dataPtr, (nuint)data.Length, &layout, &copySize);
            }
        }

        // Create texture view
        var viewDesc = new TextureViewDescriptor
        {
            Label = (byte*)(name != null ? Marshal.StringToHGlobalAnsi($"{name}_view") : IntPtr.Zero),
            Format = wgpuFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };
        var textureView = _wgpu.TextureCreateView(texture, &viewDesc);

        // Create sampler
        var filterMode = MapFilterMode(filter);
        var samplerDesc = new SamplerDescriptor
        {
            Label = (byte*)(name != null ? Marshal.StringToHGlobalAnsi($"{name}_sampler") : IntPtr.Zero),
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = filterMode,
            MinFilter = filterMode,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0.0f,
            LodMaxClamp = 32.0f,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
        };
        var sampler = _wgpu.DeviceCreateSampler(_device, &samplerDesc);

        var handle = (nuint)_nextTextureId++;
        _textures[(int)handle] = new TextureInfo
        {
            Texture = texture,
            TextureView = textureView,
            Sampler = sampler,
            Width = width,
            Height = height,
            Layers = 1,
            Format = wgpuFormat,
            IsArray = false,
        };

        return handle;
    }

    public void UpdateTexture(nuint handle, in Vector2Int size, ReadOnlySpan<byte> data)
    {
        ref var textureInfo = ref _textures[(int)handle];

        var bytesPerPixel = textureInfo.Format switch
        {
            WGPUTextureFormat.Rgba8Unorm => 4,
            WGPUTextureFormat.R8Unorm => 1,
            WGPUTextureFormat.Rgba32float => 16,
            _ => 4,
        };

        fixed (byte* dataPtr = data)
        {
            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)(size.X * bytesPerPixel),
                RowsPerImage = (uint)size.Y,
            };

            var copySize = new Extent3D { Width = (uint)size.X, Height = (uint)size.Y, DepthOrArrayLayers = 1 };
            var destination = new ImageCopyTexture
            {
                Texture = textureInfo.Texture,
                MipLevel = 0,
                Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
                Aspect = TextureAspect.All,
            };

            _wgpu.QueueWriteTexture(_queue, &destination, dataPtr, (nuint)data.Length, &layout, &copySize);
        }
    }

    public void UpdateTextureRegion(nuint handle, in RectInt region, ReadOnlySpan<byte> data, int srcWidth = -1)
    {
        ref var textureInfo = ref _textures[(int)handle];

        var bytesPerPixel = textureInfo.Format switch
        {
            WGPUTextureFormat.Rgba8Unorm => 4,
            WGPUTextureFormat.R8Unorm => 1,
            WGPUTextureFormat.Rgba32float => 16,
            _ => 4,
        };

        var rowWidth = srcWidth < 0 ? region.Width : srcWidth;

        fixed (byte* dataPtr = data)
        {
            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)(rowWidth * bytesPerPixel),
                RowsPerImage = (uint)region.Height,
            };

            var copySize = new Extent3D { 
                Width = (uint)region.Width,
                Height = (uint)region.Height,
                DepthOrArrayLayers = 1 };
            var destination = new ImageCopyTexture
            {
                Texture = textureInfo.Texture,
                MipLevel = 0,
                Origin = new Origin3D { X = (uint)region.X, Y = (uint)region.Y, Z = 0 },
                Aspect = TextureAspect.All,
            };

            _wgpu.QueueWriteTexture(
                _queue,
                &destination,
                dataPtr + (region.Y * rowWidth + region.X) * bytesPerPixel,
                (nuint)data.Length,
                &layout,
                &copySize);
        }
    }

    public void DestroyTexture(nuint handle)
    {
        ref var texture = ref _textures[(int)handle];

        if (texture.Sampler != null)
        {
            _wgpu.SamplerRelease(texture.Sampler);
            texture.Sampler = null;
        }

        if (texture.TextureView != null)
        {
            _wgpu.TextureViewRelease(texture.TextureView);
            texture.TextureView = null;
        }

        if (texture.Texture != null)
        {
            _wgpu.TextureRelease(texture.Texture);
            texture.Texture = null;
        }
    }

    public void BindTexture(nuint handle, int slot, TextureFilter filter = TextureFilter.Point)
    {
        if (slot < 0 || slot >= 8)
        {
            Log.Error($"Texture slot {slot} out of range (0-7)!");
            return;
        }

        var filterByte = (byte)filter;
        if (_state.BoundTextures[slot] == handle && _state.TextureFilters[slot] == filterByte)
            return;

        _state.BoundTextures[slot] = handle;
        _state.TextureFilters[slot] = filterByte;
        _state.BindGroupDirty = true;
    }

    // Texture Array Management
    public nuint CreateTextureArray(int width, int height, int layers)
    {
        // Create empty texture array
        var textureDesc = new TextureDescriptor
        {
            Label = (byte*)Marshal.StringToHGlobalAnsi("TextureArray"),
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = (uint)layers },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = WGPUTextureFormat.Rgba8Unorm,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
        };
        var texture = _wgpu.DeviceCreateTexture(_device, &textureDesc);

        // Create texture view
        var viewDesc = new TextureViewDescriptor
        {
            Format = WGPUTextureFormat.Rgba8Unorm,
            Dimension = TextureViewDimension.Dimension2DArray,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = (uint)layers,
            Aspect = TextureAspect.All,
        };
        var textureView = _wgpu.TextureCreateView(texture, &viewDesc);

        // Create sampler
        var samplerDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0.0f,
            LodMaxClamp = 32.0f,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
        };
        var sampler = _wgpu.DeviceCreateSampler(_device, &samplerDesc);

        var handle = (nuint)_nextTextureId++;
        _textures[(int)handle] = new TextureInfo
        {
            Texture = texture,
            TextureView = textureView,
            Sampler = sampler,
            Width = width,
            Height = height,
            Layers = layers,
            Format = WGPUTextureFormat.Rgba8Unorm,
            IsArray = true,
        };

        return handle;
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name = null)
    {
        var wgpuFormat = MapTextureFormat(format);
        var layers = layerData.Length;

        // Create texture array
        var textureDesc = new TextureDescriptor
        {
            Label = (byte*)(name != null ? Marshal.StringToHGlobalAnsi(name) : IntPtr.Zero),
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = (uint)layers },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = wgpuFormat,
            Usage = TextureUsage.TextureBinding | TextureUsage.CopyDst,
        };
        var texture = _wgpu.DeviceCreateTexture(_device, &textureDesc);

        // Upload layer data
        var bytesPerPixel = format switch
        {
            TextureFormat.RGBA8 => 4,
            TextureFormat.RGB8 => 3,
            TextureFormat.R8 => 1,
            TextureFormat.RG8 => 2,
            TextureFormat.RGBA32F => 16,
            _ => 4,
        };

        for (int i = 0; i < layers; i++)
        {
            fixed (byte* dataPtr = layerData[i])
            {
                var layout = new TextureDataLayout
                {
                    Offset = 0,
                    BytesPerRow = (uint)(width * bytesPerPixel),
                    RowsPerImage = (uint)height,
                };

                var copySize = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
                var destination = new ImageCopyTexture
                {
                    Texture = texture,
                    MipLevel = 0,
                    Origin = new Origin3D { X = 0, Y = 0, Z = (uint)i },
                    Aspect = TextureAspect.All,
                };

                _wgpu.QueueWriteTexture(_queue, &destination, dataPtr, (nuint)layerData[i].Length, &layout, &copySize);
            }
        }

        // Create texture view
        var viewDesc = new TextureViewDescriptor
        {
            Label = (byte*)(name != null ? Marshal.StringToHGlobalAnsi($"{name}_view") : IntPtr.Zero),
            Format = wgpuFormat,
            Dimension = TextureViewDimension.Dimension2DArray,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = (uint)layers,
            Aspect = TextureAspect.All,
        };
        var textureView = _wgpu.TextureCreateView(texture, &viewDesc);

        // Create sampler
        var filterMode = MapFilterMode(filter);
        var samplerDesc = new SamplerDescriptor
        {
            Label = (byte*)(name != null ? Marshal.StringToHGlobalAnsi($"{name}_sampler") : IntPtr.Zero),
            AddressModeU = AddressMode.Repeat,
            AddressModeV = AddressMode.Repeat,
            AddressModeW = AddressMode.Repeat,
            MagFilter = filterMode,
            MinFilter = filterMode,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0.0f,
            LodMaxClamp = 32.0f,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
        };
        var sampler = _wgpu.DeviceCreateSampler(_device, &samplerDesc);

        var handle = (nuint)_nextTextureId++;
        _textures[(int)handle] = new TextureInfo
        {
            Texture = texture,
            TextureView = textureView,
            Sampler = sampler,
            Width = width,
            Height = height,
            Layers = layers,
            Format = wgpuFormat,
            IsArray = true,
        };

        return handle;
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        ref var textureInfo = ref _textures[(int)handle];

        var bytesPerPixel = textureInfo.Format switch
        {
            WGPUTextureFormat.Rgba8Unorm => 4,
            WGPUTextureFormat.R8Unorm => 1,
            WGPUTextureFormat.Rgba32float => 16,
            _ => 4,
        };

        fixed (byte* dataPtr = data)
        {
            var layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)(textureInfo.Width * bytesPerPixel),
                RowsPerImage = (uint)textureInfo.Height,
            };

            var copySize = new Extent3D { Width = (uint)textureInfo.Width, Height = (uint)textureInfo.Height, DepthOrArrayLayers = 1 };
            var destination = new ImageCopyTexture
            {
                Texture = textureInfo.Texture,
                MipLevel = 0,
                Origin = new Origin3D { X = 0, Y = 0, Z = (uint)layer },
                Aspect = TextureAspect.All,
            };

            _wgpu.QueueWriteTexture(_queue, &destination, dataPtr, (nuint)data.Length, &layout, &copySize);
        }
    }

    // ============================================================================
    // Render Texture (for capturing to image)
    // ============================================================================

    private const int MaxRenderTextures = 16;
    private readonly RenderTextureInfo[] _renderTextures = new RenderTextureInfo[MaxRenderTextures];
    private readonly int[] _rtHandleToSlot = new int[MaxTextures]; // Maps texture handle → RT slot
    private readonly int[] _freeRtSlots = new int[MaxRenderTextures];
    private int _freeRtSlotCount;
    private int _nextRenderTextureSlot = 1;
    private nuint _activeRenderTexture;

    private struct RenderTextureInfo
    {
        public WGPUTexture* Texture;
        public TextureView* TextureView;
        public WGPUTexture* MsaaTexture;
        public TextureView* MsaaTextureView;
        public int Width;
        public int Height;
        public int SampleCount;
        public WGPUTextureFormat Format;
    }

    public nuint CreateRenderTexture(int width, int height, TextureFormat format = TextureFormat.BGRA8, int sampleCount = 1, string? name = null)
    {
        var wgpuFormat = MapTextureFormat(format);

        // Resolve texture (SampleCount=1) - used for sampling, readback, and as MSAA resolve target
        var textureDesc = new TextureDescriptor
        {
            Label = (byte*)(name != null ? System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(name) : IntPtr.Zero),
            Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
            MipLevelCount = 1,
            SampleCount = 1,
            Dimension = TextureDimension.Dimension2D,
            Format = wgpuFormat,
            Usage = TextureUsage.RenderAttachment | TextureUsage.CopySrc | TextureUsage.TextureBinding,
        };

        var texture = _wgpu.DeviceCreateTexture(_device, &textureDesc);

        // D2 view for render pass (resolve target when MSAA, or direct attachment when no MSAA)
        var viewDesc = new TextureViewDescriptor
        {
            Format = wgpuFormat,
            Dimension = TextureViewDimension.Dimension2D,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };
        var textureView = _wgpu.TextureCreateView(texture, &viewDesc);

        // D2Array view for sampling (sprite shader expects texture_2d_array)
        var arrayViewDesc = new TextureViewDescriptor
        {
            Format = wgpuFormat,
            Dimension = TextureViewDimension.Dimension2DArray,
            BaseMipLevel = 0,
            MipLevelCount = 1,
            BaseArrayLayer = 0,
            ArrayLayerCount = 1,
            Aspect = TextureAspect.All,
        };
        var arrayTextureView = _wgpu.TextureCreateView(texture, &arrayViewDesc);

        // MSAA texture (only when sampleCount > 1)
        WGPUTexture* msaaTexture = null;
        TextureView* msaaTextureView = null;
        if (sampleCount > 1)
        {
            var msaaDesc = new TextureDescriptor
            {
                Label = (byte*)(name != null ? System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi(name + "_msaa") : IntPtr.Zero),
                Size = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = (uint)sampleCount,
                Dimension = TextureDimension.Dimension2D,
                Format = wgpuFormat,
                Usage = TextureUsage.RenderAttachment,
            };
            msaaTexture = _wgpu.DeviceCreateTexture(_device, &msaaDesc);
            msaaTextureView = _wgpu.TextureCreateView(msaaTexture, &viewDesc);
        }

        // Allocate from shared texture handle space so RT can be used with BindTexture
        var handle = (nuint)_nextTextureId++;
        var rtSlot = _freeRtSlotCount > 0 ? _freeRtSlots[--_freeRtSlotCount] : _nextRenderTextureSlot++;

        _renderTextures[rtSlot] = new RenderTextureInfo
        {
            Texture = texture,
            TextureView = textureView,
            MsaaTexture = msaaTexture,
            MsaaTextureView = msaaTextureView,
            Width = width,
            Height = height,
            SampleCount = sampleCount,
            Format = wgpuFormat,
        };

        // D2Array view for sampling through the batch pipeline
        _textures[(int)handle] = new TextureInfo
        {
            Texture = texture,
            TextureView = arrayTextureView,
            Width = width,
            Height = height,
            Format = wgpuFormat,
            IsArray = true,
        };

        // Map handle → RT slot for render pass operations
        _rtHandleToSlot[(int)handle] = rtSlot;

        return handle;
    }

    public void DestroyRenderTexture(nuint handle)
    {
        var rtSlot = _rtHandleToSlot[(int)handle];
        ref var rt = ref _renderTextures[rtSlot];

        // Release the D2 view (render pass attachment / resolve target)
        if (rt.TextureView != null)
        {
            _wgpu.TextureViewRelease(rt.TextureView);
            rt.TextureView = null;
        }

        // Release MSAA resources
        if (rt.MsaaTextureView != null)
        {
            _wgpu.TextureViewRelease(rt.MsaaTextureView);
            rt.MsaaTextureView = null;
        }
        if (rt.MsaaTexture != null)
        {
            _wgpu.TextureRelease(rt.MsaaTexture);
            rt.MsaaTexture = null;
        }

        // Release the D2Array view (sampling)
        ref var tex = ref _textures[(int)handle];
        if (tex.TextureView != null)
        {
            _wgpu.TextureViewRelease(tex.TextureView);
            tex.TextureView = null;
        }

        if (rt.Texture != null)
        {
            _wgpu.TextureRelease(rt.Texture);
            rt.Texture = null;
        }

        _textures[(int)handle] = default;
        _renderTextures[rtSlot] = default;
        _rtHandleToSlot[(int)handle] = 0;
        _freeRtSlots[_freeRtSlotCount++] = rtSlot;
    }

    public void BeginRenderTexturePass(nuint renderTexture, Color clearColor)
    {
        if (_currentRenderPass != null)
            throw new InvalidOperationException("Already in a render pass");

        var rtSlot = _rtHandleToSlot[(int)renderTexture];
        ref var rt = ref _renderTextures[rtSlot];
        _activeRenderTexture = renderTexture;

        // Reset all cached state — new render pass encoder needs everything rebound
        _state = default;
        _state.CurrentPassSampleCount = rt.SampleCount;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
        _currentGlobalsIndex = -1;

        var colorAttachment = new RenderPassColorAttachment
        {
            View = rt.SampleCount > 1 ? rt.MsaaTextureView : rt.TextureView,
            ResolveTarget = rt.SampleCount > 1 ? rt.TextureView : null,
            LoadOp = LoadOp.Clear,
            StoreOp = rt.SampleCount > 1 ? StoreOp.Discard : StoreOp.Store,
            ClearValue = new Silk.NET.WebGPU.Color
            {
                R = clearColor.R,
                G = clearColor.G,
                B = clearColor.B,
                A = clearColor.A
            }
        };

        var desc = new RenderPassDescriptor
        {
            ColorAttachments = &colorAttachment,
            ColorAttachmentCount = 1,
            DepthStencilAttachment = null
        };

        _currentRenderPass = _wgpu.CommandEncoderBeginRenderPass(_commandEncoder, in desc);

        _wgpu.RenderPassEncoderSetViewport(_currentRenderPass, 0, 0, rt.Width, rt.Height, 0, 1);
        _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, (uint)rt.Width, (uint)rt.Height);
    }

    public void EndRenderTexturePass()
    {
        if (_currentRenderPass == null)
            throw new InvalidOperationException("Not in a render pass");

        _wgpu.RenderPassEncoderEnd(_currentRenderPass);
        _wgpu.RenderPassEncoderRelease(_currentRenderPass);
        _currentRenderPass = null;
        _activeRenderTexture = 0;

        if (_bindGroupsToRelease.Count > 0)
        {
            foreach (var bindGroup in _bindGroupsToRelease)
                _wgpu.BindGroupRelease((BindGroup*)bindGroup);
            _bindGroupsToRelease.Clear();
        }

        _currentBindGroup = null;

        // Submit the current command encoder so RTT draws are executed before any readback
        // Then create a new encoder for subsequent operations
        var commandBufferDesc = new CommandBufferDescriptor();
        var commandBuffer = _wgpu.CommandEncoderFinish(_commandEncoder, &commandBufferDesc);
        _wgpu.QueueSubmit(_queue, 1, &commandBuffer);
        _wgpu.CommandBufferRelease(commandBuffer);
        _wgpu.CommandEncoderRelease(_commandEncoder);

        // Create new encoder for any remaining frame operations
        var encoderDesc = new CommandEncoderDescriptor();
        _commandEncoder = _wgpu.DeviceCreateCommandEncoder(_device, ref encoderDesc);
    }

    public Task<byte[]> ReadRenderTexturePixelsAsync(nuint renderTexture)
    {
        var rtSlot = _rtHandleToSlot[(int)renderTexture];
        ref var rt = ref _renderTextures[rtSlot];

        int bytesPerPixel = 4; // RGBA8
        int bytesPerRow = rt.Width * bytesPerPixel;
        // WebGPU requires bytesPerRow to be aligned to 256 bytes
        int alignedBytesPerRow = (bytesPerRow + 255) & ~255;
        int bufferSize = alignedBytesPerRow * rt.Height;

        // Create staging buffer with MapRead usage
        var bufferDesc = new BufferDescriptor
        {
            Label = (byte*)System.Runtime.InteropServices.Marshal.StringToHGlobalAnsi("readback_staging"),
            Size = (ulong)bufferSize,
            Usage = WGPUBufferUsage.MapRead | WGPUBufferUsage.CopyDst,
            MappedAtCreation = false,
        };
        var stagingBuffer = _wgpu.DeviceCreateBuffer(_device, &bufferDesc);

        // Copy texture to staging buffer
        var encoder = _wgpu.DeviceCreateCommandEncoder(_device, null);

        var src = new ImageCopyTexture
        {
            Texture = rt.Texture,
            MipLevel = 0,
            Origin = new Origin3D { X = 0, Y = 0, Z = 0 },
            Aspect = TextureAspect.All,
        };

        var dst = new ImageCopyBuffer
        {
            Buffer = stagingBuffer,
            Layout = new TextureDataLayout
            {
                Offset = 0,
                BytesPerRow = (uint)alignedBytesPerRow,
                RowsPerImage = (uint)rt.Height,
            }
        };

        var copySize = new Extent3D
        {
            Width = (uint)rt.Width,
            Height = (uint)rt.Height,
            DepthOrArrayLayers = 1
        };

        _wgpu.CommandEncoderCopyTextureToBuffer(encoder, &src, &dst, &copySize);

        var cmdBufferDesc = new CommandBufferDescriptor();
        var cmdBuffer = _wgpu.CommandEncoderFinish(encoder, &cmdBufferDesc);
        _wgpu.QueueSubmit(_queue, 1, &cmdBuffer);
        _wgpu.CommandBufferRelease(cmdBuffer);
        _wgpu.CommandEncoderRelease(encoder);

        // Map buffer and read data asynchronously
        var tcs = new TaskCompletionSource<byte[]>();
        var width = rt.Width;
        var height = rt.Height;
        var wgpu = _wgpu;

        var isBgra = rt.Format == WGPUTextureFormat.Bgra8Unorm;

        PfnBufferMapCallback callback = new((status, userdata) =>
        {
            if (status != BufferMapAsyncStatus.Success)
            {
                tcs.SetException(new Exception($"Buffer map failed: {status}"));
                return;
            }

            // Read the mapped data
            var mappedPtr = wgpu.BufferGetMappedRange(stagingBuffer, 0, (nuint)bufferSize);
            var result = new byte[width * height * 4];

            // Copy row by row to handle alignment padding
            for (int y = 0; y < height; y++)
            {
                var srcOffset = y * alignedBytesPerRow;
                var dstOffset = y * width * 4;
                System.Runtime.InteropServices.Marshal.Copy((IntPtr)((byte*)mappedPtr + srcOffset), result, dstOffset, width * 4);
            }

            // Swizzle BGRA to RGBA if needed
            if (isBgra)
            {
                for (int i = 0; i < result.Length; i += 4)
                {
                    (result[i], result[i + 2]) = (result[i + 2], result[i]); // Swap B and R
                }
            }

            wgpu.BufferUnmap(stagingBuffer);
            wgpu.BufferRelease(stagingBuffer);

            tcs.SetResult(result);
        });

        _wgpu.BufferMapAsync(stagingBuffer, MapMode.Read, 0, (nuint)bufferSize, callback, null);

        return tcs.Task;
    }
}
