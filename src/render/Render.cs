//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using noz.Platform;

namespace noz;

public static class Render
{
    public static RenderConfig Config { get; private set; } = null!;
    public static IRender Backend { get; private set; } = null!;
    public static MeshBatcher Batcher { get; private set; } = null!;
    public static Camera? Camera { get; private set; }

    public static ref readonly RenderStats Stats => ref Batcher.Stats;  
    
    public static void Init(RenderConfig? config, IRender backend)
    {
        Config = config ?? new RenderConfig();
        Backend = backend;

        Backend.Init(new RenderBackendConfig
        {
            VSync = Config.Vsync
        });

        Batcher = new MeshBatcher(Config);
        Batcher.Init(Backend);
        Camera = new Camera();
    }

    public static void Shutdown()
    {
        Batcher?.Shutdown();
        Backend.Shutdown();
    }

    /// <summary>
    /// Bind a camera for rendering. Pass null to use the default screen-space camera.
    /// </summary>
    public static void BindCamera(Camera? camera)
    {
        Camera = camera;
        if (camera == null) return;

        var viewport = camera.Viewport;
        if (viewport is { Width: > 0, Height: > 0 })
            Backend.SetViewport((int)viewport.X, (int)viewport.Y, (int)viewport.Width, (int)viewport.Height);

        // Convert camera's 3x2 view matrix to 4x4 for the shader
        // Translation goes in column 4 (M14, M24) so after transpose it's in the right place
        var view = camera.ViewMatrix;
        var projection = new Matrix4x4(
            view.M11, view.M12, 0, view.M31,
            view.M21, view.M22, 0, view.M32,
            0, 0, 1, 0,
            0, 0, 0, 1
        );

        Backend.BindShader(ShaderHandle.Sprite);
        Backend.SetUniformMatrix4x4("uProjection", projection);
    }

    public static void BeginFrame()
    {
        Backend.BeginFrame();
        Batcher.BeginBatch();

        // Update and apply default camera
        var size = Application.WindowSize;
        Backend.SetViewport(0, 0, (int)size.X, (int)size.Y);
    }

    public static void EndFrame()
    {
        Batcher.BuildBatches();
        Batcher.FlushBatches();
        Backend.EndFrame();
    }

    /// <summary>
    /// Flush all pending draw commands immediately. Call this before changing cameras
    /// to ensure previous draws use the correct projection.
    /// </summary>
    public static void Flush()
    {
        Batcher.BuildBatches();
        Batcher.FlushBatches();
        Batcher.BeginBatch();
    }

    public static void Clear(Color color)
    {
        Backend.Clear(color);
    }

    public static void SetViewport(int x, int y, int width, int height)
    {
        Backend.SetViewport(x, y, width, height);
    }

    /// <summary>
    /// Draw a colored quad (no texture).
    /// </summary>
    public static void DrawQuad(
        float x,
        float y,
        float width,
        float height,
        Color32 color,
        byte layer = 128,
        ushort depth = 0,
        BlendMode blend = BlendMode.Alpha)
    {
        Batcher.SubmitQuad(
            x, y, width, height,
            0, 0, 1, 1, // UV doesn't matter for white texture
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
    public static void DrawQuad(
        float x,
        float y,
        float width,
        float height,
        in Matrix3x2 transform,
        Color32 color,
        byte layer = 128,
        ushort depth = 0,
        BlendMode blend = BlendMode.Alpha)
    {
        Batcher.SubmitQuad(
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
    public static void DrawQuad(
        float x,
        float y,
        float width,
        float height,
        float u0,
        float v0,
        float u1,
        float v1,
        TextureHandle texture,
        Color32 tint,
        byte layer = 128,
        ushort depth = 0,
        BlendMode blend = BlendMode.Alpha)
    {
        Batcher.SubmitQuad(
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
    public static void DrawQuad(
        float x,
        float y,
        float width,
        float height,
        float u0,
        float v0,
        float u1,
        float v1,
        in Matrix3x2 transform,
        TextureHandle texture,
        Color32 tint,
        byte layer = 128,
        ushort depth = 0,
        BlendMode blend = BlendMode.Alpha)
    {
        Batcher.SubmitQuad(
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
        Batcher.BeginSortGroup(groupDepth);
    }

    /// <summary>
    /// End the current sort group.
    /// </summary>
    public static void EndSortGroup()
    {
        Batcher.EndSortGroup();
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