//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static class PostProcess
{    
    private static RenderTexture? _sceneRT;
    private static RenderTexture? _currentRT;
    private static bool _active;
    private static int _width;
    private static int _height;
    private static readonly Camera _camera = new();

    // Bloom shaders
    private static Shader? _downsampleShader;
    private static Shader? _upsampleShader;
    private static Shader? _compositeShader;

    // Mip chain storage for bloom (max 8 levels = 1/256 res)
    private const int MaxMipLevels = 8;
    private static readonly RenderTexture[] _mips = new RenderTexture[MaxMipLevels];

    internal static bool IsActive => _active;

    internal static void ForceReset()
    {
        _sceneRT = null;
        _currentRT = null;
        _active = false;
    }

    internal static void SetSceneRT(RenderTexture rt)
    {
        _sceneRT = rt;
        _currentRT = rt;
        _width = rt.Width;
        _height = rt.Height;
        _active = false;
    }

    internal static RenderTexture? TakeResult()
    {
        var result = _currentRT;
        _active = false;
        _sceneRT = null;
        _currentRT = null;
        return result;
    }

    // Shorthand for BeginBlit + EndBlit when no extra state is needed.
    public static void Blit(Shader shader) { BeginBlit(shader, _width, _height); EndBlit(); }
    public static void Blit(Shader shader, int width, int height) { BeginBlit(shader, width, height); EndBlit(); }

    public static void BeginBlit(Shader shader) => BeginBlit(shader, _width, _height);

    public static void BeginBlit(Shader shader, int width, int height)
    {
        if (_sceneRT == null) return;
        if (_currentRT == null) return;

        Graphics.EndPass();

        var dest = RenderTexturePool.Acquire(width, height);

        Graphics.BeginPass(dest, Color.Transparent);
        UpdateCamera(width, height);
        Graphics.SetShader(shader);
        Graphics.SetBlendMode(BlendMode.None);
        Graphics.SetTexture(_currentRT.Handle, slot: 0);
        Graphics.SetTextureFilter(TextureFilter.Linear, slot: 0);
        Graphics.SetTransform(Matrix3x2.Identity);

        _currentRT = dest;
        _active = true;
    }

    public static void EndBlit()
    {
        if (!_active) return;
        if (_currentRT == null) return;
        Graphics.Draw(0, 0, _currentRT.Width, _currentRT.Height);
    }

    public static void Bloom(float threshold = 0.8f, float intensity = 1.2f, int mipLevels = 5)
    {
        if (_sceneRT == null || Application.IsResizing) return;
        if (_currentRT == null) return;    

        _downsampleShader ??= Asset.Load(AssetType.Shader, "pp_downsample") as Shader;
        _upsampleShader ??= Asset.Load(AssetType.Shader, "pp_upsample") as Shader;
        _compositeShader ??= Asset.Load(AssetType.Shader, "pp_composite") as Shader;

        if (_downsampleShader == null || _upsampleShader == null || _compositeShader == null)
        {
            Log.Error("PostProcess.Bloom: missing shaders");
            return;
        }

        mipLevels = Math.Clamp(mipLevels, 1, MaxMipLevels);
        var original = _currentRT;
        int w = _width, h = _height;

        // Downsample chain
        // Pass texel size and threshold via vertex color (per-draw, survives deferred)
        // color.r = threshold (0 = no threshold), color.g = texel_w, color.b = texel_h
        for (int i = 0; i < mipLevels; i++)
        {
            w = Math.Max(w / 2, 1);
            h = Math.Max(h / 2, 1);

            float srcTexelW = 1f / _currentRT.Width;
            float srcTexelH = 1f / _currentRT.Height;
            BeginBlit(_downsampleShader, w, h);
            Graphics.SetColor(new Color(
                i == 0 ? threshold : 0f,
                srcTexelW,
                srcTexelH));
            EndBlit();

            _mips[i] = _currentRT;
        }

        // Upsample chain (from smallest mip back up)
        // color.g/b = source (smaller mip) texel size for tent filter
        for (int i = mipLevels - 2; i >= 0; i--)
        {
            float srcTexelW = 1f / _currentRT.Width;
            float srcTexelH = 1f / _currentRT.Height;
            BeginBlit(_upsampleShader, _mips[i].Width, _mips[i].Height);
            Graphics.SetColor(new Color(0f, srcTexelW, srcTexelH));
            Graphics.SetTexture(_mips[i].Handle, slot: 2);
            Graphics.SetTextureFilter(TextureFilter.Linear, slot: 2);
            EndBlit();
        }

        // Composite onto original
        Graphics.SetUniform("composite_params", intensity);
        BeginBlit(_compositeShader);
        Graphics.SetTexture(original.Handle, slot: 2);
        Graphics.SetTextureFilter(TextureFilter.Linear, slot: 2);
        EndBlit();
    }

    private static void UpdateCamera(int width, int height)
    {
        _camera.SetExtents(new Rect(0, 0, width, height));
        _camera.Position = Vector2.Zero;
        _camera.Update(new Vector2Int(width, height));
        Graphics.SetCamera(_camera);
    }
}
