//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using Silk.NET.Core.Native;
using Silk.NET.Direct3D12;
using Silk.NET.DXGI;
using Silk.NET.Maths;
using UnmanagedType = System.Runtime.InteropServices.UnmanagedType;
using Marshal = System.Runtime.InteropServices.Marshal;
using DllImportAttribute = System.Runtime.InteropServices.DllImportAttribute;
using MarshalAsAttribute = System.Runtime.InteropServices.MarshalAsAttribute;

namespace NoZ.Platform;

public unsafe partial class DirectX12GraphicsDriver : IGraphicsDriver
{
    private GraphicsDriverConfig _config = null!;

    public string ShaderExtension => ".dx12";

    // D3D12 Core Objects
    private D3D12 _d3d12 = null!;
    private DXGI _dxgi = null!;
    private ComPtr<ID3D12Device> _device;
    private ComPtr<ID3D12CommandQueue> _commandQueue;
    private ComPtr<ID3D12CommandAllocator>[] _commandAllocators = new ComPtr<ID3D12CommandAllocator>[FrameCount];
    private ComPtr<ID3D12GraphicsCommandList> _commandList;

    // Swap Chain
    private ComPtr<IDXGISwapChain3> _swapChain;
    private ComPtr<ID3D12Resource>[] _renderTargets = new ComPtr<ID3D12Resource>[FrameCount];
    private ComPtr<ID3D12DescriptorHeap> _rtvHeap;
    private uint _rtvDescriptorSize;
    private int _frameIndex;
    private nint _hwnd;

    // Synchronization
    private ComPtr<ID3D12Fence> _frameFence;
    private ulong[] _frameFenceValues = new ulong[FrameCount];
    private nint _frameFenceEvent;
    private ulong _currentFenceValue = 1;

    // Descriptor Heaps
    private ComPtr<ID3D12DescriptorHeap> _cbvSrvUavHeap;
    private ComPtr<ID3D12DescriptorHeap> _samplerHeap;
    private uint _cbvSrvUavDescriptorSize;
    private uint _samplerDescriptorSize;

    // Resource tracking - arrays indexed by handle
    private const int FrameCount = 2;
    private const int MaxBuffers = 256;
    private const int MaxTextures = 1024;
    private const int MaxShaders = 64;
    private const int MaxFences = 16;
    private const int MaxMeshes = 32;

    private int _nextBufferId = 1;
    private int _nextTextureId = 2; // 1 is reserved for white texture
    private int _nextShaderId = 1;
    private int _nextFenceId = 1;
    private int _nextMeshId = 1;

    // Resource arrays
    private readonly BufferInfo[] _buffers = new BufferInfo[MaxBuffers];
    private readonly TextureInfo[] _textures = new TextureInfo[MaxTextures];
    private readonly ShaderInfo[] _shaders = new ShaderInfo[MaxShaders];
    private readonly FenceInfo[] _fences = new FenceInfo[MaxFences];
    private readonly MeshInfo[] _meshes = new MeshInfo[MaxMeshes];

    // Current state
    private nuint _boundShader;
    private nuint _boundMesh;
    private BlendMode _currentBlendMode = BlendMode.Alpha;
    private Viewport _currentViewport;
    private Box2D<int> _currentScissor;
    private bool _scissorEnabled;

    // Offscreen render target
    private ComPtr<ID3D12Resource> _offscreenRenderTarget;
    private ComPtr<ID3D12Resource> _offscreenDepthStencil;
    private ComPtr<ID3D12DescriptorHeap> _offscreenRtvHeap;
    private ComPtr<ID3D12DescriptorHeap> _offscreenDsvHeap;
    private int _offscreenWidth;
    private int _offscreenHeight;
    private int _msaaSamples;

    // Upload heap for dynamic updates
    private ComPtr<ID3D12Resource> _uploadHeap;
    private byte* _uploadHeapMappedPtr;
    private nuint _uploadHeapOffset;
    private const nuint UploadHeapSize = 64 * 1024 * 1024; // 64MB

    // Fullscreen quad for composite pass
    private nuint _fullscreenMesh;

    // Resource info structs
    private struct BufferInfo
    {
        public ComPtr<ID3D12Resource> Resource;
        public int SizeInBytes;
        public int DescriptorIndex;
    }

