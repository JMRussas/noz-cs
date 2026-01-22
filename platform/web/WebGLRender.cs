//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;
using System.Runtime.InteropServices.JavaScript;
using Microsoft.JSInterop;

namespace NoZ.Platform;

public class WebGLGraphicsDriver : IGraphicsDriver
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private GraphicsDriverConfig _config = null!;

    public string ShaderExtension => ".gles";

    private struct TextureInfo
    {
        public uint JsHandle;
        public int Width;
        public int Height;
        public int Layers;
        public bool IsArray;
    }

    private const int MaxTextures = 1024;
    private readonly TextureInfo[] _textures = new TextureInfo[MaxTextures];
    private int _nextTextureId = 1;

    private int _nextMeshId = 1;
    private readonly Dictionary<int, Task<uint>> _pendingMeshes = new();
    private int _nextUniformBufferId = 1;
    private readonly Dictionary<int, Task<uint>> _pendingUniformBuffers = new();
    private int _nextShaderId = 1;
    private readonly Dictionary<int, Task<uint>> _pendingShaders = new();
    private int _nextFenceId = 1;
    private readonly Dictionary<int, Task<uint>> _pendingFences = new();
    private readonly Dictionary<int, Task<uint>> _pendingTextures = new();

    public WebGLGraphicsDriver(IJSRuntime js)
    {
        _js = js;
    }

    public void Init(GraphicsDriverConfig config)
    {
        _config = config;
    }

    public async Task InitAsync(GraphicsDriverConfig config)
    {
        _config = config;
        _module = await _js.InvokeAsync<IJSObjectReference>("import", "/js/noz/noz-webgl.js");
        await _module.InvokeVoidAsync("init");
    }

    public void Shutdown()
    {
        _module?.InvokeVoidAsync("shutdown");
    }

    public void BeginFrame()
    {
        WebGLInterop.BeginFrame();
    }

    public void EndFrame()
    {
        WebGLInterop.EndFrame();
    }

    public void Clear(Color color)
    {
        WebGLInterop.Clear(color.R, color.G, color.B, color.A);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        WebGLInterop.SetViewport(x, y, width, height);
    }

    public void SetScissor(int x, int y, int width, int height)
    {
        WebGLInterop.SetScissor(x, y, width, height);
    }

    public void DisableScissor()
    {
        WebGLInterop.DisableScissor();
    }

    // === Mesh Management ===

    private uint GetJsHandle(int localId, Dictionary<int, Task<uint>> pending)
    {
        if (pending.TryGetValue(localId, out var task) && task.IsCompleted)
        {
            var jsHandle = task.Result;
            pending.Remove(localId);
            return jsHandle;
        }
        return (uint)localId;
    }

    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        if (_module == null) return 0;
        var meshId = _nextMeshId++;
        var descriptor = T.GetFormatDescriptor();
        var task = _module.InvokeAsync<uint>("createMesh", maxVertices, maxIndices, descriptor.Stride, (int)usage).AsTask();
        _pendingMeshes[meshId] = task;
        return (nuint)meshId;
    }

    public void DestroyMesh(nuint handle)
    {
        var jsHandle = GetJsHandle((int)handle, _pendingMeshes);
        _module?.InvokeVoidAsync("destroyMesh", jsHandle);
    }

    public void BindMesh(nuint handle)
    {
        var jsHandle = GetJsHandle((int)handle, _pendingMeshes);
        WebGLInterop.BindMesh((int)jsHandle);
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        if (_module == null) return;
        var jsHandle = GetJsHandle((int)handle, _pendingMeshes);
        _module.InvokeVoidAsync("updateMesh", jsHandle, vertexData.ToArray(), MemoryMarshal.AsBytes(indexData).ToArray());
    }

    // === Uniform Buffer Management ===

    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        if (_module == null) return 0;
        var bufferId = _nextUniformBufferId++;
        var task = _module.InvokeAsync<uint>("createUniformBuffer", sizeInBytes, (int)usage).AsTask();
        _pendingUniformBuffers[bufferId] = task;
        return (nuint)bufferId;
    }

    public void DestroyBuffer(nuint handle)
    {
        var jsHandle = GetJsHandle((int)handle, _pendingUniformBuffers);
        _module?.InvokeVoidAsync("destroyBuffer", jsHandle);
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        if (_module == null) return;
        var jsHandle = GetJsHandle((int)buffer, _pendingUniformBuffers);
        _module.InvokeVoidAsync("updateUniformBuffer", jsHandle, offsetBytes, data.ToArray());
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        var jsHandle = GetJsHandle((int)buffer, _pendingUniformBuffers);
        WebGLInterop.BindUniformBuffer((int)jsHandle, slot);
    }

    // === Texture Management ===

    public nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear, string? name = null)
    {
        if (_module == null) return 0;
        var textureId = _nextTextureId++;
        var task = _module.InvokeAsync<uint>("createTexture", width, height, data.ToArray(), (int)format, (int)filter).AsTask();
        _pendingTextures[textureId] = task;

        _textures[textureId] = new TextureInfo
        {
            JsHandle = 0, // Will be resolved later
            Width = width,
            Height = height,
            Layers = 0,
            IsArray = false
        };
        return (nuint)textureId;
    }

    public void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data)
    {
        ref var info = ref _textures[(int)handle];

        // Resolve JS handle if needed
        if (info.JsHandle == 0 && _pendingTextures.TryGetValue((int)handle, out var task) && task.IsCompleted)
        {
            info.JsHandle = task.Result;
            _pendingTextures.Remove((int)handle);
        }

        if (info.JsHandle == 0) return;
        _module?.InvokeVoidAsync("updateTexture", info.JsHandle, width, height, data.ToArray());
    }

    public void DestroyTexture(nuint handle)
    {
        ref var info = ref _textures[(int)handle];

        // Resolve JS handle if needed
        if (info.JsHandle == 0 && _pendingTextures.TryGetValue((int)handle, out var task) && task.IsCompleted)
        {
            info.JsHandle = task.Result;
            _pendingTextures.Remove((int)handle);
        }

        if (info.JsHandle != 0)
        {
            _module?.InvokeVoidAsync("destroyTexture", info.JsHandle);
            info = default;
        }
    }

    public void BindTexture(nuint handle, int slot)
    {
        ref var info = ref _textures[(int)handle];

        // Resolve JS handle if needed
        if (info.JsHandle == 0 && _pendingTextures.TryGetValue((int)handle, out var task) && task.IsCompleted)
        {
            info.JsHandle = task.Result;
            _pendingTextures.Remove((int)handle);
        }

        if (info.JsHandle == 0) return;

        if (info.IsArray)
            WebGLInterop.BindTextureArray(slot, (int)info.JsHandle);
        else
            WebGLInterop.BindTexture(slot, (int)info.JsHandle);
    }

    // === Texture Array Management ===

    public nuint CreateTextureArray(int width, int height, int layers)
    {
        if (_module == null) return 0;
        var textureId = _nextTextureId++;
        var task = _module.InvokeAsync<uint>("createTextureArray", width, height, layers).AsTask();
        _pendingTextures[textureId] = task;

        _textures[textureId] = new TextureInfo
        {
            JsHandle = 0, // Will be resolved later
            Width = width,
            Height = height,
            Layers = layers,
            IsArray = true
        };
        return (nuint)textureId;
    }

    public nuint CreateTextureArray(int width, int height, byte[][] layerData, TextureFormat format, TextureFilter filter, string? name = null)
    {
        if (_module == null) return 0;
        var layers = layerData.Length;
        var handle = CreateTextureArray(width, height, layers);
        for (var i = 0; i < layers; i++)
            UpdateTextureLayer(handle, i, layerData[i]);
        return handle;
    }

    public void UpdateTextureLayer(nuint handle, int layer, ReadOnlySpan<byte> data)
    {
        ref var info = ref _textures[(int)handle];

        // Resolve JS handle if needed
        if (info.JsHandle == 0 && _pendingTextures.TryGetValue((int)handle, out var task) && task.IsCompleted)
        {
            info.JsHandle = task.Result;
            _pendingTextures.Remove((int)handle);
        }

        if (info.JsHandle == 0 || !info.IsArray) return;
        _module?.InvokeVoidAsync("updateTextureArrayLayer", info.JsHandle, layer, data.ToArray());
    }

    // === Shader Management ===

    public nuint CreateShader(string name, string vertexSource, string fragmentSource, List<ShaderBinding> bindings)
    {
        if (_module == null) return 0;
        var shaderId = _nextShaderId++;
        var task = _module.InvokeAsync<uint>("createShader", name, vertexSource, fragmentSource).AsTask();
        _pendingShaders[shaderId] = task;
        return (nuint)shaderId;
    }

    public void DestroyShader(nuint handle)
    {
        var jsHandle = GetJsHandle((int)handle, _pendingShaders);
        _module?.InvokeVoidAsync("destroyShader", jsHandle);
    }

    public void BindShader(nuint handle)
    {
        var jsHandle = GetJsHandle((int)handle, _pendingShaders);
        WebGLInterop.BindShader((int)jsHandle);
    }

    // === State Management ===

    public void SetBlendMode(BlendMode mode)
    {
        WebGLInterop.SetBlendMode((int)mode);
    }

    // === Drawing ===

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        WebGLInterop.DrawElements(firstIndex, indexCount, baseVertex);
    }

    // === Synchronization ===

    public nuint CreateFence()
    {
        if (_module == null) return 0;
        var fenceId = _nextFenceId++;
        var task = _module.InvokeAsync<uint>("createFence").AsTask();
        _pendingFences[fenceId] = task;
        return (nuint)fenceId;
    }

    public void WaitFence(nuint fence)
    {
        var jsHandle = GetJsHandle((int)fence, _pendingFences);
        _module?.InvokeVoidAsync("waitFence", jsHandle);
    }

    public void DeleteFence(nuint fence)
    {
        var jsHandle = GetJsHandle((int)fence, _pendingFences);
        _module?.InvokeVoidAsync("deleteFence", jsHandle);
    }

    // === Render Passes ===

    public void ResizeOffscreenTarget(int width, int height, int msaaSamples)
    {
        // TODO: Implement offscreen rendering for WebGL
    }

    public void BeginScenePass(Color clearColor)
    {
        Clear(clearColor);
    }

    public void EndScenePass()
    {
        // TODO: Implement MSAA resolve for WebGL
    }

    public void Composite(nuint compositeShader)
    {
        // TODO: Implement composite for WebGL
    }
}

