//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace NoZ.Platform;

public unsafe partial class DirectX12GraphicsDriver
{
    public nuint CreateTexture(
        int width,
        int height,
        ReadOnlySpan<byte> data,
        TextureFormat format,
        TextureFilter filter,
        string? name)
    {
        var dxgiFormat = ToDxgiFormat(format);
        var bytesPerPixel = GetBytesPerPixel(format);

        // Create texture resource
        var heapProps = new HeapProperties
        {
            Type = HeapType.Default,
            CPUPageProperty = CpuPageProperty.Unknown,
            MemoryPoolPreference = MemoryPool.Unknown,
            CreationNodeMask = 1,
            VisibleNodeMask = 1
        };

        var resourceDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = dxgiFormat,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.None
        };

        ComPtr<ID3D12Resource> resource = default;
        var hr = _device.CreateCommittedResource(
            &heapProps,
            HeapFlags.None,
            &resourceDesc,
            ResourceStates.CopyDest,
            (ClearValue*)null,
            SilkMarshal.GuidPtrOf<ID3D12Resource>(),
            (void**)&resource);

        if (hr < 0)
            throw new Exception($"Failed to create texture: 0x{hr:X8}");

        // Upload texture data
        if (data.Length > 0)
        {
            UploadTextureData(resource, width, height, 0, data, bytesPerPixel, dxgiFormat);
        }

        // Transition to shader resource state
        TransitionResource(resource, ResourceStates.CopyDest, ResourceStates.PixelShaderResource);

        // Create SRV
        var handle = _nextTextureId++;
        var descriptorIndex = MaxBuffers + handle; // Offset past buffer descriptors

        var srvHandle = _cbvSrvUavHeap.GetCPUDescriptorHandleForHeapStart();
        srvHandle.Ptr += (nuint)(descriptorIndex * _cbvSrvUavDescriptorSize);