    private struct TextureInfo
    {
        public ComPtr<ID3D12Resource> Resource;
        public int Width;
        public int Height;
        public int Layers;
        public int DescriptorIndex;
        public TextureFormat Format;
        public bool IsArray;
    }

    private struct MeshInfo
    {
        public ComPtr<ID3D12Resource> VertexBuffer;
        public ComPtr<ID3D12Resource> IndexBuffer;
        public VertexBufferView VertexBufferView;
        public IndexBufferView IndexBufferView;
        public int Stride;
        public int MaxVertices;
        public int MaxIndices;
    }

    private struct ShaderInfo
    {
        public ComPtr<ID3D12RootSignature> RootSignature;
        public ComPtr<ID3D10Blob> VertexShaderBlob;
        public ComPtr<ID3D10Blob> PixelShaderBlob;
        public Dictionary<PsoKey, ComPtr<ID3D12PipelineState>> PsoCache;
    }

    private struct FenceInfo
    {
        public ComPtr<ID3D12Fence> Fence;
        public ulong Value;
        public nint Event;
    }

    private struct PsoKey : IEquatable<PsoKey>
    {
        public nuint ShaderHandle;
        public BlendMode BlendMode;
        public int VertexStride;

        public bool Equals(PsoKey other) =>
            ShaderHandle == other.ShaderHandle &&
            BlendMode == other.BlendMode &&
            VertexStride == other.VertexStride;

        public override bool Equals(object? obj) => obj is PsoKey other && Equals(other);
        public override int GetHashCode() => HashCode.Combine(ShaderHandle, BlendMode, VertexStride);
    }

    public void Init(GraphicsDriverConfig config)
    {
        _config = config;
        _hwnd = config.Platform.WindowHandle;

        if (_hwnd == nint.Zero)
            throw new Exception("DirectX12GraphicsDriver requires a valid WindowHandle from platform");

        var windowSize = config.Platform.WindowSize;
        var width = (int)windowSize.X;
        var height = (int)windowSize.Y;

        if (width <= 0 || height <= 0)
            throw new Exception("DirectX12GraphicsDriver  requires valid WindowSize from platform");

        _d3d12 = D3D12.GetApi();
#pragma warning disable CS0618 // DXGI.GetApi is obsolete but we manage the swap chain ourselves
        _dxgi = DXGI.GetApi(DXSwapchainProvider.Win32);
#pragma warning restore CS0618

        CreateDevice();
        CreateCommandQueue();
        CreateSwapChain(width, height);
        CreateDescriptorHeaps();
        CreateCommandAllocatorsAndList();
        CreateFrameFence();
        CreateUploadHeap();
        CreateFullscreenQuad();
    }

    public void Shutdown()
    {
        // Wait for GPU to finish all work
        WaitForGpu();

        // Destroy offscreen target
        DestroyOffscreenTarget();

        // Destroy resources
        for (int i = 0; i < MaxMeshes; i++)
        {
            ref var mesh = ref _meshes[i];
            mesh.VertexBuffer.Dispose();
            mesh.IndexBuffer.Dispose();
        }

        for (int i = 0; i < MaxBuffers; i++)
            _buffers[i].Resource.Dispose();

        for (int i = 0; i < MaxTextures; i++)
            _textures[i].Resource.Dispose();

        for (int i = 0; i < MaxShaders; i++)
        {
            ref var shader = ref _shaders[i];
            shader.RootSignature.Dispose();
            shader.VertexShaderBlob.Dispose();
            shader.PixelShaderBlob.Dispose();
            if (shader.PsoCache != null)
            {
                foreach (var pso in shader.PsoCache.Values)
                    pso.Dispose();
            }
        }

        for (int i = 0; i < MaxFences; i++)
        {
            ref var fence = ref _fences[i];
            fence.Fence.Dispose();
            if (fence.Event != nint.Zero)
                CloseHandle(fence.Event);
        }

        // Destroy upload heap
        if (_uploadHeapMappedPtr != null)
        {
            _uploadHeap.Unmap(0, (Silk.NET.Direct3D12.Range*)null);
            _uploadHeapMappedPtr = null;
        }
        _uploadHeap.Dispose();

        // Destroy swap chain resources
        for (int i = 0; i < FrameCount; i++)
        {
            _renderTargets[i].Dispose();
            _commandAllocators[i].Dispose();
        }

        // Destroy core objects
        if (_frameFenceEvent != nint.Zero)
            CloseHandle(_frameFenceEvent);
        _frameFence.Dispose();
        _commandList.Dispose();
        _rtvHeap.Dispose();
        _cbvSrvUavHeap.Dispose();
        _samplerHeap.Dispose();
        _swapChain.Dispose();
        _commandQueue.Dispose();
        _device.Dispose();
    }

