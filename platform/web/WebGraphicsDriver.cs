//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Buffers;
using System.Numerics;
using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using System.Runtime.Versioning;
using System.Text;
using NoZ.Platform;

namespace NoZ.Platform.Web;

// ArrayPool helper extension for efficient memory reuse
internal static class ArrayPoolExtensions
{
    public static ArraySegment<byte> RentAndCopy(this ArrayPool<byte> pool, ReadOnlySpan<byte> source, out byte[] rented)
    {
        if (source.Length == 0)
        {
            rented = Array.Empty<byte>();
            return new ArraySegment<byte>(rented, 0, 0);
        }
        rented = pool.Rent(source.Length);
        source.CopyTo(rented);
        return new ArraySegment<byte>(rented, 0, source.Length);
    }
}

/// <summary>
/// IGraphicsDriver implementation for browser WebGPU using JSImport interop
/// </summary>
[SupportedOSPlatform("browser")]
public class WebGraphicsDriver : IGraphicsDriver
{
    private GraphicsDriverConfig _config = null!;
    private int _surfaceWidth;
    private int _surfaceHeight;
    private string _surfaceFormat = "";

    // Resource tracking (ID-based, matching JS side)
    private int _nextMeshId = 1;
    private int _nextBufferId = 1;
    private int _nextTextureId = 2; // 1 reserved for white texture
    private int _nextShaderId = 1;

    private readonly Dictionary<nuint, MeshInfo> _meshes = new();
    private readonly Dictionary<nuint, BufferInfo> _buffers = new();
    private readonly Dictionary<nuint, TextureInfo> _textures = new();
    private readonly Dictionary<nuint, ShaderInfo> _shaders = new();

    // Cached state
    private CachedState _state;

    // Per-name uniform data storage
    private readonly Dictionary<string, byte[]> _uniformData = new();

    // Globals buffer management
    private const int MaxGlobalsBuffers = 64;
    private const int GlobalsBufferSize = 80; // mat4 (64) + float (4) + padding (12)
    private readonly int[] _globalsBuffers = new int[MaxGlobalsBuffers];
    private int _globalsBufferCount;
    private int _currentGlobalsIndex = -1;

    // Pre-allocated buffers to reduce per-frame allocations
    private readonly byte[] _globalsWriteBuffer = new byte[GlobalsBufferSize];
    private readonly StringBuilder _jsonBuilder = new(512);
    private readonly JSObject[] _singleColorAttachment = new JSObject[1]; // Reusable array for render pass

    public string ShaderExtension => "";

    private struct CachedState
    {
        public nuint BoundShader;
        public BlendMode BlendMode;
        public TextureFilter TextureFilter;
        public nuint BoundMesh;
        public nuint[] BoundTextures;
        public TextureFilter[] TextureFilters;
        public bool PipelineDirty;
        public bool BindGroupDirty;
        public RectInt Viewport;
        public bool ScissorEnabled;
        public RectInt Scissor;
        public int CurrentPassSampleCount;
        public int CurrentPipelineId;
        public int CurrentBindGroupId;

        /// <summary>Reset all cached state to defaults, preserving allocated arrays.</summary>
        public void Reset()
        {
            BoundShader = 0;
            BlendMode = BlendMode.None;
            TextureFilter = TextureFilter.Point;
            BoundMesh = 0;
            Array.Clear(BoundTextures);
            Array.Clear(TextureFilters);
            PipelineDirty = true;
            BindGroupDirty = true;
            Viewport = default;
            ScissorEnabled = false;
            Scissor = default;
            CurrentPassSampleCount = 0;
            CurrentPipelineId = 0;
            CurrentBindGroupId = 0;
        }
    }

    private struct MeshInfo
    {
        public int JsMeshId;
        public int Stride;
        public int MaxVertices;
        public int MaxIndices;
        public VertexFormatDescriptor Descriptor;
    }

    private struct BufferInfo
    {
        public int JsBufferId;
        public int SizeInBytes;
        public BufferUsage Usage;
    }

    private struct TextureInfo
    {
        public int JsTextureId;
        public int Width;
        public int Height;
        public int Layers;
        public string Format;
        public bool IsArray;
    }

    private struct ShaderInfo
    {
        public string Name;
        public int VertexModuleId;
        public int FragmentModuleId;
        public int BindGroupLayoutId;
        public int PipelineLayoutId;
        public Dictionary<PsoKey, int> PsoCache; // Maps PSO key to JS pipeline ID
        public int BindGroupEntryCount;
        public List<ShaderBinding> Bindings;
        public List<TextureSlotInfo> TextureSlots;
        public Dictionary<string, uint> UniformBindings;
        public Dictionary<string, int> UniformBuffers; // Per-shader uniform buffers (name -> JS buffer ID)
    }