        var srvDesc = new ShaderResourceViewDesc
        {
            Format = dxgiFormat,
            ViewDimension = SrvDimension.Texture2D,
            Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
            Anonymous = new ShaderResourceViewDescUnion
            {
                Texture2D = new Tex2DSrv
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    PlaneSlice = 0,
                    ResourceMinLODClamp = 0.0f
                }
            }
        };
        _device.CreateShaderResourceView(resource, &srvDesc, srvHandle);

        _textures[handle] = new TextureInfo
        {
            Resource = resource,
            Width = width,
            Height = height,
            Layers = 0,
            DescriptorIndex = descriptorIndex,
            Format = format,
            IsArray = false
        };

        return (nuint)handle;
    }

    public void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data)
    {
        ref var textureInfo = ref _textures[(int)handle];
        if (textureInfo.Resource.Handle == null) return;

        var bytesPerPixel = GetBytesPerPixel(textureInfo.Format);
        var dxgiFormat = ToDxgiFormat(textureInfo.Format);

        // Transition to copy dest
        TransitionResource(textureInfo.Resource,
            ResourceStates.PixelShaderResource,
            ResourceStates.CopyDest);

        // Upload new data
        UploadTextureData(textureInfo.Resource, width, height, 0, data, bytesPerPixel, dxgiFormat);

        // Transition back to shader resource
        TransitionResource(textureInfo.Resource,
            ResourceStates.CopyDest,
            ResourceStates.PixelShaderResource);
    }

    public void DestroyTexture(nuint handle)
    {
        ref var texture = ref _textures[(int)handle];
        texture.Resource.Dispose();
        texture = default;
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        ref var textureInfo = ref _textures[(int)handle];
        if (textureInfo.Resource.Handle == null) return;

        var bytesPerPixel = GetBytesPerPixel(textureInfo.Format);
        var dxgiFormat = ToDxgiFormat(textureInfo.Format);

        TransitionResource(textureInfo.Resource,
            ResourceStates.PixelShaderResource,
            ResourceStates.CopyDest);

        UploadTextureData(textureInfo.Resource, textureInfo.Width, textureInfo.Height, layer, data, bytesPerPixel, dxgiFormat);

        TransitionResource(textureInfo.Resource,
            ResourceStates.CopyDest,
            ResourceStates.PixelShaderResource);
    }

    public void BindTexture(nuint handle, int slot, TextureFilter filter = TextureFilter.Point)
    {
        ref var textureInfo = ref _textures[(int)handle];
        if (textureInfo.Resource.Handle == null) return;

        // TODO: Handle per-slot filter for DX12 (requires sampler state changes)

        // Get GPU handle for the SRV
        var gpuHandle = _cbvSrvUavHeap.GetGPUDescriptorHandleForHeapStart();
        gpuHandle.Ptr += (ulong)(textureInfo.DescriptorIndex * _cbvSrvUavDescriptorSize);

        // Set as root descriptor table
        // Assuming SRV table is at root parameter index 1
        _commandList.SetGraphicsRootDescriptorTable((uint)(1 + slot), gpuHandle);
    }

    // === Texture Array Management ===

    public nuint CreateTextureArray(int width, int height, int layers)
    {
        return CreateTextureArrayInternal(width, height, layers, null, TextureFormat.RGBA8, TextureFilter.Linear, null);
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name=null)
    {
        return CreateTextureArrayInternal(width, height, layerData.Length, layerData, format, filter, name);
    }

    private nuint CreateTextureArrayInternal(int width, int height, int layers, byte[][]? layerData, TextureFormat format, TextureFilter filter, string? name)
    {
        var dxgiFormat = ToDxgiFormat(format);
        var bytesPerPixel = GetBytesPerPixel(format);

        // Create texture array resource
        var heapProps = new HeapProperties
        {
            Type = HeapType.Default,
            CPUPageProperty = CpuPageProperty.Unknown,
            MemoryPoolPreference = MemoryPool.Unknown,
            CreationNodeMask = 1,
            VisibleNodeMask = 1
        };

        var resourceDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Texture2D,
            Alignment = 0,
            Width = (ulong)width,
            Height = (uint)height,
            DepthOrArraySize = (ushort)layers,
            MipLevels = 1,
            Format = dxgiFormat,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Layout = TextureLayout.LayoutUnknown,
            Flags = ResourceFlags.None
        };

        ComPtr<ID3D12Resource> resource = default;
        var hr = _device.CreateCommittedResource(
            &heapProps,
            HeapFlags.None,
            &resourceDesc,
            ResourceStates.CopyDest,
            (ClearValue*)null,
            SilkMarshal.GuidPtrOf<ID3D12Resource>(),
            (void**)&resource);

        if (hr < 0)
            throw new Exception($"Failed to create texture array: 0x{hr:X8}");

        // Upload layer data if provided
        if (layerData != null)
        {
            for (int i = 0; i < layers; i++)
            {
                if (layerData[i] != null && layerData[i].Length > 0)
                {
                    UploadTextureData(resource, width, height, i, layerData[i], bytesPerPixel, dxgiFormat);
                }
            }
        }

        // Transition to shader resource state
        TransitionResource(resource, ResourceStates.CopyDest,
            ResourceStates.PixelShaderResource);

        // Create SRV for texture array
        var handle = _nextTextureId++;
        var descriptorIndex = MaxBuffers + handle; // Offset past buffer descriptors

        var srvHandle = _cbvSrvUavHeap.GetCPUDescriptorHandleForHeapStart();
        srvHandle.Ptr += (nuint)(descriptorIndex * _cbvSrvUavDescriptorSize);

        var srvDesc = new ShaderResourceViewDesc
        {
            Format = dxgiFormat,
            ViewDimension = SrvDimension.Texture2Darray,
            Shader4ComponentMapping = D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING,
            Anonymous = new ShaderResourceViewDescUnion
            {
                Texture2DArray = new Tex2DArraySrv
                {
                    MostDetailedMip = 0,
                    MipLevels = 1,
                    FirstArraySlice = 0,
                    ArraySize = (uint)layers,
                    PlaneSlice = 0,
                    ResourceMinLODClamp = 0.0f
                }
            }
        };
        _device.CreateShaderResourceView(resource, &srvDesc, srvHandle);

        _textures[handle] = new TextureInfo
        {
            Resource = resource,
            Width = width,
            Height = height,
            Layers = layers,
            DescriptorIndex = descriptorIndex,
            Format = format,
            IsArray = true
        };

        return (nuint)handle;
    }

    // === Helper Methods ===

    private void UploadTextureData(ComPtr<ID3D12Resource> destResource, int width, int height, int arraySlice,
        ReadOnlySpan<byte> data, int bytesPerPixel, Format format)
    {
        // Calculate row pitch (must be 256-byte aligned for D3D12)
        var rowPitch = (width * bytesPerPixel + 255) & ~255;
        var uploadSize = (nuint)(rowPitch * height);

        // Check if we have room in upload heap
        if (_uploadHeapOffset + uploadSize > UploadHeapSize)
        {
            // Need to flush and wait - simple approach
            WaitForGpu();
            _uploadHeapOffset = 0;
        }

        // Copy data to upload heap with proper row pitch
        var destPtr = _uploadHeapMappedPtr + _uploadHeapOffset;
        var srcRowPitch = width * bytesPerPixel;

        fixed (byte* srcPtr = data)
        {
            for (int y = 0; y < height; y++)
            {
                Buffer.MemoryCopy(
                    srcPtr + y * srcRowPitch,
                    destPtr + y * rowPitch,
                    rowPitch,
                    srcRowPitch);
            }
        }

        // Calculate subresource index for array slice
        var subresourceIndex = (uint)arraySlice;

        // Copy from upload heap to texture
        var srcLocation = new TextureCopyLocation
        {
            PResource = _uploadHeap,
            Type = TextureCopyType.PlacedFootprint,
            Anonymous = new TextureCopyLocationUnion
            {
                PlacedFootprint = new PlacedSubresourceFootprint
                {
                    Offset = _uploadHeapOffset,
                    Footprint = new SubresourceFootprint
                    {
                        Format = format,
                        Width = (uint)width,
                        Height = (uint)height,
                        Depth = 1,
                        RowPitch = (uint)rowPitch
                    }
                }
            }
        };

        var dstLocation = new TextureCopyLocation
        {
            PResource = destResource,
            Type = TextureCopyType.SubresourceIndex,
            Anonymous = new TextureCopyLocationUnion
            {
                SubresourceIndex = subresourceIndex
            }
        };

        _commandList.CopyTextureRegion(&dstLocation, 0, 0, 0, &srcLocation, (Box*)null);

        _uploadHeapOffset += uploadSize;
        // Align to 256 bytes for next upload
        _uploadHeapOffset = (_uploadHeapOffset + 255) & ~(nuint)255;
    }

    private void TransitionResource(ComPtr<ID3D12Resource> resource, ResourceStates before, ResourceStates after)
    {
        var barrier = new ResourceBarrier
        {
            Type = ResourceBarrierType.Transition,
            Flags = ResourceBarrierFlags.None,
            Anonymous = new ResourceBarrierUnion
            {
                Transition = new ResourceTransitionBarrier
                {
                    PResource = resource,
                    StateBefore = before,
                    StateAfter = after,
                    Subresource = 0xFFFFFFFF // All subresources
                }
            }
        };
        _commandList.ResourceBarrier(1, &barrier);
    }

    private static Format ToDxgiFormat(TextureFormat format) => format switch
    {
        TextureFormat.R8 => Format.FormatR8Unorm,
        TextureFormat.RG8 => Format.FormatR8G8Unorm,
        TextureFormat.RGB8 => Format.FormatR8G8B8A8Unorm, // No RGB8 in D3D12, use RGBA8
        TextureFormat.RGBA8 => Format.FormatR8G8B8A8Unorm,
        TextureFormat.RGBA32F => Format.FormatR32G32B32A32Float,
        _ => Format.FormatR8G8B8A8Unorm
    };

    private static int GetBytesPerPixel(TextureFormat format) => format switch
    {
        TextureFormat.R8 => 1,
        TextureFormat.RG8 => 2,
        TextureFormat.RGB8 => 4, // Expanded to RGBA
        TextureFormat.RGBA8 => 4,
        TextureFormat.RGBA32F => 16, // 4 floats * 4 bytes
        _ => 4
    };

    // D3D12 shader 4 component mapping default
    private const uint D3D12_DEFAULT_SHADER_4_COMPONENT_MAPPING =
        (0) | (1 << 3) | (2 << 6) | (3 << 9) | (1 << 12);
}