    public void BeginFrame()
    {
        // Wait for the previous frame to complete
        var completedValue = _frameFence.GetCompletedValue();
        if (completedValue < _frameFenceValues[_frameIndex])
        {
            _frameFence.SetEventOnCompletion(_frameFenceValues[_frameIndex], (void*)_frameFenceEvent);
            WaitForSingleObject(_frameFenceEvent, 0xFFFFFFFF);
        }

        // Reset command allocator and command list
        _commandAllocators[_frameIndex].Reset();
        _commandList.Reset(_commandAllocators[_frameIndex], (ID3D12PipelineState*)null);

        // Reset upload heap offset for this frame
        _uploadHeapOffset = 0;

        // Transition render target to render target state
        var barrier = new ResourceBarrier
        {
            Type = ResourceBarrierType.Transition,
            Flags = ResourceBarrierFlags.None,
            Anonymous = new ResourceBarrierUnion
            {
                Transition = new ResourceTransitionBarrier
                {
                    PResource = _renderTargets[_frameIndex],
                    StateBefore = ResourceStates.Present,
                    StateAfter = ResourceStates.RenderTarget,
                    Subresource = 0xFFFFFFFF
                }
            }
        };
        _commandList.ResourceBarrier(1, &barrier);

        // Set descriptor heaps
        ID3D12DescriptorHeap*[] heaps = [_cbvSrvUavHeap, _samplerHeap];
        fixed (ID3D12DescriptorHeap** pHeaps = heaps)
            _commandList.SetDescriptorHeaps(2, pHeaps);
    }

    public void EndFrame()
    {
        // Transition render target to present state
        var barrier = new ResourceBarrier
        {
            Type = ResourceBarrierType.Transition,
            Flags = ResourceBarrierFlags.None,
            Anonymous = new ResourceBarrierUnion
            {
                Transition = new ResourceTransitionBarrier
                {
                    PResource = _renderTargets[_frameIndex],
                    StateBefore = ResourceStates.RenderTarget,
                    StateAfter = ResourceStates.Present,
                    Subresource = 0xFFFFFFFF
                }
            }
        };
        _commandList.ResourceBarrier(1, &barrier);

        // Close and execute command list
        _commandList.Close();
        ID3D12CommandList* commandLists = (ID3D12CommandList*)_commandList.Handle;
        _commandQueue.ExecuteCommandLists(1, &commandLists);

        // Present
        _swapChain.Present(_config.VSync ? 1u : 0u, 0);

        // Signal fence
        _frameFenceValues[_frameIndex] = _currentFenceValue++;
        _commandQueue.Signal(_frameFence, _frameFenceValues[_frameIndex]);

        // Move to next frame
        _frameIndex = (int)_swapChain.GetCurrentBackBufferIndex();
    }

    public void Clear(Color color)
    {
        var rtv = GetCurrentRtvHandle();
        float* clearColor = stackalloc float[4] { color.R, color.G, color.B, color.A };
        _commandList.ClearRenderTargetView(rtv, clearColor, 0, (Box2D<int>*)null);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        _currentViewport = new Viewport
        {
            TopLeftX = x,
            TopLeftY = y,
            Width = width,
            Height = height,
            MinDepth = 0.0f,
            MaxDepth = 1.0f
        };
        fixed (Viewport* pViewport = &_currentViewport)
            _commandList.RSSetViewports(1, pViewport);
    }

    public void SetScissor(int x, int y, int width, int height)
    {
        if (_scissorEnabled)
            return;

        _scissorEnabled = true;
        _currentScissor = new Box2D<int>(x, y, x + width, y + height);
        fixed (Box2D<int>* pScissor = &_currentScissor)
            _commandList.RSSetScissorRects(1, pScissor);
    }