    private struct TextureSlotInfo
    {
        public uint TextureBinding;
        public uint SamplerBinding;
        public bool IsUnfilterable;
    }

    private struct PsoKey : IEquatable<PsoKey>
    {
        public nuint ShaderHandle;
        public BlendMode BlendMode;
        public int VertexStride;
        public int MsaaSamples;

        public bool Equals(PsoKey other) =>
            ShaderHandle == other.ShaderHandle &&
            BlendMode == other.BlendMode &&
            VertexStride == other.VertexStride &&
            MsaaSamples == other.MsaaSamples;

        public override bool Equals(object? obj) => obj is PsoKey other && Equals(other);

        public override int GetHashCode() => HashCode.Combine(ShaderHandle, BlendMode, VertexStride, MsaaSamples);
    }

    public void Init(GraphicsDriverConfig config)
    {
        _config = config;
        // Actual initialization happens in InitAsync
    }

    /// <summary>
    /// Async initialization - must be called after Init()
    /// </summary>
    public async Task InitAsync()
    {
        // Import the JS module first before calling any JSImport functions
        // Use absolute path from web root (not relative, which resolves from _framework/)
        await JSHost.ImportAsync("noz-webgpu", "/js/noz/noz-webgpu.js");

        var result = await WebGPUInterop.InitAsync("#canvas");

        if (result == null)
            throw new InvalidOperationException("WebGPU initialization failed");

        _surfaceWidth = result.GetPropertyAsInt32("width");
        _surfaceHeight = result.GetPropertyAsInt32("height");
        _surfaceFormat = result.GetPropertyAsString("format") ?? "bgra8unorm";

        _state = new CachedState
        {
            BoundTextures = new nuint[8],
            TextureFilters = new TextureFilter[8]
        };

    }

    public void Shutdown()
    {
        WebGPUInterop.Shutdown();

        _meshes.Clear();
        _buffers.Clear();
        _textures.Clear();
        _shaders.Clear();
    }

    // ============================================================================
    // Frame Management
    // ============================================================================

    public bool BeginFrame()
    {
        if (!WebGPUInterop.BeginFrame())
            return false;

        // Check for resize
        var windowSize = _config.Platform.WindowSize;
        var newWidth = (int)windowSize.X;
        var newHeight = (int)windowSize.Y;
        if (newWidth != _surfaceWidth || newHeight != _surfaceHeight)
        {
            _surfaceWidth = newWidth;
            _surfaceHeight = newHeight;
        }

        _state.Reset();

        return true;
    }

    public void EndFrame()
    {
        WebGPUInterop.EndFrame();
    }

    public void Clear(Color color)
    {
        // Clear is handled in BeginScenePass
    }

    // ============================================================================
    // Viewport / Scissor
    // ============================================================================

