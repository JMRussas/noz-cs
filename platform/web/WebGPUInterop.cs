//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices.JavaScript;

namespace NoZ.Platform.Web;

/// <summary>
/// JSImport declarations for the browser WebGPU bridge (noz-webgpu.js)
/// </summary>
public static partial class WebGPUInterop
{
    // Module name must match the name passed to JSHost.ImportAsync() in WebGraphicsDriver
    private const string ModuleName = "noz-webgpu";

    // ============================================================================
    // Initialization
    // ============================================================================

    [JSImport("init", ModuleName)]
    internal static partial Task<JSObject> InitAsync(string canvasSelector);

    [JSImport("shutdown", ModuleName)]
    internal static partial void Shutdown();

    [JSImport("getSurfaceSize", ModuleName)]
    internal static partial JSObject GetSurfaceSize();

    [JSImport("checkResize", ModuleName)]
    internal static partial bool CheckResize();

    // ============================================================================
    // Buffer Management
    // ============================================================================

    [JSImport("createBuffer", ModuleName)]
    internal static partial int CreateBuffer(int size, int usage, string? label);

    [JSImport("writeBuffer", ModuleName)]
    internal static partial void WriteBuffer(int bufferId, int offset, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> data);

    [JSImport("destroyBuffer", ModuleName)]
    internal static partial void DestroyBuffer(int bufferId);

    // ============================================================================
    // Mesh Management
    // ============================================================================

    [JSImport("createMesh", ModuleName)]
    internal static partial int CreateMesh(int maxVertices, int maxIndices, int vertexStride, string? label);

    [JSImport("updateMesh", ModuleName)]
    internal static partial void UpdateMesh(int meshId, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> vertexData, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> indexData);

    [JSImport("destroyMesh", ModuleName)]
    internal static partial void DestroyMesh(int meshId);

    // ============================================================================
    // Texture Management
    // ============================================================================

    [JSImport("createTexture", ModuleName)]
    internal static partial int CreateTexture(int width, int height, string format, int usage, string? label);

    [JSImport("createTextureArray", ModuleName)]
    internal static partial int CreateTextureArray(int width, int height, int layers, string format, string? label);

    [JSImport("writeTexture", ModuleName)]
    internal static partial void WriteTexture(int textureId, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> data, int width, int height, int bytesPerRow, int layer);

    [JSImport("writeTextureRegion", ModuleName)]
    internal static partial void WriteTextureRegion(int textureId, [JSMarshalAs<JSType.MemoryView>] ArraySegment<byte> data, int x, int y, int width, int height, int bytesPerRow);

    [JSImport("destroyTexture", ModuleName)]
    internal static partial void DestroyTexture(int textureId);

    // ============================================================================
    // Shader Management
    // ============================================================================

    [JSImport("createShaderModule", ModuleName)]
    internal static partial int CreateShaderModule(string code, string? label);

    [JSImport("destroyShaderModule", ModuleName)]
    internal static partial void DestroyShaderModule(int shaderId);

    // ============================================================================
    // Pipeline Management
    // ============================================================================

    [JSImport("createBindGroupLayout", ModuleName)]
    internal static partial int CreateBindGroupLayout([JSMarshalAs<JSType.Array<JSType.Object>>] JSObject[] entries, string? label);

    [JSImport("createPipelineLayout", ModuleName)]
    internal static partial int CreatePipelineLayout([JSMarshalAs<JSType.Array<JSType.Number>>] int[] bindGroupLayoutIds, string? label);

    [JSImport("createRenderPipeline", ModuleName)]
    internal static partial int CreateRenderPipeline(JSObject descriptor);

    [JSImport("destroyRenderPipeline", ModuleName)]
    internal static partial void DestroyRenderPipeline(int pipelineId);

    // ============================================================================
    // Bind Group Management
    // ============================================================================

    [JSImport("createBindGroup", ModuleName)]
    internal static partial int CreateBindGroup(int layoutId, [JSMarshalAs<JSType.Array<JSType.Object>>] JSObject[] entries, string? label);

    // Alternative that takes JSON string to avoid JSObject marshalling issues
    [JSImport("createBindGroupFromJson", ModuleName)]
    internal static partial int CreateBindGroupFromJson(int layoutId, string entriesJson, string? label);

    [JSImport("destroyBindGroup", ModuleName)]
    internal static partial void DestroyBindGroup(int bindGroupId);

    // ============================================================================
    // Frame Management
    // ============================================================================

    [JSImport("beginFrame", ModuleName)]
    internal static partial bool BeginFrame();

    [JSImport("endFrame", ModuleName)]
    internal static partial void EndFrame();

    // ============================================================================
    // Render Pass Management
    // ============================================================================

