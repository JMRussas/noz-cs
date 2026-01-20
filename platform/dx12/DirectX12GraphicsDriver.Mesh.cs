//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;

namespace NoZ.Platform;

public unsafe partial class DirectX12GraphicsDriver
{
    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        var descriptor = T.GetFormatDescriptor();
        var vertexBufferSize = maxVertices * descriptor.Stride;
        var indexBufferSize = maxIndices * sizeof(ushort);

        // Align sizes to 256 bytes
        vertexBufferSize = (vertexBufferSize + 255) & ~255;
        indexBufferSize = (indexBufferSize + 255) & ~255;

        // Use upload heap for dynamic meshes (most common case in this engine)
        var heapType = usage == BufferUsage.Static
            ? HeapType.Default
            : HeapType.Upload;

        var heapProps = new HeapProperties
        {
            Type = heapType,
            CPUPageProperty = CpuPageProperty.Unknown,
            MemoryPoolPreference = MemoryPool.Unknown,
            CreationNodeMask = 1,
            VisibleNodeMask = 1
        };

        // Create vertex buffer
        var vbDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Buffer,
            Alignment = 0,
            Width = (ulong)vertexBufferSize,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.None
        };

        var initialState = heapType == HeapType.Upload
            ? ResourceStates.GenericRead
            : ResourceStates.VertexAndConstantBuffer;

        ComPtr<ID3D12Resource> vertexBuffer = default;
        var hr = _device.CreateCommittedResource(
            &heapProps,
            HeapFlags.None,
            &vbDesc,
            initialState,
            (ClearValue*)null,
            SilkMarshal.GuidPtrOf<ID3D12Resource>(),
            (void**)&vertexBuffer);

        if (hr < 0)
            throw new Exception($"Failed to create vertex buffer: 0x{hr:X8}");

        // Create index buffer
        var ibDesc = new ResourceDesc
        {
            Dimension = ResourceDimension.Buffer,
            Alignment = 0,
            Width = (ulong)indexBufferSize,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.None
        };

        var ibInitialState = heapType == HeapType.Upload
            ? ResourceStates.GenericRead
            : ResourceStates.IndexBuffer;

        ComPtr<ID3D12Resource> indexBuffer = default;
        hr = _device.CreateCommittedResource(
            &heapProps,
            HeapFlags.None,
            &ibDesc,
            ibInitialState,
            (ClearValue*)null,
            SilkMarshal.GuidPtrOf<ID3D12Resource>(),
            (void**)&indexBuffer);

        if (hr < 0)
        {
            vertexBuffer.Dispose();
            throw new Exception($"Failed to create index buffer: 0x{hr:X8}");
        }

        var handle = _nextMeshId++;
        _meshes[handle] = new MeshInfo
        {
            VertexBuffer = vertexBuffer,
            IndexBuffer = indexBuffer,
            VertexBufferView = new VertexBufferView
            {
                BufferLocation = vertexBuffer.GetGPUVirtualAddress(),
                SizeInBytes = (uint)vertexBufferSize,
                StrideInBytes = (uint)descriptor.Stride
            },
            IndexBufferView = new IndexBufferView
            {
                BufferLocation = indexBuffer.GetGPUVirtualAddress(),
                SizeInBytes = (uint)indexBufferSize,
                Format = Format.FormatR16Uint
            },
            Stride = descriptor.Stride,
            MaxVertices = maxVertices,
            MaxIndices = maxIndices
        };

        return (nuint)handle;
    }

    public void DestroyMesh(nuint handle)
    {
        ref var mesh = ref _meshes[(int)handle];
        mesh.VertexBuffer.Dispose();
        mesh.IndexBuffer.Dispose();
        mesh = default;
    }

    public void BindMesh(nuint handle)
    {
        ref var mesh = ref _meshes[(int)handle];
        if (mesh.VertexBuffer.Handle == null) return;

        _boundMesh = handle;

        fixed (VertexBufferView* pView = &mesh.VertexBufferView)
            _commandList.IASetVertexBuffers(0, 1, pView);

        fixed (IndexBufferView* pView = &mesh.IndexBufferView)
            _commandList.IASetIndexBuffer(pView);

        _commandList.IASetPrimitiveTopology(D3DPrimitiveTopology.D3DPrimitiveTopologyTrianglelist);
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        ref var mesh = ref _meshes[(int)handle];
        if (mesh.VertexBuffer.Handle == null) return;

        // Update vertex buffer
        if (vertexData.Length > 0)
        {
            void* mappedPtr;
            var range = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 };
            mesh.VertexBuffer.Map(0, &range, &mappedPtr);

            fixed (byte* srcPtr = vertexData)
            {
                Buffer.MemoryCopy(srcPtr, mappedPtr, mesh.MaxVertices * mesh.Stride, vertexData.Length);
            }

            var writtenRange = new Silk.NET.Direct3D12.Range { Begin = 0, End = (nuint)vertexData.Length };
            mesh.VertexBuffer.Unmap(0, &writtenRange);
        }

        // Update index buffer
        if (indexData.Length > 0)
        {
            void* mappedPtr;
            var range = new Silk.NET.Direct3D12.Range { Begin = 0, End = 0 };
            mesh.IndexBuffer.Map(0, &range, &mappedPtr);

            fixed (ushort* srcPtr = indexData)
            {
                Buffer.MemoryCopy(srcPtr, mappedPtr, mesh.MaxIndices * sizeof(ushort), indexData.Length * sizeof(ushort));
            }

            var writtenRange = new Silk.NET.Direct3D12.Range { Begin = 0, End = (nuint)(indexData.Length * sizeof(ushort)) };
            mesh.IndexBuffer.Unmap(0, &writtenRange);
        }
    }
}