    public void DisableScissor()
    {
        if (!_scissorEnabled)
            return;

        _scissorEnabled = false;
        // Set scissor to viewport size
        var rect = new Box2D<int>(
            (int)_currentViewport.TopLeftX,
            (int)_currentViewport.TopLeftY,
            (int)(_currentViewport.TopLeftX + _currentViewport.Width),
            (int)(_currentViewport.TopLeftY + _currentViewport.Height));
        _commandList.RSSetScissorRects(1, &rect);
    }

    // Private initialization helpers

    private void CreateDevice()
    {
#if DEBUG
        // Enable debug layer
        ComPtr<ID3D12Debug> debugController = default;
        if (_d3d12.GetDebugInterface(SilkMarshal.GuidPtrOf<ID3D12Debug>(), (void**)&debugController) >= 0)
        {
            debugController.EnableDebugLayer();
            debugController.Dispose();
        }
#endif

        // Create device
        fixed (ComPtr<ID3D12Device>* pDevice = &_device)
        {
            var hr = _d3d12.CreateDevice(
                (IUnknown*)null,
                D3DFeatureLevel.Level120,
                SilkMarshal.GuidPtrOf<ID3D12Device>(),
                (void**)pDevice);

            if (hr < 0)
                throw new Exception($"Failed to create D3D12 device: 0x{hr:X8}");
        }
    }

    private void CreateCommandQueue()
    {
        var desc = new CommandQueueDesc
        {
            Type = CommandListType.Direct,
            Priority = (int)CommandQueuePriority.Normal,
            Flags = CommandQueueFlags.None,
            NodeMask = 0
        };

        fixed (ComPtr<ID3D12CommandQueue>* pQueue = &_commandQueue)
        {
            var hr = _device.CreateCommandQueue(&desc, SilkMarshal.GuidPtrOf<ID3D12CommandQueue>(), (void**)pQueue);
            if (hr < 0)
                throw new Exception($"Failed to create command queue: 0x{hr:X8}");
        }
    }

    private void CreateSwapChain(int width, int height)
    {
        ComPtr<IDXGIFactory4> factory4 = default;
        int hr = _dxgi.CreateDXGIFactory2(0, SilkMarshal.GuidPtrOf<IDXGIFactory4>(), (void**)&factory4);
        if (hr < 0)
            throw new Exception($"Failed to create DXGI factory: 0x{hr:X8}");

        // Get IDXGIFactory2 interface for CreateSwapChainForHwnd
        ComPtr<IDXGIFactory2> factory2 = default;
        hr = factory4.QueryInterface(SilkMarshal.GuidPtrOf<IDXGIFactory2>(), (void**)&factory2);
        if (hr < 0)
        {
            factory4.Dispose();
            throw new Exception($"Failed to get IDXGIFactory2: 0x{hr:X8}");
        }

        var swapChainDesc = new SwapChainDesc1
        {
            Width = (uint)width,
            Height = (uint)height,
            Format = Format.FormatR8G8B8A8Unorm,
            Stereo = 0,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            BufferUsage = DXGI.UsageRenderTargetOutput,
            BufferCount = FrameCount,
            Scaling = Scaling.Stretch,
            SwapEffect = SwapEffect.FlipDiscard,
            AlphaMode = AlphaMode.Unspecified,
            Flags = 0
        };

        ComPtr<IDXGISwapChain1> swapChain1 = default;
        hr = factory2.CreateSwapChainForHwnd(
            (IUnknown*)_commandQueue.Handle,
            _hwnd,
            &swapChainDesc,
            (SwapChainFullscreenDesc*)null,
            (IDXGIOutput*)null,
            (IDXGISwapChain1**)&swapChain1);

        if (hr < 0)
        {
            factory2.Dispose();
            factory4.Dispose();
            throw new Exception($"Failed to create swap chain: 0x{hr:X8}");
        }

        // Disable Alt+Enter fullscreen
        factory4.MakeWindowAssociation(_hwnd, 1u << 1); // DXGI_MWA_NO_ALT_ENTER

        fixed (ComPtr<IDXGISwapChain3>* pSwapChain = &_swapChain)
        {
            hr = swapChain1.QueryInterface(SilkMarshal.GuidPtrOf<IDXGISwapChain3>(), (void**)pSwapChain);
        }
        swapChain1.Dispose();
        factory2.Dispose();
        factory4.Dispose();

        if (hr < 0)
            throw new Exception($"Failed to get IDXGISwapChain3: 0x{hr:X8}");

        _frameIndex = (int)_swapChain.GetCurrentBackBufferIndex();

        // Create RTV heap
        var rtvHeapDesc = new DescriptorHeapDesc
        {
            Type = DescriptorHeapType.Rtv,
            NumDescriptors = FrameCount,
            Flags = DescriptorHeapFlags.None,
            NodeMask = 0
        };
        fixed (ComPtr<ID3D12DescriptorHeap>* pRtvHeap = &_rtvHeap)
        {
            hr = _device.CreateDescriptorHeap(&rtvHeapDesc, SilkMarshal.GuidPtrOf<ID3D12DescriptorHeap>(), (void**)pRtvHeap);
            if (hr < 0)
                throw new Exception($"Failed to create RTV heap: 0x{hr:X8}");
        }

        _rtvDescriptorSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.Rtv);

