//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;
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
        _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/noz/noz-webgl.js");
        await _module.InvokeVoidAsync("init");
    }

    public void Shutdown()
    {
        _module?.InvokeVoidAsync("shutdown");
    }

    public void BeginFrame()
    {
        _module?.InvokeVoidAsync("beginFrame");
    }

    public void EndFrame()
    {
        _module?.InvokeVoidAsync("endFrame");
    }

    public void Clear(Color color)
    {
        _module?.InvokeVoidAsync("clear", color.R, color.G, color.B, color.A);
    }

    public void SetViewport(int x, int y, int width, int height)
    {
        _module?.InvokeVoidAsync("setViewport", x, y, width, height);
    }

    public void SetScissor(int x, int y, int width, int height)
    {
        _module?.InvokeVoidAsync("setScissor", x, y, width, height);
    }

    public void DisableScissor()
    {
        _module?.InvokeVoidAsync("disableScissor");
    }

    // === Mesh Management ===

    public nuint CreateMesh<T>(int maxVertices, int maxIndices, BufferUsage usage, string name = "") where T : IVertex
    {
        if (_module == null) return 0;
        var descriptor = T.GetFormatDescriptor();
        var id = _module.InvokeAsync<uint>("createMesh", maxVertices, maxIndices, descriptor.Stride, (int)usage).AsTask().Result;
        return (nuint)id;
    }

    public void DestroyMesh(nuint handle)
    {
        _module?.InvokeVoidAsync("destroyMesh", (uint)handle);
    }

    public void BindMesh(nuint handle)
    {
        _module?.InvokeVoidAsync("bindMesh", (uint)handle);
    }

    public void UpdateMesh(nuint handle, ReadOnlySpan<byte> vertexData, ReadOnlySpan<ushort> indexData)
    {
        if (_module == null) return;
        _module.InvokeVoidAsync("updateMesh", (uint)handle, vertexData.ToArray(), MemoryMarshal.AsBytes(indexData).ToArray());
    }

    // === Uniform Buffer Management ===

    public nuint CreateUniformBuffer(int sizeInBytes, BufferUsage usage, string name = "")
    {
        if (_module == null) return 0;
        var id = _module.InvokeAsync<uint>("createUniformBuffer", sizeInBytes, (int)usage).AsTask().Result;
        return (nuint)id;
    }

    public void DestroyBuffer(nuint handle)
    {
        _module?.InvokeVoidAsync("destroyBuffer", (uint)handle);
    }

    public void UpdateUniformBuffer(nuint buffer, int offsetBytes, ReadOnlySpan<byte> data)
    {
        if (_module == null) return;
        _module.InvokeVoidAsync("updateUniformBuffer", (uint)buffer, offsetBytes, data.ToArray());
    }

    public void BindUniformBuffer(nuint buffer, int slot)
    {
        _module?.InvokeVoidAsync("bindUniformBuffer", (uint)buffer, slot);
    }

    // === Texture Management ===

    public nuint CreateTexture(int width, int height, ReadOnlySpan<byte> data, TextureFormat format = TextureFormat.RGBA8, TextureFilter filter = TextureFilter.Linear)
    {
        if (_module == null) return 0;
        var jsHandle = _module.InvokeAsync<uint>("createTexture", width, height, data.ToArray(), (int)format, (int)filter).AsTask().Result;

        var handle = _nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsHandle = jsHandle,
            Width = width,
            Height = height,
            Layers = 0,
            IsArray = false
        };
        return (nuint)handle;
    }

    public void UpdateTexture(nuint handle, int width, int height, ReadOnlySpan<byte> data)
    {
        ref var info = ref _textures[(int)handle];
        if (info.JsHandle == 0) return;
        _module?.InvokeVoidAsync("updateTexture", info.JsHandle, width, height, data.ToArray());
    }

    public void DestroyTexture(nuint handle)
    {
        ref var info = ref _textures[(int)handle];
        if (info.JsHandle != 0)
        {
            _module?.InvokeVoidAsync("destroyTexture", info.JsHandle);
            info = default;
        }
    }

    public void BindTexture(nuint handle, int slot)
    {
        ref var info = ref _textures[(int)handle];
        if (info.JsHandle == 0) return;

        if (info.IsArray)
            _module?.InvokeVoidAsync("bindTextureArray", slot, info.JsHandle);
        else
            _module?.InvokeVoidAsync("bindTexture", slot, info.JsHandle);
    }

    // === Texture Array Management ===

    public nuint CreateTextureArray(int width, int height, int layers)
    {
        if (_module == null) return 0;
        var jsHandle = _module.InvokeAsync<uint>("createTextureArray", width, height, layers).AsTask().Result;

        var handle = _nextTextureId++;
        _textures[handle] = new TextureInfo
        {
            JsHandle = jsHandle,
            Width = width,
            Height = height,
            Layers = layers,
            IsArray = true
        };
        return (nuint)handle;
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
        if (info.JsHandle == 0 || !info.IsArray) return;
        _module?.InvokeVoidAsync("updateTextureArrayLayer", info.JsHandle, layer, data.ToArray());
    }

    // === Shader Management ===

    public nuint CreateShader(string name, string vertexSource, string fragmentSource)
    {
        if (_module == null) return 0;
        var id = _module.InvokeAsync<uint>("createShader", name, vertexSource, fragmentSource).AsTask().Result;
        return (nuint)id;
    }

    public void DestroyShader(nuint handle)
    {
        _module?.InvokeVoidAsync("destroyShader", (uint)handle);
    }

    public void BindShader(nuint handle)
    {
        _module?.InvokeVoidAsync("bindShader", (uint)handle);
    }

    public void SetUniformMatrix4x4(string name, in Matrix4x4 value)
    {
        if (_module == null) return;

        float[] data =
        [
            value.M11, value.M21, value.M31, value.M41,
            value.M12, value.M22, value.M32, value.M42,
            value.M13, value.M23, value.M33, value.M43,
            value.M14, value.M24, value.M34, value.M44
        ];
        _module.InvokeVoidAsync("setUniformMatrix4x4", name, data);
    }

    public void SetUniformInt(string name, int value)
    {
        _module?.InvokeVoidAsync("setUniformInt", name, value);
    }

    public void SetUniformFloat(string name, float value)
    {
        _module?.InvokeVoidAsync("setUniformFloat", name, value);
    }

    public void SetUniformVec2(string name, Vector2 value)
    {
        _module?.InvokeVoidAsync("setUniformVec2", name, value.X, value.Y);
    }

    public void SetUniformVec4(string name, Vector4 value)
    {
        _module?.InvokeVoidAsync("setUniformVec4", name, value.X, value.Y, value.Z, value.W);
    }

    // === State Management ===

    public void SetBlendMode(BlendMode mode)
    {
        _module?.InvokeVoidAsync("setBlendMode", (int)mode);
    }

    // === Drawing ===

    public void DrawElements(int firstIndex, int indexCount, int baseVertex = 0)
    {
        _module?.InvokeVoidAsync("drawElements", firstIndex, indexCount, baseVertex);
    }

    // === Synchronization ===

    public nuint CreateFence()
    {
        if (_module == null) return 0;
        var id = _module.InvokeAsync<uint>("createFence").AsTask().Result;
        return (nuint)id;
    }

    public void WaitFence(nuint fence)
    {
        _module?.InvokeVoidAsync("waitFence", (uint)fence);
    }

    public void DeleteFence(nuint fence)
    {
        _module?.InvokeVoidAsync("deleteFence", (uint)fence);
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
