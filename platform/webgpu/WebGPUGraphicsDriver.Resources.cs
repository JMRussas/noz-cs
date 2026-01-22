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
            _ => WGPUTextureFormat.Rgba8Unorm,
        };
    }

    private FilterMode MapFilterMode(TextureFilter filter)
    {
        return filter switch
        {
            TextureFilter.Nearest => FilterMode.Nearest,
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

    public void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data)
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
                BytesPerRow = (uint)(width * bytesPerPixel),
                RowsPerImage = (uint)height,
            };

            var copySize = new Extent3D { Width = (uint)width, Height = (uint)height, DepthOrArrayLayers = 1 };
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

    public void BindTexture(nuint handle, int slot)
    {
        if (slot < 0 || slot >= 8)
        {
            Log.Error($"Texture slot {slot} out of range (0-7)!");
            return;
        }

        if (_state.BoundTextures[slot] == handle)
            return;

        _state.BoundTextures[slot] = handle;
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
}
