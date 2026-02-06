//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices.JavaScript;
using System.Text.Json;
using NoZ.Platform;

namespace NoZ.Platform.Web;

/// <summary>
/// Helper methods for creating JSObjects for WebGPU interop
/// </summary>
internal static class JSObjectHelper
{
    // ============================================================================
    // Bind Group Layout Entries
    // ============================================================================

    public static JSObject CreateBindGroupLayoutEntry(ShaderBinding binding)
    {
        return binding.Type switch
        {
            ShaderBindingType.UniformBuffer => WebGPUInterop.CreateUniformBufferLayoutEntry((int)binding.Binding),
            ShaderBindingType.Texture2D => WebGPUInterop.CreateTexture2DLayoutEntry((int)binding.Binding),
            ShaderBindingType.Texture2DArray => WebGPUInterop.CreateTexture2DArrayLayoutEntry((int)binding.Binding),
            ShaderBindingType.Texture2DUnfilterable => WebGPUInterop.CreateUnfilterableTexture2DLayoutEntry((int)binding.Binding),
            ShaderBindingType.Sampler => WebGPUInterop.CreateSamplerLayoutEntry((int)binding.Binding),
            _ => throw new NotSupportedException($"Binding type {binding.Type} not supported")
        };
    }

    // ============================================================================
    // Bind Group Entries
    // ============================================================================

    public static JSObject CreateBindGroupEntry(uint binding, int bufferId, int bufferSize)
    {
        return WebGPUInterop.CreateBufferBindGroupEntry((int)binding, bufferId, 0, bufferSize);
    }

    public static JSObject CreateTextureBindGroupEntry(uint binding, int textureId)
    {
        return WebGPUInterop.CreateTextureBindGroupEntry((int)binding, textureId);
    }

    public static JSObject CreateSamplerBindGroupEntry(uint binding, bool useLinear)
    {
        return WebGPUInterop.CreateSamplerBindGroupEntry((int)binding, useLinear);
    }

    // ============================================================================
    // Render Pipeline Descriptor
    // ============================================================================

    public static JSObject CreateRenderPipelineDescriptor(
        int vertexModuleId,
        int fragmentModuleId,
        int pipelineLayoutId,
        VertexFormatDescriptor vertexDescriptor,
        BlendMode blendMode,
        int sampleCount,
        string targetFormat,
        string label)
    {
        var vertexBuffersJson = CreateVertexBufferLayoutJson(vertexDescriptor);
        var blendModeStr = GetBlendModeString(blendMode);

        return WebGPUInterop.CreateRenderPipelineDescriptor(
            vertexModuleId,
            fragmentModuleId,
            pipelineLayoutId,
            "vs_main",
            "fs_main",
            $"[{vertexBuffersJson}]",
            blendModeStr,
            "triangle-list",
            "none",
            "ccw",
            sampleCount,
            targetFormat,
            label);
    }

    private static string CreateVertexBufferLayoutJson(VertexFormatDescriptor descriptor)
    {
        var attributes = new List<string>();
        foreach (var attr in descriptor.Attributes)
        {
            var format = GetVertexFormatString(attr);
            attributes.Add($"{{\"shaderLocation\":{attr.Location},\"offset\":{attr.Offset},\"format\":\"{format}\"}}");
        }

        return $"{{\"arrayStride\":{descriptor.Stride},\"stepMode\":\"vertex\",\"attributes\":[{string.Join(",", attributes)}]}}";
    }

    private static string GetVertexFormatString(VertexAttribute attr)
    {
        return attr.Type switch
        {
            VertexAttribType.Float when attr.Components == 1 => "float32",
            VertexAttribType.Float when attr.Components == 2 => "float32x2",
            VertexAttribType.Float when attr.Components == 3 => "float32x3",
            VertexAttribType.Float when attr.Components == 4 => "float32x4",
            VertexAttribType.Int when attr.Components == 1 => "sint32",
            VertexAttribType.Int when attr.Components == 2 => "sint32x2",
            VertexAttribType.Int when attr.Components == 3 => "sint32x3",
            VertexAttribType.Int when attr.Components == 4 => "sint32x4",
            VertexAttribType.UByte when attr.Components == 4 && attr.Normalized => "unorm8x4",
            VertexAttribType.UByte when attr.Components == 4 => "uint8x4",
            _ => throw new NotSupportedException($"Vertex attribute type {attr.Type} with {attr.Components} components not supported")
        };
    }

    private static string GetBlendModeString(BlendMode mode)
    {
        return mode switch
        {
            BlendMode.None => "none",
            BlendMode.Alpha => "alpha",
            BlendMode.Additive => "additive",
            BlendMode.Multiply => "multiply",
            BlendMode.Premultiplied => "premultiplied",
            _ => "none"
        };
    }

    // ============================================================================
    // Color Attachment
    // ============================================================================

    public static JSObject CreateColorAttachment(int textureId, int resolveTextureId, string loadOp, string storeOp, Color clearColor)
    {
        return WebGPUInterop.CreateColorAttachment(
            textureId,
            resolveTextureId,
            loadOp,
            storeOp,
            clearColor.R,
            clearColor.G,
            clearColor.B,
            clearColor.A);
    }
}