    public void SetViewport(in RectInt viewport)
    {
        // Clamp to current render target dimensions (RT or surface)
        int targetWidth, targetHeight;
        if (_activeRenderTexture != 0 && _renderTextures.TryGetValue(_activeRenderTexture, out var rt))
        {
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
        WebGPUInterop.SetViewport(clampedViewport.X, clampedViewport.Y, clampedViewport.Width, clampedViewport.Height, 0, 1);
    }

    public void SetScissor(in RectInt scissor)
    {
        // Get current render target dimensions
        int targetWidth, targetHeight;
        if (_activeRenderTexture != 0 && _renderTextures.TryGetValue(_activeRenderTexture, out var rt))
        {
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

        WebGPUInterop.SetScissorRect(clampedScissor.X, clampedScissor.Y, clampedScissor.Width, clampedScissor.Height);
    }

    public void ClearScissor()
    {
        _state.ScissorEnabled = false;

        // Use render texture dimensions if rendering to texture, otherwise surface dimensions
        int width, height;
        if (_activeRenderTexture != 0 && _renderTextures.TryGetValue(_activeRenderTexture, out var rt))
        {
            width = rt.Width;
            height = rt.Height;
        }
        else
        {
            width = _surfaceWidth;
            height = _surfaceHeight;
        }
        WebGPUInterop.SetScissorRect(0, 0, width, height);
    }

    // ============================================================================
    // Mesh Management
    // ============================================================================

    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        var descriptor = T.GetFormatDescriptor();

        var jsMeshId = WebGPUInterop.CreateMesh(maxVertices, maxIndices, descriptor.Stride, name);

        var handle = (nuint)_nextMeshId++;
        _meshes[handle] = new MeshInfo
        {
            JsMeshId = jsMeshId,
            Stride = descriptor.Stride,
            MaxVertices = maxVertices,
            MaxIndices = maxIndices,
            Descriptor = descriptor
        };

        return handle;
    }

    public void DestroyMesh(nuint handle)
    {
        if (_meshes.TryGetValue(handle, out var mesh))
        {
            WebGPUInterop.DestroyMesh(mesh.JsMeshId);
            _meshes.Remove(handle);
        }
    }

    public void BindMesh(nuint handle)
    {
        if (_state.BoundMesh == handle)
            return;

        _state.BoundMesh = handle;
        _state.PipelineDirty = true;
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        if (!_meshes.TryGetValue(handle, out var mesh))
        {
            Log.Error($"Mesh {handle} not found");
            return;
        }

        // Use ArrayPool to reduce allocations for frequent mesh updates
        var vertexSegment = ArrayPool<byte>.Shared.RentAndCopy(vertexData, out var rentedVertex);
        var indexSegment = ArrayPool<byte>.Shared.RentAndCopy(MemoryMarshal.AsBytes(indexData), out var rentedIndex);

        try
        {
            WebGPUInterop.UpdateMesh(mesh.JsMeshId, vertexSegment, indexSegment);
        }
        finally
        {
            if (rentedVertex.Length > 0) ArrayPool<byte>.Shared.Return(rentedVertex);
            if (rentedIndex.Length > 0) ArrayPool<byte>.Shared.Return(rentedIndex);
        }
    }

    // ============================================================================
    // Buffer Management
    // ============================================================================

    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        var jsBufferId = WebGPUInterop.CreateBuffer(sizeInBytes, (int)(WebGPUBufferUsage.Uniform | WebGPUBufferUsage.CopyDst), name);

        var handle = (nuint)_nextBufferId++;
        _buffers[handle] = new BufferInfo
        {
            JsBufferId = jsBufferId,
            SizeInBytes = sizeInBytes,
            Usage = usage
        };

        return handle;
    }

    public void DestroyBuffer(nuint handle)
    {
        if (_buffers.TryGetValue(handle, out var buffer))
        {
            WebGPUInterop.DestroyBuffer(buffer.JsBufferId);
            _buffers.Remove(handle);
        }
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        if (!_buffers.TryGetValue(buffer, out var bufferInfo))
        {
            Log.Error($"Buffer {buffer} not found");
            return;
        }

        var segment = ArrayPool<byte>.Shared.RentAndCopy(data, out var rented);
        try
        {
            WebGPUInterop.WriteBuffer(bufferInfo.JsBufferId, offsetBytes, segment);
        }
        finally
        {
            if (rented.Length > 0) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        // Uniform buffer binding is handled through bind groups
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Texture Management
    // ============================================================================

    private static string MapTextureFormat(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8 => WebGPUTextureFormat.RGBA8,
            TextureFormat.RGB8 => WebGPUTextureFormat.RGBA8, // WebGPU doesn't support RGB8
            TextureFormat.R8 => WebGPUTextureFormat.R8,
            TextureFormat.RG8 => WebGPUTextureFormat.RGBA8, // Fallback
            TextureFormat.RGBA32F => WebGPUTextureFormat.RGBA32F,
            TextureFormat.BGRA8 => WebGPUTextureFormat.BGRA8,
            _ => WebGPUTextureFormat.RGBA8
        };
    }

    private static int GetBytesPerPixel(TextureFormat format)
    {
        return format switch
        {
            TextureFormat.RGBA8 => 4,
            TextureFormat.RGB8 => 3,
            TextureFormat.R8 => 1,
            TextureFormat.RG8 => 2,
            TextureFormat.RGBA32F => 16,
            _ => 4
        };
    }

    public nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name = null)
    {
        var gpuFormat = MapTextureFormat(format);
        var usage = (int)(WebGPUTextureUsage.TextureBinding | WebGPUTextureUsage.CopyDst);

        var jsTextureId = WebGPUInterop.CreateTexture(width, height, gpuFormat, usage, name);

        if (data.Length > 0)
        {
            var bytesPerPixel = GetBytesPerPixel(format);
            WebGPUInterop.WriteTexture(jsTextureId, new ArraySegment<byte>(data.ToArray()), width, height, width * bytesPerPixel, 0);
        }

        var handle = (nuint)_nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = 1,
            Format = gpuFormat,
            IsArray = false
        };