        // Create RTVs for swap chain buffers
        var rtvHandle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();

        for (int i = 0; i < FrameCount; i++)
        {
            fixed (ComPtr<ID3D12Resource>* pRenderTarget = &_renderTargets[i])
            {
                hr = _swapChain.GetBuffer((uint)i, SilkMarshal.GuidPtrOf<ID3D12Resource>(), (void**)pRenderTarget);
                if (hr < 0)
                    throw new Exception($"Failed to get swap chain buffer {i}: 0x{hr:X8}");
            }

            _device.CreateRenderTargetView(_renderTargets[i], (RenderTargetViewDesc*)null, rtvHandle);
            rtvHandle.Ptr += _rtvDescriptorSize;
        }
    }

    private void CreateDescriptorHeaps()
    {
        // CBV/SRV/UAV heap (shader visible)
        var cbvSrvUavHeapDesc = new DescriptorHeapDesc
        {
            Type = DescriptorHeapType.CbvSrvUav,
            NumDescriptors = MaxTextures + MaxBuffers,
            Flags = DescriptorHeapFlags.ShaderVisible,
            NodeMask = 0
        };
        fixed (ComPtr<ID3D12DescriptorHeap>* pHeap = &_cbvSrvUavHeap)
        {
            var hr = _device.CreateDescriptorHeap(&cbvSrvUavHeapDesc, SilkMarshal.GuidPtrOf<ID3D12DescriptorHeap>(), (void**)pHeap);
            if (hr < 0)
                throw new Exception($"Failed to create CBV/SRV/UAV heap: 0x{hr:X8}");
        }

        _cbvSrvUavDescriptorSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.CbvSrvUav);

        // Sampler heap (shader visible)
        var samplerHeapDesc = new DescriptorHeapDesc
        {
            Type = DescriptorHeapType.Sampler,
            NumDescriptors = 16,
            Flags = DescriptorHeapFlags.ShaderVisible,
            NodeMask = 0
        };
        fixed (ComPtr<ID3D12DescriptorHeap>* pHeap = &_samplerHeap)
        {
            var hr = _device.CreateDescriptorHeap(&samplerHeapDesc, SilkMarshal.GuidPtrOf<ID3D12DescriptorHeap>(), (void**)pHeap);
            if (hr < 0)
                throw new Exception($"Failed to create sampler heap: 0x{hr:X8}");
        }

        _samplerDescriptorSize = _device.GetDescriptorHandleIncrementSize(DescriptorHeapType.Sampler);

        // Create default samplers
        CreateDefaultSamplers();
    }

    private void CreateDefaultSamplers()
    {
        var samplerHandle = _samplerHeap.GetCPUDescriptorHandleForHeapStart();

        // Linear sampler (slot 0)
        var linearSamplerDesc = new SamplerDesc
        {
            Filter = Filter.MinMagMipLinear,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MipLODBias = 0,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunc.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };
        _device.CreateSampler(&linearSamplerDesc, samplerHandle);

        // Point sampler (slot 1)
        samplerHandle.Ptr += _samplerDescriptorSize;
        var pointSamplerDesc = new SamplerDesc
        {
            Filter = Filter.MinMagMipPoint,
            AddressU = TextureAddressMode.Clamp,
            AddressV = TextureAddressMode.Clamp,
            AddressW = TextureAddressMode.Clamp,
            MipLODBias = 0,
            MaxAnisotropy = 1,
            ComparisonFunc = ComparisonFunc.Never,
            MinLOD = 0,
            MaxLOD = float.MaxValue
        };
        _device.CreateSampler(&pointSamplerDesc, samplerHandle);
    }

    private void CreateCommandAllocatorsAndList()
    {
        for (int i = 0; i < FrameCount; i++)
        {
            fixed (ComPtr<ID3D12CommandAllocator>* pAllocator = &_commandAllocators[i])
            {
                var hr = _device.CreateCommandAllocator(
                    CommandListType.Direct,
                    SilkMarshal.GuidPtrOf<ID3D12CommandAllocator>(),
                    (void**)pAllocator);

                if (hr < 0)
                    throw new Exception($"Failed to create command allocator {i}: 0x{hr:X8}");
            }
        }

        fixed (ComPtr<ID3D12GraphicsCommandList>* pCommandList = &_commandList)
        {
            var listHr = _device.CreateCommandList(
                0,
                CommandListType.Direct,
                _commandAllocators[0],
                (ID3D12PipelineState*)null,
                SilkMarshal.GuidPtrOf<ID3D12GraphicsCommandList>(),
                (void**)pCommandList);

            if (listHr < 0)
                throw new Exception($"Failed to create command list: 0x{listHr:X8}");
        }

        // Close it initially - it will be reset in BeginFrame
        _commandList.Close();
    }

    private void CreateFrameFence()
    {
        fixed (ComPtr<ID3D12Fence>* pFence = &_frameFence)
        {
            var hr = _device.CreateFence(0, FenceFlags.None,
                SilkMarshal.GuidPtrOf<ID3D12Fence>(), (void**)pFence);

            if (hr < 0)
                throw new Exception($"Failed to create fence: 0x{hr:X8}");
        }

        _frameFenceEvent = CreateEventW(null, 0, 0, null);
        if (_frameFenceEvent == nint.Zero)
            throw new Exception("Failed to create fence event");
    }

    private void CreateUploadHeap()
    {
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
            Width = UploadHeapSize,
            Height = 1,
            DepthOrArraySize = 1,
            MipLevels = 1,
            Format = Format.FormatUnknown,
            SampleDesc = new SampleDesc { Count = 1, Quality = 0 },
            Layout = TextureLayout.LayoutRowMajor,
            Flags = ResourceFlags.None
        };

        fixed (ComPtr<ID3D12Resource>* pUploadHeap = &_uploadHeap)
        {
            var hr = _device.CreateCommittedResource(
                &heapProps,
                HeapFlags.None,
                &resourceDesc,
                ResourceStates.GenericRead,
                (ClearValue*)null,
                SilkMarshal.GuidPtrOf<ID3D12Resource>(),
                (void**)pUploadHeap);

            if (hr < 0)
                throw new Exception($"Failed to create upload heap: 0x{hr:X8}");
        }

        // Map persistently
        void* mappedPtr;
        _uploadHeap.Map(0, (Silk.NET.Direct3D12.Range*)null, &mappedPtr);
        _uploadHeapMappedPtr = (byte*)mappedPtr;
    }

    private void CreateFullscreenQuad()
    {
        // Will be created when mesh system is ready
    }

    private void WaitForGpu()
    {
        _commandQueue.Signal(_frameFence, _currentFenceValue);

        if (_frameFence.GetCompletedValue() < _currentFenceValue)
        {
            _frameFence.SetEventOnCompletion(_currentFenceValue, (void*)_frameFenceEvent);
            WaitForSingleObject(_frameFenceEvent, 0xFFFFFFFF);
        }

        _currentFenceValue++;
    }

    private CpuDescriptorHandle GetCurrentRtvHandle()
    {
        var handle = _rtvHeap.GetCPUDescriptorHandleForHeapStart();
        handle.Ptr += (nuint)(_frameIndex * _rtvDescriptorSize);
        return handle;
    }

    // Win32 imports
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern nint CreateEventW(void* lpEventAttributes, int bManualReset, int bInitialState, char* lpName);

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern uint WaitForSingleObject(nint hHandle, uint dwMilliseconds);

    [DllImport("kernel32.dll", SetLastError = true)]
    [return: MarshalAs(UnmanagedType.Bool)]
    private static extern bool CloseHandle(nint hObject);
}
