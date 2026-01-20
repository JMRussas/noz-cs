//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace NoZ.Platform;

public unsafe partial class DirectX12GraphicsDriver
{
    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        // Align size to 256 bytes (D3D12 constant buffer requirement)
        sizeInBytes = (sizeInBytes + 255) & ~255;

        var heapProps = new HeapProperties
        {
            Type = HeapType.Upload,
            CPUPageProperty = CpuPageProperty.Unknown,
            MemoryPoolPreference = MemoryPool.Unknown,
            CreationNodeMask = 1,
            VisibleNodeMask = 1
        };

        var resourceDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Buffer,
            Alignment = 0,
            Width = (ulong)sizeInBytes,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.None
        };

        ComPtr<ID3D12Resource> resource = default;
        var hr = _device.CreateCommittedResource(
            &heapProps,
            HeapFlags.None,
            &resourceDesc,
            ResourceStates.GenericRead,
            (ClearValue*)null,
            SilkMarshal.GuidPtrOf<ID3D12Resource>(),
            (void**)&resource);

        if (hr < 0)
            throw new Exception($"Failed to create uniform buffer: 0x{hr:X8}");

        // Create CBV
        var handle = _nextBufferId++;
        var descriptorIndex = handle; // Use handle as descriptor index for simplicity

        var cbvHandle = _cbvSrvUavHeap.GetCPUDescriptorHandleForHeapStart();
        cbvHandle.Ptr += (nuint)(descriptorIndex * _cbvSrvUavDescriptorSize);

        var cbvDesc = new ConstantBufferViewDesc
        {
            BufferLocation = resource.GetGPUVirtualAddress(),
            SizeInBytes = (uint)sizeInBytes
        };
        _device.CreateConstantBufferView(&cbvDesc, cbvHandle);

        _buffers[handle] = new BufferInfo
        {
            Resource = resource,
            SizeInBytes = sizeInBytes,
            DescriptorIndex = descriptorIndex
        };

        return (nuint)handle;
    }

    public void DestroyBuffer(nuint handle)
    {
        ref var buffer = ref _buffers[(int)handle];
        buffer.Resource.Dispose();
        buffer = default;
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        ref var bufferInfo = ref _buffers[(int)buffer];
        if (bufferInfo.Resource.Handle == null) return;

        // Map, copy, unmap
        void* mappedPtr;
        var range = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 }; // We're writing, not reading
        bufferInfo.Resource.Map(0, &range, &mappedPtr);

        fixed (byte* srcPtr = data)
        {
            Buffer.MemoryCopy(srcPtr, (byte*)mappedPtr + offsetBytes, bufferInfo.SizeInBytes - offsetBytes, data.Length);
        }

        var writtenRange = new Silk.NET.Direct3D12.Range { Begin = (nuint)offsetBytes, End = (nuint)(offsetBytes + data.Length) };
        bufferInfo.Resource.Unmap(0, &writtenRange);
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        ref var bufferInfo = ref _buffers[(int)buffer];
        if (bufferInfo.Resource.Handle == null) return;

        // Get GPU handle for the CBV
        var gpuHandle = _cbvSrvUavHeap.GetGPUDescriptorHandleForHeapStart();
        gpuHandle.Ptr += (ulong)(bufferInfo.DescriptorIndex * _cbvSrvUavDescriptorSize);

        // Set as root descriptor table at the appropriate slot
        // Slot mapping: 0 = CBV table, 1 = SRV table, 2 = Sampler table
        _commandList.SetGraphicsRootDescriptorTable((uint)slot, gpuHandle);
    }
}
