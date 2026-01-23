//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Numerics;
using Silk.NET.Core.Loader;
using Silk.NET.WebGPU;
using WGPUBuffer = Silk.NET.WebGPU.Buffer;
using WGPUTexture = Silk.NET.WebGPU.Texture;
using WGPUTextureFormat = Silk.NET.WebGPU.TextureFormat;

namespace NoZ.Platform.WebGPU;

[StructLayout(LayoutKind.Sequential)]
internal struct CompositeVertex : IVertex
{
    public Vector2 Position;
    public Vector2 UV;

    public static readonly int SizeInBytes = Marshal.SizeOf(typeof(CompositeVertex));

    public static VertexFormatDescriptor GetFormatDescriptor() => new()
    {
        Stride = SizeInBytes,
        Attributes =
        [
            new VertexAttribute(
                0,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<CompositeVertex>(nameof(Position))),
            new VertexAttribute(
                1,
                2,
                VertexAttribType.Float,
                (int)Marshal.OffsetOf<CompositeVertex>(nameof(UV)))
        ]
    };
}

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

    // Fullscreen quad for composite
    private nuint _fullscreenQuadMesh;

    // Global samplers for per-draw-call filtering
    private Sampler* _linearSampler;
    private Sampler* _nearestSampler;

    // Per-name uniform data storage - written to per-shader buffers when bind groups are created
    private readonly Dictionary<string, byte[]> _uniformData = new();

    public string ShaderExtension => "";

    private struct CachedState
    {
        public nuint BoundShader;
        public BlendMode BlendMode;
        public TextureFilter TextureFilter;
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
        public Dictionary<string, nint> UniformBuffers; // Per-shader uniform buffers by name (nint -> WGPUBuffer*)
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
        CreateFullscreenQuad();
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
    }

    public void Shutdown()
    {
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
        var sameTexture = _offscreenResolveTexture == _offscreenMsaaTexture;
        var sameView = _offscreenResolveTextureView == _offscreenMsaaTextureView;

        if (_offscreenMsaaTextureView != null)
            _wgpu.TextureViewRelease(_offscreenMsaaTextureView);

        if (_offscreenMsaaTexture != null)
            _wgpu.TextureRelease(_offscreenMsaaTexture);

        if (_offscreenResolveTextureView != null && !sameView)
        {
            _wgpu.TextureViewRelease(_offscreenResolveTextureView);
            _offscreenResolveTextureView = null;
        }

        if (_offscreenResolveTexture != null && !sameTexture)
        {
            _wgpu.TextureRelease(_offscreenResolveTexture);
            _offscreenResolveTexture = null;
        }

        if (_offscreenDepthTextureView != null)
            _wgpu.TextureViewRelease(_offscreenDepthTextureView);

        if (_offscreenDepthTexture != null)
            _wgpu.TextureRelease(_offscreenDepthTexture);

        _offscreenMsaaTextureView = null;
        _offscreenMsaaTexture = null;
        _offscreenDepthTextureView = null;
        _offscreenDepthTexture = null;
    }

    public bool BeginFrame()
    {
        // Validate device
        if (_device == null)
            return false;

        // Check for window resize and reconfigure surface if needed
        var windowSize = _config.Platform.WindowSize;
        var newWidth = (int)windowSize.X;
        var newHeight = (int)windowSize.Y;
        if (newWidth != _surfaceWidth || newHeight != _surfaceHeight)
        {
            _surfaceWidth = newWidth;
            _surfaceHeight = newHeight;

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

        // Acquire surface texture for this frame
        SurfaceTexture surfaceTexture;
        _wgpu.SurfaceGetCurrentTexture(_surface, &surfaceTexture);

        if (surfaceTexture.Status != SurfaceGetCurrentTextureStatus.Success)
            return false;

        _currentSurfaceTexture = surfaceTexture.Texture;

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
    }

    public void Clear(Color color)
    {
        // Clear is handled in BeginScenePass
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        // Clamp viewport to surface bounds (WebGPU requires viewport within render target)
        width = Math.Min(width, _surfaceWidth - x);
        height = Math.Min(height, _surfaceHeight - y);
        if (width <= 0 || height <= 0)
            return;

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
        _state.ScissorX = Math.Max(0, x);
        _state.ScissorY = Math.Max(0, y);
        _state.ScissorW = Math.Max(1, width);
        _state.ScissorH = Math.Max(1, height);

        if (_currentRenderPass != null)
        {
            _wgpu.RenderPassEncoderSetScissorRect(_currentRenderPass,
                (uint)_state.ScissorX, (uint)_state.ScissorY,
                (uint)_state.ScissorW, (uint)_state.ScissorH);
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

    private void CreateFullscreenQuad()
    {
        // Create fullscreen quad mesh (2 triangles covering NDC -1 to 1)
        _fullscreenQuadMesh = CreateMesh<CompositeVertex>(4, 6, BufferUsage.Static, "fullscreen_quad");

        // Define vertices: position in NDC space, UV coordinates
        var vertices = new CompositeVertex[4];
        vertices[0] = new CompositeVertex { Position = new Vector2(-1, -1), UV = new Vector2(0, 0) }; // Bottom-left
        vertices[1] = new CompositeVertex { Position = new Vector2(1, -1), UV = new Vector2(1, 0) };  // Bottom-right
        vertices[2] = new CompositeVertex { Position = new Vector2(-1, 1), UV = new Vector2(0, 1) };  // Top-left
        vertices[3] = new CompositeVertex { Position = new Vector2(1, 1), UV = new Vector2(1, 1) };   // Top-right

        // Define indices for 2 triangles (CCW winding)
        var indices = new ushort[6];
        indices[0] = 0; indices[1] = 1; indices[2] = 2; // First triangle: bottom-left, bottom-right, top-left
        indices[3] = 1; indices[4] = 3; indices[5] = 2; // Second triangle: bottom-right, top-right, top-left

        // Upload to GPU
        UpdateMesh(_fullscreenQuadMesh,
            MemoryMarshal.AsBytes(vertices.AsSpan()),
            indices.AsSpan());
    }
}