    [JSImport("beginRenderPass", ModuleName)]
    internal static partial bool BeginRenderPass([JSMarshalAs<JSType.Array<JSType.Object>>] JSObject[] colorAttachments, JSObject? depthAttachment, string? label);

    [JSImport("endRenderPass", ModuleName)]
    internal static partial void EndRenderPass();

    // ============================================================================
    // Render Commands
    // ============================================================================

    [JSImport("setViewport", ModuleName)]
    internal static partial void SetViewport(float x, float y, float width, float height, float minDepth, float maxDepth);

    [JSImport("setScissorRect", ModuleName)]
    internal static partial void SetScissorRect(int x, int y, int width, int height);

    [JSImport("setPipeline", ModuleName)]
    internal static partial void SetPipeline(int pipelineId);

    [JSImport("setBindGroup", ModuleName)]
    internal static partial void SetBindGroup(int index, int bindGroupId);

    [JSImport("setVertexBuffer", ModuleName)]
    internal static partial void SetVertexBuffer(int slot, int meshId);

    [JSImport("setIndexBuffer", ModuleName)]
    internal static partial void SetIndexBuffer(int meshId);

    [JSImport("drawIndexed", ModuleName)]
    internal static partial void DrawIndexed(int indexCount, int instanceCount, int firstIndex, int baseVertex, int firstInstance);

    [JSImport("draw", ModuleName)]
    internal static partial void Draw(int vertexCount, int instanceCount, int firstVertex, int firstInstance);

    // ============================================================================
    // Render Texture (for capturing to image)
    // ============================================================================

    [JSImport("createRenderTexture", ModuleName)]
    internal static partial int CreateRenderTexture(int width, int height, string format, string? label);

    [JSImport("destroyRenderTexture", ModuleName)]
    internal static partial void DestroyRenderTexture(int textureId);

    [JSImport("beginRenderTexturePass", ModuleName)]
    internal static partial void BeginRenderTexturePass(int textureId, float clearR, float clearG, float clearB, float clearA);

    [JSImport("endRenderTexturePass", ModuleName)]
    internal static partial void EndRenderTexturePass();

    [JSImport("readRenderTexturePixels", ModuleName)]
    internal static partial Task<string> ReadRenderTexturePixelsAsync(int textureId);

    // ============================================================================
    // Utility Functions
    // ============================================================================

    [JSImport("getPresentFormat", ModuleName)]
    internal static partial string GetPresentFormat();

    [JSImport("getBlendState", ModuleName)]
    internal static partial JSObject? GetBlendState(string blendMode);

    // ============================================================================
    // Object Creation Helpers (for C# interop - replaces eval-based creation)
    // ============================================================================

    // Bind Group Layout Entry Creators
    [JSImport("createUniformBufferLayoutEntry", ModuleName)]
    internal static partial JSObject CreateUniformBufferLayoutEntry(int binding);

    [JSImport("createTexture2DLayoutEntry", ModuleName)]
    internal static partial JSObject CreateTexture2DLayoutEntry(int binding);

    [JSImport("createTexture2DArrayLayoutEntry", ModuleName)]
    internal static partial JSObject CreateTexture2DArrayLayoutEntry(int binding);

    [JSImport("createUnfilterableTexture2DLayoutEntry", ModuleName)]
    internal static partial JSObject CreateUnfilterableTexture2DLayoutEntry(int binding);

    [JSImport("createSamplerLayoutEntry", ModuleName)]
    internal static partial JSObject CreateSamplerLayoutEntry(int binding);

    // Bind Group Entry Creators
    [JSImport("createBufferBindGroupEntry", ModuleName)]
    internal static partial JSObject CreateBufferBindGroupEntry(int binding, int bufferId, int offset, int size);

    [JSImport("createTextureBindGroupEntry", ModuleName)]
    internal static partial JSObject CreateTextureBindGroupEntry(int binding, int textureViewId);

    [JSImport("createSamplerBindGroupEntry", ModuleName)]
    internal static partial JSObject CreateSamplerBindGroupEntry(int binding, bool useLinearSampler);

    // Render Pipeline Descriptor Creator
    [JSImport("createRenderPipelineDescriptor", ModuleName)]
    internal static partial JSObject CreateRenderPipelineDescriptor(
        int vertexModuleId,
        int fragmentModuleId,
        int pipelineLayoutId,
        string vertexEntryPoint,
        string fragmentEntryPoint,
        string vertexBuffersJson,
        string blendMode,
        string topology,
        string cullMode,
        string frontFace,
        int sampleCount,
        string targetFormat,
        string label);

    // Color Attachment Creator
    [JSImport("createColorAttachment", ModuleName)]
    internal static partial JSObject CreateColorAttachment(
        int textureId,
        int resolveTextureId,
        string loadOp,
        string storeOp,
        float clearR,
        float clearG,
        float clearB,
        float clearA);
}
