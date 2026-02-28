//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Numerics;
using Silk.NET.Core.Loader;
using Silk.NET.Core.Native;
using Silk.NET.WebGPU;
using Silk.NET.WebGPU.Extensions.Dawn;
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
    private bool _surfaceDirty;

    // Command recording
    private CommandEncoder* _commandEncoder;
    private RenderPassEncoder* _currentRenderPass;
    private WGPUTexture* _currentSurfaceTexture;
    private TextureView* _currentSurfaceTextureView;

    // Depth buffer
    private WGPUTexture* _depthTexture;
    private TextureView* _depthTextureView;
    private const WGPUTextureFormat DepthFormat = WGPUTextureFormat.Depth24Plus;

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

    // Bind group management
    private BindGroup* _currentBindGroup;
    private List<nint> _bindGroupsToRelease = new();

    // Global samplers for per-draw-call filtering
    private Sampler* _linearSampler;
    private Sampler* _nearestSampler;

    // Per-name uniform data storage - written to per-shader buffers when bind groups are created
    private readonly Dictionary<string, byte[]> _uniformData = new();

    // Per-batch globals buffer pool
    private const int MaxGlobalsBuffers = 64;
    private const int GlobalsBufferSize = 80; // mat4 (64) + float (4) + padding (12) = 80 bytes
    private WGPUBuffer*[] _globalsBuffers = new WGPUBuffer*[MaxGlobalsBuffers];
    private int _globalsBufferCount;
    private int _currentGlobalsIndex = -1;

    public string ShaderExtension => "";

    private struct CachedState
    {
        public nuint BoundShader;
        public BlendMode BlendMode;
        public TextureFilter TextureFilter;
        public nuint BoundMesh;
        public fixed ulong BoundTextures[8];
        public fixed byte TextureFilters[8];
        public fixed ulong BoundUniformBuffers[4];
        public bool PipelineDirty;
        public bool BindGroupDirty;
        public RectInt Viewport;
        public bool ScissorEnabled;
        public RectInt Scissor;
        public int CurrentPassSampleCount;
        public bool HasDepthAttachment;
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
        public TextureView* TextureView2D;
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
        public bool IsUnfilterable;  // True for textures like RGBA32F that use textureLoad
    }

    private struct ShaderInfo
    {
        public string Name;
        public ShaderModule* VertexModule;
        public ShaderModule* FragmentModule;
        public BindGroupLayout* BindGroupLayout0;
        public PipelineLayout* PipelineLayout;
        public Dictionary<PsoKey, nint> PsoCache; // Dictionary stores nint pointers, converted to/from RenderPipeline*
        public int BindGroupEntryCount; // Number of bindings this shader expects
        public List<ShaderBinding> Bindings; // Binding metadata from asset pipeline
        public List<TextureSlotInfo> TextureSlots; // Derived from bindings: texture+sampler pairs
        public Dictionary<string, uint> UniformBindings; // Uniform name → binding number
        public Dictionary<string, nint> UniformBuffers; // Per-shader uniform buffers by name (nint -> WGPUBuffer*)
        public ShaderFlags Flags; // Depth/blend flags from shader asset
    }

    private struct PsoKey : IEquatable<PsoKey>
    {
        public nuint ShaderHandle;
        public BlendMode BlendMode;
        public int VertexStride;
        public int MsaaSamples;
        public bool HasDepthAttachment;

        public bool Equals(PsoKey other) =>
            ShaderHandle == other.ShaderHandle &&
            BlendMode == other.BlendMode &&
            VertexStride == other.VertexStride &&
            MsaaSamples == other.MsaaSamples &&
            HasDepthAttachment == other.HasDepthAttachment;

        public override bool Equals(object? obj) => obj is PsoKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ShaderHandle, BlendMode, VertexStride, MsaaSamples, HasDepthAttachment);
    }

    public void Init(GraphicsDriverConfig config)
    {
        _config = config;

        // For browser/WASM, use IOS platform which uses __Internal linking
        // This allows Emscripten to provide the WebGPU functions
        if (OperatingSystem.IsBrowser())
            SearchPathContainer.Platform = UnderlyingPlatform.IOS;

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

        _surface = CreateSurface();
        RequestAdapter();
        RequestDevice();

        _queue = _wgpu.DeviceGetQueue(_device);

        if (_queue == null)
            throw new Exception("Failed to get device queue");

        CreateSwapChain();
        CreateGlobalSamplers();
    }

    private void CreateGlobalSamplers()
    {
        var linearDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Linear,
            MinFilter = FilterMode.Linear,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0.0f,
            LodMaxClamp = 32.0f,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
        };
        _linearSampler = _wgpu.DeviceCreateSampler(_device, &linearDesc);

        var nearestDesc = new SamplerDescriptor
        {
            AddressModeU = AddressMode.ClampToEdge,
            AddressModeV = AddressMode.ClampToEdge,
            AddressModeW = AddressMode.ClampToEdge,
            MagFilter = FilterMode.Nearest,
            MinFilter = FilterMode.Nearest,
            MipmapFilter = MipmapFilterMode.Nearest,
            LodMinClamp = 0.0f,
            LodMaxClamp = 32.0f,
            Compare = CompareFunction.Undefined,
            MaxAnisotropy = 1,
        };
        _nearestSampler = _wgpu.DeviceCreateSampler(_device, &nearestDesc);
    }

    private void RequestAdapter()
    {
        var tcs = new TaskCompletionSource<nint>();
        var options = new RequestAdapterOptions
        {
            CompatibleSurface = _surface,
            PowerPreference = PowerPreference.HighPerformance,
        };

        PfnRequestAdapterCallback callback = new((status, adapter, message, userdata) =>
        {
            if (status == RequestAdapterStatus.Success)
                tcs.SetResult((nint)adapter);
            else
                tcs.SetResult(0);
        });

        _wgpu.InstanceRequestAdapter(_instance, &options, callback, null);

        if (!tcs.Task.Wait(TimeSpan.FromSeconds(5)) || tcs.Task.Result == 0)
            throw new Exception("Failed to find a compatible WebGPU adapter");

        _adapter = (Adapter*)tcs.Task.Result;

        AdapterProperties props;
        _wgpu.AdapterGetProperties(_adapter, &props);
        var adapterName = Marshal.PtrToStringAnsi((nint)props.Name) ?? "Unknown";
        Log.Info($"WebGPU adapter: {adapterName} (backend: {props.BackendType})");
    }

    private void RequestDevice()
    {
        var tcs = new TaskCompletionSource<nint>(); // Store pointer as nint

        using var labelToggle = SilkMarshal.StringToMemory("use_user_defined_labels_in_backend");

        var enabledToggles = stackalloc byte*[1];
        enabledToggles[0] = (byte*)labelToggle;

        var togglesDescriptor = stackalloc DawnTogglesDescriptor[1];
        togglesDescriptor->Chain = new ChainedStruct { SType = (SType)0x0005000A };
        togglesDescriptor->EnabledToggleCount = 1;
        togglesDescriptor->EnabledToggles = enabledToggles;
        togglesDescriptor->DisabledToggleCount = 0;
        togglesDescriptor->DisabledToggles = null;

        using var deviceLabel = SilkMarshal.StringToMemory("NoZ Device");
        var deviceDesc = new DeviceDescriptor
        {
            NextInChain = (ChainedStruct*)togglesDescriptor,
            Label = (byte*)deviceLabel,
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
            Log.Error($"[WebGPU] {type}: {msg}");
        });
        _wgpu.DeviceSetUncapturedErrorCallback(_device, errorCallback, null);
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

        // Prefer linear format - simpler for 2D, no gamma conversion needed
        _surfaceFormat = caps.Formats[0];
        for (int i = 0; i < (int)caps.FormatCount; i++)
        {
            var fmt = caps.Formats[i];
            if (fmt == WGPUTextureFormat.Bgra8Unorm || fmt == WGPUTextureFormat.Rgba8Unorm)
            {
                _surfaceFormat = fmt;
                break;
            }
        }
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

        // Create depth texture immediately so it's available for the first frame.
        CreateDepthTexture();
    }

    private void CreateDepthTexture()
    {
        if (_depthTextureView != null) { _wgpu.TextureViewRelease(_depthTextureView); _depthTextureView = null; }
        if (_depthTexture != null) { _wgpu.TextureRelease(_depthTexture); _depthTexture = null; }

        fixed (byte* label = "DepthTexture\0"u8)
        {
            var desc = new TextureDescriptor
            {
                Label = label,
                Size = new Extent3D { Width = (uint)_surfaceWidth, Height = (uint)_surfaceHeight, DepthOrArrayLayers = 1 },
                MipLevelCount = 1,
                SampleCount = 1,
                Dimension = TextureDimension.Dimension2D,
                Format = DepthFormat,
                Usage = TextureUsage.RenderAttachment,
            };
            _depthTexture = _wgpu.DeviceCreateTexture(_device, &desc);
        }
        _depthTextureView = _wgpu.TextureCreateView(_depthTexture, null);
    }

    public void SetShaderFlags(nuint handle, ShaderFlags flags)
    {
        _shaders[(int)handle].Flags = flags;
    }

    public void Shutdown()
    {
        // Release depth buffer
        if (_depthTextureView != null) { _wgpu.TextureViewRelease(_depthTextureView); _depthTextureView = null; }
        if (_depthTexture != null) { _wgpu.TextureRelease(_depthTexture); _depthTexture = null; }

        // Release global samplers
        if (_linearSampler != null)
        {
            _wgpu.SamplerRelease(_linearSampler);
            _linearSampler = null;
        }
        if (_nearestSampler != null)
        {
            _wgpu.SamplerRelease(_nearestSampler);
            _nearestSampler = null;
        }

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

    public bool BeginFrame()
    {
        // Validate device
        if (_device == null)
            return false;

        var windowSize = _config.Platform.WindowSize;
        var newWidth = (int)windowSize.X;
        var newHeight = (int)windowSize.Y;
        if (newWidth != _surfaceWidth || newHeight != _surfaceHeight || _surfaceDirty)
        {
            _surfaceWidth = newWidth;
            _surfaceHeight = newHeight;
            _surfaceDirty = false;

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
            CreateDepthTexture();
        }

        // Acquire surface texture for this frame
        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);

        // Surface can become outdated after alt-tab, minimize, etc. Reconfigure and retry.
        if (surfaceTexture.Status == SurfaceGetCurrentTextureStatus.Outdated)
        {
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
            _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);
        }

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
            return false;

        _currentSurfaceTexture = surfaceTexture.Texture;
        _currentSurfaceTextureView = _wgpu.TextureCreateView(_currentSurfaceTexture, null);

        var encoderDesc = new CommandEncoderDescriptor();
        _commandEncoder = _wgpu.DeviceCreateCommandEncoder(_device, ref encoderDesc);

        if (_commandEncoder == null)
            return false;

        _state = default;

        if (_currentBindGroup != null)
        {
            _wgpu.BindGroupRelease(_currentBindGroup);
            _currentBindGroup = null;
        }

        return true;
    }

    public void EndFrame()
    {
        var commandBufferDesc = new CommandBufferDescriptor();
        var commandBuffer = _wgpu.CommandEncoderFinish(_commandEncoder, &commandBufferDesc);

        _wgpu.QueueSubmit(_queue, 1, &commandBuffer);
        _wgpu.SurfacePresent(_surface);

        _wgpu.CommandBufferRelease(commandBuffer);
        _wgpu.CommandEncoderRelease(_commandEncoder);
        _commandEncoder = null;

        if (_currentSurfaceTextureView != null)
        {
            _wgpu.TextureViewRelease(_currentSurfaceTextureView);
            _currentSurfaceTextureView = null;
        }
    }

    public void SetVSync(bool vsync)
    {
        var newMode = vsync ? PresentMode.Fifo : PresentMode.Immediate;
        if (newMode == _presentMode) return;
        _presentMode = newMode;
        _surfaceDirty = true;
    }

    public void SetViewport(in RectInt viewport)
    {
        // Clamp to current render target dimensions (RT or surface)
        int targetWidth, targetHeight;
        if (_activeRenderTexture != 0)
        {
            ref var rt = ref _renderTextures[_rtHandleToSlot[(int)_activeRenderTexture]];
            targetWidth = rt.Width;
            targetHeight = rt.Height;
        }
        else
        {
            targetWidth = _surfaceWidth;
            targetHeight = _surfaceHeight;
        }

        var clampedViewport = viewport;
        clampedViewport.Width = Math.Min(viewport.Width, targetWidth - viewport.X);
        clampedViewport.Height = Math.Min(viewport.Height, targetHeight - viewport.Y);
        if (clampedViewport.Width <= 0 || clampedViewport.Height <= 0)
            return;

        if (_state.Viewport == clampedViewport)
            return;

        _state.Viewport = clampedViewport;
        if (_currentRenderPass != null)
            _wgpu.RenderPassEncoderSetViewport(
                _currentRenderPass,
                clampedViewport.X,
                clampedViewport.Y,
                clampedViewport.Width,
                clampedViewport.Height, 0.0f, 1.0f);
    }

    public void SetScissor(in RectInt scissor)
    {
        // Get current render target dimensions
        int targetWidth, targetHeight;
        if (_activeRenderTexture != 0)
        {
            ref var rt = ref _renderTextures[_rtHandleToSlot[(int)_activeRenderTexture]];
            targetWidth = rt.Width;
            targetHeight = rt.Height;
        }
        else
        {
            targetWidth = _surfaceWidth;
            targetHeight = _surfaceHeight;
        }

        var clampedScissor = scissor;
        clampedScissor.X = Math.Max(0, scissor.X);
        clampedScissor.Y = Math.Max(0, scissor.Y);
        clampedScissor.Width = Math.Min(scissor.Width, targetWidth - scissor.X);
        clampedScissor.Height = Math.Min(scissor.Height, targetHeight - scissor.Y);
        if (clampedScissor.Width <= 0 || clampedScissor.Height <= 0)
            return;

        _state.ScissorEnabled = true;
        _state.Scissor = clampedScissor;

        if (_currentRenderPass == null) return;

        _wgpu.RenderPassEncoderSetScissorRect(
            _currentRenderPass,
            (uint)clampedScissor.X,
            (uint)clampedScissor.Y,
            (uint)clampedScissor.Width,
            (uint)clampedScissor.Height);
    }

    public void ClearScissor()
    {
        _state.ScissorEnabled = false;

        if (_currentRenderPass != null)
        {
            // Set scissor to full render target - use RT dimensions if rendering to texture
            uint width, height;
            if (_activeRenderTexture != 0)
            {
                ref var rt = ref _renderTextures[_rtHandleToSlot[(int)_activeRenderTexture]];
                width = (uint)rt.Width;
                height = (uint)rt.Height;
            }
            else
            {
                width = (uint)_surfaceWidth;
                height = (uint)_surfaceHeight;
            }
            _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass, 0, 0, width, height);
        }
    }

}
