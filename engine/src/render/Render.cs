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

    private const int MaxBones = 64;
    private static Matrix3x2[] _boneTransforms = null!;
    private static float _time;
    private static ShaderHandle _currentShader;
    private static Shader? _compositeShader;
    private static bool _inUIPass;
    private static bool _inScenePass;

    public static ShaderHandle CurrentShader => _currentShader;
    public static Color ClearColor { get; set; } = Color.Black;  
    
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

        // Initialize bone transforms with identity at index 0
        _boneTransforms = new Matrix3x2[MaxBones];
        _boneTransforms[0] = Matrix3x2.Identity;
        for (var i = 1; i < MaxBones; i++)
            _boneTransforms[i] = Matrix3x2.Identity;
    }

    public static void Shutdown()
    {
        Batcher?.Shutdown();
        Backend.Shutdown();
    }

    /// <summary>
    /// Bind a shader for rendering. Call this before submitting draw commands.
    /// </summary>
    public static void BindShader(Shader shader)
    {
        _currentShader = shader.Handle;
        Backend.BindShader(shader.Handle);
    }

    /// <summary>
    /// Bind a camera for rendering. Pass null to use the default screen-space camera.
    /// Sets viewport and shader uniforms (projection, time, bones) on the currently bound shader.
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

        Backend.SetUniformMatrix4x4("uProjection", projection);
        Backend.SetUniformFloat("uTime", _time);
        Backend.SetBoneTransforms(_boneTransforms);
    }

    internal static void BeginFrame()
    {
        Backend.BeginFrame();

        // Update time for shader animation
        _time += Time.DeltaTime;

        // Ensure offscreen target matches window size
        var size = Application.WindowSize;
        Backend.ResizeOffscreenTarget((int)size.X, (int)size.Y, Config.MsaaSamples);

        _inUIPass = false;
        _inScenePass = true;

        Backend.BeginScenePass(ClearColor);
        Batcher.BeginBatch();
    }

    public static void BeginUI()
    {
        if (_inUIPass) return;

        Batcher.BuildBatches();
        Batcher.FlushBatches();

        if (_inScenePass)
        {
            Backend.EndScenePass();
            _inScenePass = false;

            if (_compositeShader != null)
                Backend.Composite(_compositeShader.Handle);
        }

        _inUIPass = true;
        Batcher.BeginBatch();
    }

    internal static void EndFrame()
    {
        Batcher.BuildBatches();
        Batcher.FlushBatches();

        if (_inScenePass)
        {
            Backend.EndScenePass();
            _inScenePass = false;

            if (_compositeShader != null)
                Backend.Composite(_compositeShader.Handle);
        }

        Backend.EndFrame();
    }

    internal static void ResolveAssets()
    {
        if (!string.IsNullOrEmpty(Config.CompositeShader))
        {
            _compositeShader = Asset.Get<Shader>(AssetType.Shader, Config.CompositeShader);
            if (_compositeShader == null)
                Log.Warning($"Composite shader '{Config.CompositeShader}' not found");
            else
                Log.Info($"Composite shader '{Config.CompositeShader}' resolved");
        }
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

    public static void SetBoneTransform(int index, in Matrix3x2 transform)
    {
        if (index > 0 && index < MaxBones)
            _boneTransforms[index] = transform;
    }

    public static void SetBoneTransforms(ReadOnlySpan<Matrix3x2> transforms, int startIndex = 1)
    {
        var count = Math.Min(transforms.Length, MaxBones - startIndex);
        for (var i = 0; i < count; i++)
            _boneTransforms[startIndex + i] = transforms[i];
    }

    public static void UploadBoneTransforms()
    {
        Backend.SetBoneTransforms(_boneTransforms);
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