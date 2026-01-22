//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using Silk.NET.WebGPU;
using WGPUBuffer = Silk.NET.WebGPU.Buffer;
using WGPUTexture = Silk.NET.WebGPU.Texture;
using WGPUTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace NoZ.Platform.WebGPU;

public unsafe partial class WebGPUGraphicsDriver : IGraphicsDriver
{
    private GraphicsDriverConfig _config = null!;
    private Silk.NET.WebGPU.WebGPU _wgpu = null!;

    // Core WebGPU objects
    private Instance* _instance;
    private Adapter* _adapter;
    private Device* _device;
    private Queue* _queue;
    private Surface* _surface;
    private WGPUTextureFormat _surfaceFormat;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private PresentMode _presentMode;

    // Command recording
    private CommandEncoder* _commandEncoder;
    private RenderPassEncoder* _currentRenderPass;
    private WGPUTexture* _currentSurfaceTexture;
    private TextureView* _currentSurfaceView;
    private bool _inRenderPass;

    // Resource tracking
    private const int MaxMeshes = 32;
    private const int MaxBuffers = 256;
    private const int MaxTextures = 1024;
    private const int MaxShaders = 64;

    private int _nextMeshId = 1;
    private int _nextBufferId = 1;
    private int _nextTextureId = 2; // 1 reserved for white texture
    private int _nextShaderId = 1;

    private readonly MeshInfo[] _meshes = new MeshInfo[MaxMeshes];
    private readonly BufferInfo[] _buffers = new BufferInfo[MaxBuffers];
    private readonly TextureInfo[] _textures = new TextureInfo[MaxTextures];
    private readonly ShaderInfo[] _shaders = new ShaderInfo[MaxShaders];

    // Cached state
    private CachedState _state;

    // Offscreen rendering
    private WGPUTexture* _offscreenMsaaTexture;
    private TextureView* _offscreenMsaaTextureView;
    private WGPUTexture* _offscreenResolveTexture;
    private TextureView* _offscreenResolveTextureView;
    private WGPUTexture* _offscreenDepthTexture;
    private TextureView* _offscreenDepthTextureView;
    private int _offscreenWidth;
    private int _offscreenHeight;
    private int _msaaSamples = 1;

    // Bind group management
    private BindGroup* _currentBindGroup;
    private List<nint> _bindGroupsToRelease = new();

    public string ShaderExtension => "";

    private struct CachedState
    {
        public nuint BoundShader;
        public BlendMode BlendMode;
        public nuint BoundMesh;
        public int VertexStride;
        public fixed ulong BoundTextures[8];
        public fixed ulong BoundUniformBuffers[4];
        public bool PipelineDirty;
        public bool BindGroupDirty;
        public int ViewportX, ViewportY, ViewportW, ViewportH;
        public bool ScissorEnabled;
        public int ScissorX, ScissorY, ScissorW, ScissorH;
    }

    private struct MeshInfo
    {
        public WGPUBuffer* VertexBuffer;
        public WGPUBuffer* IndexBuffer;
        public int Stride;
        public int MaxVertices;
        public int MaxIndices;
        public VertexFormatDescriptor Descriptor;
    }

    private struct BufferInfo
    {
        public WGPUBuffer* Buffer;
        public int SizeInBytes;
        public NoZ.Platform.BufferUsage Usage;
    }

    private struct TextureInfo
    {
        public WGPUTexture* Texture;
        public TextureView* TextureView;
        public Sampler* Sampler;
        public int Width;
        public int Height;
        public int Layers;
        public WGPUTextureFormat Format;
        public bool IsArray;
    }

    private struct TextureSlotInfo
    {
        public uint TextureBinding;
        public uint SamplerBinding;
    }

    private struct ShaderInfo
    {
        public ShaderModule* VertexModule;
        public ShaderModule* FragmentModule;
        public BindGroupLayout* BindGroupLayout0;
        public PipelineLayout* PipelineLayout;
        public Dictionary<PsoKey, nint> PsoCache; // Dictionary stores nint pointers, converted to/from RenderPipeline*
        public int BindGroupEntryCount; // Number of bindings this shader expects
        public List<ShaderBinding> Bindings; // Binding metadata from asset pipeline
        public List<TextureSlotInfo> TextureSlots; // Derived from bindings: texture+sampler pairs
        public Dictionary<string, uint> UniformBindings; // Uniform name → binding number
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
        _wgpu = Silk.NET.WebGPU.WebGPU.GetApi();