        return handle;
    }

    public void UpdateTexture(nuint handle, in Vector2Int size, ReadOnlySpan<byte> data)
    {
        if (!_textures.TryGetValue(handle, out var textureInfo))
        {
            Log.Error($"Texture {handle} not found");
            return;
        }

        var bytesPerPixel = textureInfo.Format switch
        {
            "rgba8" => 4,
            "r8" => 1,
            "rgba32f" => 16,
            _ => 4
        };

        var segment = ArrayPool<byte>.Shared.RentAndCopy(data, out var rented);
        try
        {
            WebGPUInterop.WriteTexture(textureInfo.JsTextureId, segment, size.X, size.Y, size.X * bytesPerPixel, 0);
        }
        finally
        {
            if (rented.Length > 0) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void UpdateTextureRegion(nuint handle, in RectInt region, ReadOnlySpan<byte> data, int srcWidth = -1)
    {
        if (!_textures.TryGetValue(handle, out var textureInfo))
        {
            Log.Error($"Texture {handle} not found");
            return;
        }

        var bytesPerPixel = textureInfo.Format switch
        {
            "rgba8" => 4,
            "r8" => 1,
            "rgba32f" => 16,
            _ => 4
        };

        var rowWidth = srcWidth < 0 ? region.Width : srcWidth;
        var segment = ArrayPool<byte>.Shared.RentAndCopy(data, out var rented);
        try
        {
            WebGPUInterop.WriteTextureRegion(textureInfo.JsTextureId, segment, region.X, region.Y, region.Width, region.Height, rowWidth * bytesPerPixel);
        }
        finally
        {
            if (rented.Length > 0) ArrayPool<byte>.Shared.Return(rented);
        }
    }

    public void DestroyTexture(nuint handle)
    {
        if (_textures.TryGetValue(handle, out var texture))
        {
            WebGPUInterop.DestroyTexture(texture.JsTextureId);
            _textures.Remove(handle);
        }
    }

    public void BindTexture(nuint handle, int slot, TextureFilter filter = TextureFilter.Point)
    {
        if (slot < 0 || slot >= 8)
        {
            Log.Error($"Texture slot {slot} out of range (0-7)!");
            return;
        }

        if (_state.BoundTextures[slot] == handle && _state.TextureFilters[slot] == filter)
            return;

        _state.BoundTextures[slot] = handle;
        _state.TextureFilters[slot] = filter;
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Texture Array Management
    // ============================================================================

    public nuint CreateTextureArray(int width, int height, int layers)
    {
        var jsTextureId = WebGPUInterop.CreateTextureArray(width, height, layers, WebGPUTextureFormat.RGBA8, null);

        var handle = (nuint)_nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = layers,
            Format = WebGPUTextureFormat.RGBA8,
            IsArray = true
        };

        return handle;
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name = null)
    {
        var gpuFormat = MapTextureFormat(format);
        var layers = layerData.Length;

        var jsTextureId = WebGPUInterop.CreateTextureArray(width, height, layers, gpuFormat, name);

        var bytesPerPixel = GetBytesPerPixel(format);
        for (int i = 0; i < layers; i++)
        {
            WebGPUInterop.WriteTexture(jsTextureId, new ArraySegment<byte>(layerData[i]), width, height, width * bytesPerPixel, i);
        }

        var handle = (nuint)_nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = layers,
            Format = gpuFormat,
            IsArray = true
        };

        return handle;
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        if (!_textures.TryGetValue(handle, out var textureInfo))
        {
            Log.Error($"Texture {handle} not found");
            return;
        }

        var bytesPerPixel = textureInfo.Format switch
        {
            "rgba8" => 4,
            "r8" => 1,
            "rgba32f" => 16,
            _ => 4
        };

        WebGPUInterop.WriteTexture(textureInfo.JsTextureId, new ArraySegment<byte>(data.ToArray()), textureInfo.Width, textureInfo.Height, textureInfo.Width * bytesPerPixel, layer);
    }

    // ============================================================================
    // Shader Management
    // ============================================================================

    public nuint CreateShader(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings)
    {
        var vertexModuleId = WebGPUInterop.CreateShaderModule(vertexSource, $"{name}_vertex");
        var fragmentModuleId = WebGPUInterop.CreateShaderModule(fragmentSource, $"{name}_fragment");

        // Build texture slots and uniform bindings from metadata
        var textureSlots = new List<TextureSlotInfo>();
        var uniformBindings = new Dictionary<string, uint>();

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];

            if (binding.Type == ShaderBindingType.UniformBuffer)
                uniformBindings[binding.Name] = binding.Binding;

            if (binding.Type == ShaderBindingType.Texture2D || binding.Type == ShaderBindingType.Texture2DArray)
            {
                bool hasSampler = i + 1 < bindings.Count && bindings[i + 1].Type == ShaderBindingType.Sampler;
                bool isUnfilterable = !hasSampler;

                // Update binding type to unfilterable if no sampler follows (browser WebGPU requires this)
                if (isUnfilterable && binding.Type == ShaderBindingType.Texture2D)
                {
                    bindings[i] = binding with { Type = ShaderBindingType.Texture2DUnfilterable };
                }

                textureSlots.Add(new TextureSlotInfo
                {
                    TextureBinding = binding.Binding,
                    SamplerBinding = hasSampler ? binding.Binding + 1 : 0,
                    IsUnfilterable = isUnfilterable
                });
            }
        }

        // Create bind group layout
        var layoutEntries = CreateBindGroupLayoutEntries(bindings);
        var bindGroupLayoutId = WebGPUInterop.CreateBindGroupLayout(layoutEntries, $"{name}_layout");

        // Create pipeline layout
        var pipelineLayoutId = WebGPUInterop.CreatePipelineLayout(new[] { bindGroupLayoutId }, $"{name}_pipeline_layout");

        var handle = (nuint)_nextShaderId++;
        _shaders[handle] = new ShaderInfo
        {
            Name = name,
            VertexModuleId = vertexModuleId,
            FragmentModuleId = fragmentModuleId,
            BindGroupLayoutId = bindGroupLayoutId,
            PipelineLayoutId = pipelineLayoutId,
            PsoCache = new Dictionary<PsoKey, int>(),
            BindGroupEntryCount = bindings.Count,
            Bindings = bindings,
            TextureSlots = textureSlots,
            UniformBindings = uniformBindings,
            UniformBuffers = new Dictionary<string, int>()
        };

        return handle;
    }

    private JSObject[] CreateBindGroupLayoutEntries(List<ShaderBinding> bindings)
    {
        var entries = new JSObject[bindings.Count];

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];
            var entry = JSObjectHelper.CreateBindGroupLayoutEntry(binding);
            entries[i] = entry;
        }

        return entries;
    }

    public void DestroyShader(nuint handle)
    {
        if (_shaders.TryGetValue(handle, out var shader))
        {
            // Destroy cached pipelines
            foreach (var pipelineId in shader.PsoCache.Values)
            {
                WebGPUInterop.DestroyRenderPipeline(pipelineId);
            }

            // Destroy per-shader uniform buffers
            foreach (var bufferId in shader.UniformBuffers.Values)
            {
                WebGPUInterop.DestroyBuffer(bufferId);
            }

            WebGPUInterop.DestroyShaderModule(shader.VertexModuleId);
            WebGPUInterop.DestroyShaderModule(shader.FragmentModuleId);

            _shaders.Remove(handle);
        }
    }

    public void BindShader(nuint handle)
    {
        if (_state.BoundShader == handle)
            return;

        _state.BoundShader = handle;
        _state.PipelineDirty = true;
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Render State
    // ============================================================================

    public void SetBlendMode(BlendMode mode)
    {
        if (_state.BlendMode == mode)
            return;

        _state.BlendMode = mode;
        _state.PipelineDirty = true;
    }

    public void SetTextureFilter(TextureFilter filter)
    {
        if (_state.TextureFilter == filter)
            return;

        _state.TextureFilter = filter;
        _state.BindGroupDirty = true;
    }

    public void SetUniform(string name, ReadOnlySpan<byte> data)
    {
        if (!_uniformData.TryGetValue(name, out var existing) || existing.Length != data.Length)
            _uniformData[name] = new byte[data.Length];

        data.CopyTo(_uniformData[name]);
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Globals Management
    // ============================================================================

    public void SetGlobalsCount(int count)
    {
        while (_globalsBufferCount < count)
        {
            var bufferId = WebGPUInterop.CreateBuffer(GlobalsBufferSize, (int)(WebGPUBufferUsage.Uniform | WebGPUBufferUsage.CopyDst), $"globals_{_globalsBufferCount}");
            _globalsBuffers[_globalsBufferCount] = bufferId;
            _globalsBufferCount++;
        }
    }

    public void SetGlobals(int index, ReadOnlySpan<byte> data)
    {
        if (index < 0 || index >= _globalsBufferCount)
            return;

        // Copy to pre-allocated buffer to avoid allocation
        data.CopyTo(_globalsWriteBuffer);
        WebGPUInterop.WriteBuffer(_globalsBuffers[index], 0, new ArraySegment<byte>(_globalsWriteBuffer, 0, data.Length));
    }

    public void BindGlobals(int index)
    {
        if (_currentGlobalsIndex == index)
            return;

        _currentGlobalsIndex = index;
        _state.BindGroupDirty = true;
    }

    // ============================================================================
    // Drawing
    // ============================================================================

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        // Update pipeline if needed
        if (_state.PipelineDirty)
        {
            var pipelineId = GetOrCreatePipeline(_state.BoundShader, _state.BlendMode, _meshes[_state.BoundMesh].Stride);
            if (pipelineId > 0)
            {
                WebGPUInterop.SetPipeline(pipelineId);
                _state.CurrentPipelineId = pipelineId;
            }
            _state.PipelineDirty = false;
        }

        // Update bind group if needed
        if (_state.BindGroupDirty)
        {
            var bindGroupId = CreateBindGroup();
            if (bindGroupId > 0)
            {
                WebGPUInterop.SetBindGroup(0, bindGroupId);
                _state.CurrentBindGroupId = bindGroupId;
            }
            _state.BindGroupDirty = false;
        }

        // Bind mesh buffers
        var mesh = _meshes[_state.BoundMesh];
        WebGPUInterop.SetVertexBuffer(0, mesh.JsMeshId);
        WebGPUInterop.SetIndexBuffer(mesh.JsMeshId);

        // Apply scissor (Y flip needed - WebGPU uses top-down, engine uses bottom-up)
        if (_state.ScissorEnabled)
        {
            WebGPUInterop.SetScissorRect(
                _state.Scissor.X,
                _state.Viewport.Height - _state.Scissor.Y - _state.Scissor.Height,
                _state.Scissor.Width,
                _state.Scissor.Height);
        }
        else
        {
            int width, height;
            if (_activeRenderTexture != 0 && _renderTextures.TryGetValue(_activeRenderTexture, out var rt))
            {
                width = rt.Width;
                height = rt.Height;
            }
            else
            {
                width = _surfaceWidth;
                height = _surfaceHeight;
            }
            WebGPUInterop.SetScissorRect(0, 0, width, height);
        }

        // Draw
        WebGPUInterop.DrawIndexed(indexCount, 1, firstIndex, baseVertex, 0);
    }

    private int GetOrCreatePipeline(nuint shaderHandle, BlendMode blendMode, int vertexStride)
    {
        if (!_shaders.TryGetValue(shaderHandle, out var shader))
        {
            Log.Error($"Shader {shaderHandle} not found");
            return 0;
        }

        var key = new PsoKey
        {
            ShaderHandle = shaderHandle,
            BlendMode = blendMode,
            VertexStride = vertexStride,
            MsaaSamples = _state.CurrentPassSampleCount
        };

        if (shader.PsoCache.TryGetValue(key, out var pipelineId))
            return pipelineId;

        // Create new pipeline
        var mesh = _meshes[_state.BoundMesh];
        var descriptor = JSObjectHelper.CreateRenderPipelineDescriptor(
            shader.VertexModuleId,
            shader.FragmentModuleId,
            shader.PipelineLayoutId,
            mesh.Descriptor,
            blendMode,
            _state.CurrentPassSampleCount,
            _surfaceFormat,
            $"{shader.Name}_{blendMode}_{vertexStride}b_{key.MsaaSamples}x"
        );

        pipelineId = WebGPUInterop.CreateRenderPipeline(descriptor);
        shader.PsoCache[key] = pipelineId;

        return pipelineId;
    }

    private int CreateBindGroup()
    {
        if (!_shaders.TryGetValue(_state.BoundShader, out var shader))
        {
            Log.Error($"Shader {_state.BoundShader} not found");
            return 0;
        }

        var bindings = shader.Bindings;
        if (bindings == null || bindings.Count == 0)
        {
            Log.Error("Shader has no binding metadata!");
            return 0;
        }

        // Build JSON using StringBuilder to avoid allocations
        _jsonBuilder.Clear();
        _jsonBuilder.Append('[');
        bool first = true;

        for (int i = 0; i < bindings.Count; i++)
        {
            var binding = bindings[i];

            if (!first) _jsonBuilder.Append(',');
            first = false;

            switch (binding.Type)
            {
                case ShaderBindingType.UniformBuffer:
                {
                    int bufferId;
                    int bufferSize;

                    if (binding.Name == "globals")
                    {
                        if (_currentGlobalsIndex < 0 || _currentGlobalsIndex >= _globalsBufferCount)
                        {
                            Log.Error($"Globals index {_currentGlobalsIndex} out of range!");
                            return 0;
                        }
                        bufferId = _globalsBuffers[_currentGlobalsIndex];
                        bufferSize = GlobalsBufferSize;
                    }
                    else
                    {
                        if (!_uniformData.TryGetValue(binding.Name, out var uniformData))
                        {
                            Log.Error($"Uniform '{binding.Name}' not set!");
                            return 0;
                        }

                        if (!shader.UniformBuffers.TryGetValue(binding.Name, out bufferId) || bufferId == 0)
                        {
                            bufferId = WebGPUInterop.CreateBuffer(uniformData.Length, (int)(WebGPUBufferUsage.Uniform | WebGPUBufferUsage.CopyDst), $"{shader.Name}_{binding.Name}");
                            shader.UniformBuffers[binding.Name] = bufferId;
                        }

                        WebGPUInterop.WriteBuffer(bufferId, 0, new ArraySegment<byte>(uniformData));
                        bufferSize = uniformData.Length;
                    }

                    _jsonBuilder.Append("{\"type\":\"buffer\",\"binding\":");
                    _jsonBuilder.Append(binding.Binding);
                    _jsonBuilder.Append(",\"bufferId\":");
                    _jsonBuilder.Append(bufferId);
                    _jsonBuilder.Append(",\"size\":");
                    _jsonBuilder.Append(bufferSize);
                    _jsonBuilder.Append('}');
                    break;
                }

                case ShaderBindingType.Texture2D:
                case ShaderBindingType.Texture2DArray:
                case ShaderBindingType.Texture2DUnfilterable:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, shader);
                    nuint textureHandle = textureSlot >= 0 ? _state.BoundTextures[textureSlot] : 0;

                    if (textureHandle == 0 || !_textures.TryGetValue(textureHandle, out var tex))
                    {
                        Log.Error($"Texture slot {textureSlot} (binding {binding.Binding}) not bound!");
                        return 0;
                    }

                    _jsonBuilder.Append("{\"type\":\"texture\",\"binding\":");
                    _jsonBuilder.Append(binding.Binding);
                    _jsonBuilder.Append(",\"textureId\":");
                    _jsonBuilder.Append(tex.JsTextureId);
                    _jsonBuilder.Append('}');
                    break;
                }

                case ShaderBindingType.Sampler:
                {
                    int textureSlot = GetTextureSlotForBinding(binding.Binding, shader);
                    var slotFilter = textureSlot >= 0 ? _state.TextureFilters[textureSlot] : TextureFilter.Point;
                    bool useLinear = slotFilter == TextureFilter.Linear;

                    _jsonBuilder.Append("{\"type\":\"sampler\",\"binding\":");
                    _jsonBuilder.Append(binding.Binding);
                    _jsonBuilder.Append(",\"useLinear\":");
                    _jsonBuilder.Append(useLinear ? "true" : "false");
                    _jsonBuilder.Append('}');
                    break;
                }
            }
        }

        _jsonBuilder.Append(']');
        var bindGroupId = WebGPUInterop.CreateBindGroupFromJson(shader.BindGroupLayoutId, _jsonBuilder.ToString(), null);
        return bindGroupId;
    }

    // No longer needed - using StringBuilder instead
    /*
    private class BindGroupEntryData
    {
        public string type { get; set; } = "";
        public int binding { get; set; }
        public int bufferId { get; set; }
        public int size { get; set; }
        public int textureId { get; set; }
        public bool useLinear { get; set; }
    }
    */

    private int GetTextureSlotForBinding(uint bindingNumber, ShaderInfo shader)
    {
        for (int i = 0; i < shader.TextureSlots.Count; i++)
        {
            var slot = shader.TextureSlots[i];
            if (slot.TextureBinding == bindingNumber || slot.SamplerBinding == bindingNumber)
                return i;
        }
        return -1;
    }

    // ============================================================================
    // Fences (stubs for web)
    // ============================================================================

    public nuint CreateFence() => 0;
    public void WaitFence(nuint fence) { }
    public void DeleteFence(nuint fence) { }

    // ============================================================================
    // Render Passes
    // ============================================================================

    public void BeginScenePass(Color clearColor)
    {
        // Reset all cached state — new render pass encoder needs everything rebound
        _state.Reset();
        _state.CurrentPassSampleCount = 1;
        _currentGlobalsIndex = -1;

        _singleColorAttachment[0] = JSObjectHelper.CreateColorAttachment(
            -1, // surface texture
            0, // no resolve
            "clear",
            "store",
            clearColor
        );

        WebGPUInterop.BeginRenderPass(_singleColorAttachment, null, "ScenePass");
        WebGPUInterop.SetViewport(0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);
    }

    public void EndScenePass()
    {
        WebGPUInterop.EndRenderPass();
    }

    public void ResumeScenePass()
    {
        // Reset all cached state — new render pass encoder needs everything rebound
        _state.Reset();
        _state.CurrentPassSampleCount = 1;
        _currentGlobalsIndex = -1;

        _singleColorAttachment[0] = JSObjectHelper.CreateColorAttachment(
            -1, // surface texture
            0, // no resolve
            "load",
            "store",
            Color.Transparent // not used with "load"
        );

        WebGPUInterop.BeginRenderPass(_singleColorAttachment, null, "ScenePass (resumed)");
        WebGPUInterop.SetViewport(0, 0, _surfaceWidth, _surfaceHeight, 0, 1);
        WebGPUInterop.SetScissorRect(0, 0, _surfaceWidth, _surfaceHeight);
    }

    // ============================================================================
    // Render Texture (for capturing to image)
    // ============================================================================

    private readonly Dictionary<nuint, RenderTextureInfo> _renderTextures = new();
    private nuint _activeRenderTexture;

    private struct RenderTextureInfo
    {
        public int JsTextureId;
        public int Width;
        public int Height;
        public int SampleCount;
        public string Format;
    }

    public nuint CreateRenderTexture(int width, int height, TextureFormat format = TextureFormat.BGRA8, int sampleCount = 1, string? name = null)
    {
        var gpuFormat = MapTextureFormat(format);
        // JS side allocates from shared nextTextureId and stores in both textures + renderTextures maps
        // When sampleCount > 1, JS creates both MSAA and resolve textures
        var jsTextureId = WebGPUInterop.CreateRenderTexture(width, height, gpuFormat, sampleCount, name);

        // Use shared handle space so RT handles work with BindTexture/CreateBindGroup
        var handle = (nuint)_nextTextureId++;
        _renderTextures[handle] = new RenderTextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            SampleCount = sampleCount,
            Format = gpuFormat
        };

        // Also store in _textures so CreateBindGroup can resolve the texture
        // JS side ensures the textures map points to the resolve texture for sampling
        _textures[handle] = new TextureInfo
        {
            JsTextureId = jsTextureId,
            Width = width,
            Height = height,
            Layers = 1,
            Format = gpuFormat,
            IsArray = true  // Sprite shader expects texture_2d_array; JS side creates 2DArray view
        };

        return handle;
    }

    public void DestroyRenderTexture(nuint handle)
    {
        if (_renderTextures.TryGetValue(handle, out var rt))
        {
            WebGPUInterop.DestroyRenderTexture(rt.JsTextureId);
            _renderTextures.Remove(handle);
        }
        _textures.Remove(handle);
    }

    public void BeginRenderTexturePass(nuint renderTexture, Color clearColor)
    {
        if (!_renderTextures.TryGetValue(renderTexture, out var rt))
        {
            Log.Error($"Render texture {renderTexture} not found");
            return;
        }

        _activeRenderTexture = renderTexture;

        // Reset all cached state — new render pass encoder needs everything rebound
        _state.Reset();
        _state.CurrentPassSampleCount = rt.SampleCount;
        _currentGlobalsIndex = -1;

        WebGPUInterop.BeginRenderTexturePass(rt.JsTextureId, clearColor.R, clearColor.G, clearColor.B, clearColor.A);
        WebGPUInterop.SetViewport(0, 0, rt.Width, rt.Height, 0, 1);
        WebGPUInterop.SetScissorRect(0, 0, rt.Width, rt.Height);
    }

    public void EndRenderTexturePass()
    {
        _activeRenderTexture = 0;
        WebGPUInterop.EndRenderTexturePass();
    }

    public async Task<byte[]> ReadRenderTexturePixelsAsync(nuint renderTexture)
    {
        if (!_renderTextures.TryGetValue(renderTexture, out var rt))
        {
            Log.Error($"Render texture {renderTexture} not found");
            return Array.Empty<byte>();
        }

        var base64 = await WebGPUInterop.ReadRenderTexturePixelsAsync(rt.JsTextureId);
        var result = Convert.FromBase64String(base64);

        // Swizzle BGRA to RGBA if needed
        if (rt.Format == WebGPUTextureFormat.BGRA8)
        {
            for (int i = 0; i < result.Length; i += 4)
            {
                (result[i], result[i + 2]) = (result[i + 2], result[i]); // Swap B and R
            }
        }

        return result;
    }

}
