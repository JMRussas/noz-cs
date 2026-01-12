//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public static class Render
{
    private static RenderConfig _config = null!;
    private static IRender _backend = null!;
    private static MeshBatcher _batcher = null!;
    private static Matrix4x4 _projectionMatrix;

    public static IRender Backend => _backend;
    public static MeshBatcher Batcher => _batcher;

    // Stats
    public static int DrawCallCount => _batcher?.DrawCallCount ?? 0;
    public static int VertexCount => _batcher?.VertexCount ?? 0;
    public static int CommandCount => _batcher?.CommandCount ?? 0;

    public static void Init(RenderConfig? config, IRender backend)
    {
        _config = config ?? new RenderConfig();
        _backend = backend;

        _backend.Init(new RenderBackendConfig
        {
            VSync = _config.Vsync,
            MaxCommands = _config.MaxCommands
        });

        // Initialize batcher
        _batcher = new MeshBatcher();
        _batcher.Init(_backend);
    }

    public static void Shutdown()
    {
        _batcher?.Shutdown();
        _backend.Shutdown();
    }

    public static void BeginFrame()
    {
        _backend.BeginFrame();
        _batcher.BeginBatch();

        // Update projection matrix for current window size
        var size = Application.WindowSize;
        _projectionMatrix = Matrix4x4.CreateOrthographicOffCenter(
            0, size.X, size.Y, 0, -1000, 1000
        );

        // Set projection on sprite shader
        _backend.BindShader(ShaderHandle.Sprite);
        _backend.SetUniformMatrix4x4("uProjection", _projectionMatrix);
    }

    public static void EndFrame()
    {
        _batcher.BuildBatches();
        _batcher.FlushBatches();
        _backend.EndFrame();
    }

    public static void Clear(Color color)
    {
        _backend.Clear(color);
    }

    public static void SetViewport(int x, int y, int width, int height)
    {
        _backend.SetViewport(x, y, width, height);
    }

    /// <summary>
    /// Draw a colored quad (no texture).
    /// </summary>
    public static void DrawQuad(float x, float y, float width, float height, Color32 color,
        byte layer = 128, ushort depth = 0, BlendMode blend = BlendMode.Alpha)
    {
        _batcher.SubmitQuad(
            x, y, width, height,
            0, 0, 1, 1,  // UV doesn't matter for white texture
            Matrix3x2.Identity,
            TextureHandle.White,
            blend,
            layer,
            depth,
            color
        );
    }

    /// <summary>
    /// Draw a colored quad with transform.
    /// </summary>
    public static void DrawQuad(float x, float y, float width, float height, in Matrix3x2 transform,
        Color32 color, byte layer = 128, ushort depth = 0, BlendMode blend = BlendMode.Alpha)
    {
        _batcher.SubmitQuad(
            x, y, width, height,
            0, 0, 1, 1,
            transform,
            TextureHandle.White,
            blend,
            layer,
            depth,
            color
        );
    }

    /// <summary>
    /// Draw a textured quad.
    /// </summary>
    public static void DrawQuad(float x, float y, float width, float height,
        float u0, float v0, float u1, float v1,
        TextureHandle texture, Color32 tint,
        byte layer = 128, ushort depth = 0, BlendMode blend = BlendMode.Alpha)
    {
        _batcher.SubmitQuad(
            x, y, width, height,
            u0, v0, u1, v1,
            Matrix3x2.Identity,
            texture,
            blend,
            layer,
            depth,
            tint
        );
    }

    /// <summary>
    /// Draw a textured quad with transform.
    /// </summary>
    public static void DrawQuad(float x, float y, float width, float height,
        float u0, float v0, float u1, float v1,
        in Matrix3x2 transform, TextureHandle texture, Color32 tint,
        byte layer = 128, ushort depth = 0, BlendMode blend = BlendMode.Alpha)
    {
        _batcher.SubmitQuad(
            x, y, width, height,
            u0, v0, u1, v1,
            transform,
            texture,
            blend,
            layer,
            depth,
            tint
        );
    }

    /// <summary>
    /// Begin a sort group for layered rendering.
    /// </summary>
    public static void BeginSortGroup(ushort groupDepth)
    {
        _batcher.BeginSortGroup(groupDepth);
    }

    /// <summary>
    /// End the current sort group.
    /// </summary>
    public static void EndSortGroup()
    {
        _batcher.EndSortGroup();
    }

    // Sprite rendering (to be implemented when Sprite is extended)
    public static void Draw(Sprite sprite)
    {
        // TODO: Get sprite texture, rect, and UV and submit to batcher
    }

    public static void Draw(Sprite sprite, in Matrix3x2 transform)
    {
        // TODO: Get sprite texture, rect, and UV and submit to batcher with transform
    }
}