        InitSync();
    }

    private void InitSync()
    {
        // Create instance
        var instanceDesc = new InstanceDescriptor();
        _instance = _wgpu.CreateInstance(&instanceDesc);

        if (_instance == null)
            throw new Exception("Failed to create WebGPU instance");

        // Create surface (platform-specific)
        _surface = CreateSurface();

        // Request adapter (async with blocking)
        RequestAdapter();

        // Request device (async with blocking)
        RequestDevice();

        // Get queue
        _queue = _wgpu.DeviceGetQueue(_device);

        if (_queue == null)
            throw new Exception("Failed to get device queue");

        Log.Debug($"WebGPU queue obtained: {(nint)_queue:X}");

        // Create swap chain
        CreateSwapChain();
    }

    private void RequestAdapter()
    {
        var tcs = new TaskCompletionSource<nint>(); // Store pointer as nint
        var options = new RequestAdapterOptions
        {
            CompatibleSurface = _surface,
            PowerPreference = PowerPreference.HighPerformance,
        };

        PfnRequestAdapterCallback callback = new((status, adapter, message, userdata) =>
        {
            if (status == RequestAdapterStatus.Success)
            {
                tcs.SetResult((nint)adapter);
            }
            else
            {
                var msg = Marshal.PtrToStringAnsi((nint)message) ?? "Unknown error";
                tcs.SetException(new Exception($"Failed to request adapter: {msg}"));
            }
        });

        _wgpu.InstanceRequestAdapter(_instance, &options, callback, null);

        if (!tcs.Task.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Adapter request timed out");

        _adapter = (Adapter*)tcs.Task.Result;
    }

    private void RequestDevice()
    {
        var tcs = new TaskCompletionSource<nint>(); // Store pointer as nint
        var deviceDesc = new DeviceDescriptor
        {
            Label = (byte*)Marshal.StringToHGlobalAnsi("NoZ Device"),
        };

        PfnRequestDeviceCallback callback = new((status, device, message, userdata) =>
        {
            if (status == RequestDeviceStatus.Success)
            {
                tcs.SetResult((nint)device);
            }
            else
            {
                var msg = Marshal.PtrToStringAnsi((nint)message) ?? "Unknown error";
                tcs.SetException(new Exception($"Failed to request device: {msg}"));
            }
        });

        _wgpu.AdapterRequestDevice(_adapter, &deviceDesc, callback, null);

        if (!tcs.Task.Wait(TimeSpan.FromSeconds(5)))
            throw new TimeoutException("Device request timed out");

        _device = (Device*)tcs.Task.Result;

        if (_device == null)
            throw new Exception("Device is null after successful request - this should not happen");

        // Set error callback to catch validation errors
        PfnErrorCallback errorCallback = new((type, message, userdata) =>
        {
            var msg = Marshal.PtrToStringAnsi((nint)message) ?? "Unknown error";
            Log.Debug($"[WebGPU Error] Type={type}: {msg}");
        });
        _wgpu.DeviceSetUncapturedErrorCallback(_device, errorCallback, null);

        Log.Debug($"WebGPU device created successfully: {(nint)_device:X}");
    }

    private Surface* CreateSurface()
    {
        if (OperatingSystem.IsBrowser())
        {
            return CreateWebSurface();
        }
        else if (OperatingSystem.IsWindows())
        {
            return CreateWindowsSurface();
        }
        else if (OperatingSystem.IsLinux())
        {
            return CreateLinuxSurface();
        }
        else if (OperatingSystem.IsMacOS())
        {
            return CreateMacOSSurface();
        }

        throw new PlatformNotSupportedException($"Platform not supported for WebGPU");
    }

    private Surface* CreateWebSurface()
    {
        var canvasDesc = new SurfaceDescriptorFromCanvasHTMLSelector
        {
            Chain = new ChainedStruct
            {
                SType = SType.SurfaceDescriptorFromCanvasHtmlSelector,
            },
            Selector = (byte*)Marshal.StringToHGlobalAnsi("canvas"),
        };

        var surfaceDesc = new SurfaceDescriptor
        {
            NextInChain = (ChainedStruct*)(&canvasDesc),
        };

        return _wgpu.InstanceCreateSurface(_instance, &surfaceDesc);
    }

    [DllImport("kernel32.dll")]
    private static extern nint GetModuleHandle(string? lpModuleName);

    private Surface* CreateWindowsSurface()
    {
        var hwnd = _config.Platform.WindowHandle;
        var hinstance = GetModuleHandle(null);

        var windowsDesc = new SurfaceDescriptorFromWindowsHWND
        {
            Chain = new ChainedStruct
            {
                SType = SType.SurfaceDescriptorFromWindowsHwnd,
            },
            Hinstance = (void*)hinstance,
            Hwnd = (void*)hwnd,
        };

        var surfaceDesc = new SurfaceDescriptor
        {
            NextInChain = (ChainedStruct*)(&windowsDesc),
        };

        return _wgpu.InstanceCreateSurface(_instance, &surfaceDesc);
    }

    private Surface* CreateLinuxSurface()
    {
        // TODO: Implement X11/Wayland surface creation
        throw new NotImplementedException("Linux surface creation not yet implemented");
    }

    private Surface* CreateMacOSSurface()
    {
        // TODO: Implement Metal layer surface creation
        throw new NotImplementedException("macOS surface creation not yet implemented");
    }

    private void CreateSwapChain()
    {
        var windowSize = _config.Platform.WindowSize;
        _surfaceWidth = (int)windowSize.X;
        _surfaceHeight = (int)windowSize.Y;

        // Query surface capabilities
        SurfaceCapabilities caps;
        _wgpu.SurfaceGetCapabilities(_surface, _adapter, &caps);

        // Use first available format (typically BGRA8Unorm or RGBA8Unorm)
        _surfaceFormat = caps.Formats[0];
        _presentMode = _config.VSync ? PresentMode.Fifo : PresentMode.Immediate;

        // Configure surface (replaces swap chain in modern WebGPU)
        var surfaceConfig = new SurfaceConfiguration
        {
            Device = _device,
            Format = _surfaceFormat,
            Usage = TextureUsage.RenderAttachment,
            Width = (uint)_surfaceWidth,
            Height = (uint)_surfaceHeight,
            PresentMode = _presentMode,
            AlphaMode = CompositeAlphaMode.Auto,
        };

        _wgpu.SurfaceConfigure(_surface, &surfaceConfig);
    }

    public void Shutdown()
    {
        // Release offscreen resources
        DestroyOffscreenTarget();

        // Release surface
        if (_surface != null)
        {
            _wgpu.SurfaceRelease(_surface);
            _surface = null;
        }

        // Release device and queue
        if (_queue != null)
        {
            _wgpu.QueueRelease(_queue);
            _queue = null;
        }

        if (_device != null)
        {
            _wgpu.DeviceRelease(_device);
            _device = null;
        }

        // Release adapter
        if (_adapter != null)
        {
            _wgpu.AdapterRelease(_adapter);
            _adapter = null;
        }

        // Release instance
        if (_instance != null)
        {
            _wgpu.InstanceRelease(_instance);
            _instance = null;
        }
    }

    private void DestroyOffscreenTarget()
    {
        // Check if resolve and msaa are the same (happens when msaa=1)
        var sameTexture = _offscreenResolveTexture == _offscreenMsaaTexture;
        var sameView = _offscreenResolveTextureView == _offscreenMsaaTextureView;

        if (_offscreenMsaaTextureView != null)
        {
            _wgpu.TextureViewRelease(_offscreenMsaaTextureView);
            _offscreenMsaaTextureView = null;
        }

        if (_offscreenMsaaTexture != null)
        {
            _wgpu.TextureRelease(_offscreenMsaaTexture);
            _offscreenMsaaTexture = null;
        }

        // Only release resolve if it's different from msaa
        if (_offscreenResolveTextureView != null && !sameView)
        {
            _wgpu.TextureViewRelease(_offscreenResolveTextureView);
        }
        _offscreenResolveTextureView = null;

        if (_offscreenResolveTexture != null && !sameTexture)
        {
            _wgpu.TextureRelease(_offscreenResolveTexture);
        }
        _offscreenResolveTexture = null;

        if (_offscreenDepthTextureView != null)
        {
            _wgpu.TextureViewRelease(_offscreenDepthTextureView);
            _offscreenDepthTextureView = null;
        }

        if (_offscreenDepthTexture != null)
        {
            _wgpu.TextureRelease(_offscreenDepthTexture);
            _offscreenDepthTexture = null;
        }
    }

    public void BeginFrame()
    {
        // Validate device
        if (_device == null)
            throw new InvalidOperationException("WebGPU device is null - initialization failed or device lost");

        // Acquire surface texture for this frame
        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
            throw new Exception($"Failed to get current surface texture: {surfaceTexture.Status}");

        _currentSurfaceTexture = surfaceTexture.Texture;
        Log.Debug($"Acquired surface texture: {(nint)_currentSurfaceTexture:X}");

        // Create command encoder for this frame
        var encoderDesc = new CommandEncoderDescriptor();
        _commandEncoder = _wgpu.DeviceCreateCommandEncoder(_device, ref encoderDesc);

        if (_commandEncoder == null)
            throw new Exception("Failed to create command encoder - device may be lost");

        Log.Debug($"Command encoder created: {(nint)_commandEncoder:X}");

        // Reset state
        _state = default;
        // Note: BoundBoneTexture defaults to 0 - engine will bind bone texture when needed

        if (_currentBindGroup != null)
        {
            _wgpu.BindGroupRelease(_currentBindGroup);
            _currentBindGroup = null;
        }
    }

    public void EndFrame()
    {
        Log.Info("EndFrame called");

        // Finish command encoder
        Log.Info("Finishing command encoder...");
        var commandBufferDesc = new CommandBufferDescriptor();
        var commandBuffer = _wgpu.CommandEncoderFinish(_commandEncoder, &commandBufferDesc);

        Log.Info($"Command buffer created: {(nint)commandBuffer:X}");

        // Submit to queue
        Log.Info("Submitting to queue...");
        _wgpu.QueueSubmit(_queue, 1, &commandBuffer);

        // Present surface
        Log.Info("Presenting surface...");
        _wgpu.SurfacePresent(_surface);

        // Cleanup
        Log.Info("Cleaning up command buffer and encoder...");
        _wgpu.CommandBufferRelease(commandBuffer);
        _wgpu.CommandEncoderRelease(_commandEncoder);
        _commandEncoder = null;

        Log.Info("EndFrame completed successfully!");
    }

    public void Clear(Color color)
    {
        // Clear is handled in BeginScenePass
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        if (_state.ViewportX == x && _state.ViewportY == y &&
            _state.ViewportW == width && _state.ViewportH == height)
            return;

        _state.ViewportX = x;
        _state.ViewportY = y;
        _state.ViewportW = width;
        _state.ViewportH = height;

        if (_currentRenderPass != null)
        {
            _wgpu.RenderPassEncoderSetViewport(_currentRenderPass,
                x, y, width, height, 0.0f, 1.0f);
        }
    }

    public void SetScissor(int x, int y, int width, int height)
    {
        _state.ScissorEnabled = true;
        _state.ScissorX = x;
        _state.ScissorY = y;
        _state.ScissorW = width;
        _state.ScissorH = height;

        if (_currentRenderPass != null)
        {
            _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass,
                (uint)x, (uint)y, (uint)width, (uint)height);
        }
    }

    public void DisableScissor()
    {
        _state.ScissorEnabled = false;

        if (_currentRenderPass != null)
        {
            // Set scissor to full viewport
            _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass,
                0, 0, (uint)_surfaceWidth, (uint)_surfaceHeight);
        }
    }
}