internal static partial class WebGLInterop
{
    [JSImport("beginFrame", "noz-webgl")]
    internal static partial void BeginFrame();

    [JSImport("endFrame", "noz-webgl")]
    internal static partial void EndFrame();

    [JSImport("clear", "noz-webgl")]
    internal static partial void Clear(float r, float g, float b, float a);

    [JSImport("setViewport", "noz-webgl")]
    internal static partial void SetViewport(int x, int y, int width, int height);

    [JSImport("setScissor", "noz-webgl")]
    internal static partial void SetScissor(int x, int y, int width, int height);

    [JSImport("disableScissor", "noz-webgl")]
    internal static partial void DisableScissor();

    [JSImport("bindMesh", "noz-webgl")]
    internal static partial void BindMesh(int meshHandle);

    [JSImport("bindShader", "noz-webgl")]
    internal static partial void BindShader(int shaderHandle);

    [JSImport("bindTexture", "noz-webgl")]
    internal static partial void BindTexture(int slot, int textureHandle);

    [JSImport("bindTextureArray", "noz-webgl")]
    internal static partial void BindTextureArray(int slot, int textureHandle);

    [JSImport("setBlendMode", "noz-webgl")]
    internal static partial void SetBlendMode(int mode);

    [JSImport("drawElements", "noz-webgl")]
    internal static partial void DrawElements(int firstIndex, int indexCount, int baseVertex);

    [JSImport("bindUniformBuffer", "noz-webgl")]
    internal static partial void BindUniformBuffer(int bufferHandle, int slot);
}
