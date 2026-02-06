//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using NoZ.Platform;

namespace NoZ;

public static class UIRender
{
    private const int MaxUIVertices = 16384;
    private const int MaxUIIndices = 32768;

    private static nuint _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader? _shader;
    private static Texture? _whiteTexture;
    private static bool _initialized;
    private static ushort _uiLayer;

    public static void Init(UIConfig config)
    {
        if (_initialized) return;

        _shader = Asset.Get<Shader>(AssetType.Shader, config.UIShader);
        _uiLayer = config.UILayer;
        _vertices = new NativeArray<UIVertex>(MaxUIVertices);
        _indices = new NativeArray<ushort>(MaxUIIndices);

        _mesh = Graphics.Driver.CreateMesh<UIVertex>(
            MaxUIVertices,
            MaxUIIndices,
            BufferUsage.Dynamic,
            "UIRender"
        );

        // Create 1x1 white texture for non-textured UI elements
        byte[] white = [255, 255, 255, 255];
        _whiteTexture = Texture.Create(1, 1, white, TextureFormat.RGBA8, TextureFilter.Point, "UIRender_White");

        _initialized = true;
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        _vertices.Dispose();
        _indices.Dispose();
        _whiteTexture?.Dispose();

        Graphics.Driver.DestroyMesh(_mesh);
        _initialized = false;
    }

    public static void DrawRect(
        in Rect rect,
        Color color,
        float borderRadius = 0,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        DrawRect(rect, color, BorderRadius.Circular(borderRadius), borderWidth, borderColor, order: order);
    }

    public static void DrawRect(
        in Rect rect,
        Color color,
        BorderRadius borderRadius,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        DrawTexturedRect(rect, _whiteTexture, color, borderRadius, borderWidth, borderColor, order: order);
    }

    public static void DrawImage(
        in Rect rect,
        Texture texture,
        Color color,
        BorderRadius borderRadius = default)
    {
        DrawTexturedRect(rect, texture, color, borderRadius, 0, default);
    }

    private static void DrawTexturedRect(
        in Rect rect,
        Texture? texture,
        Color color,
        BorderRadius borderRadius,
        float borderWidth,
        Color borderColor,
        ushort order = 0)
    {
        if (!_initialized || _shader == null) return;

        var vertexOffset = _vertices.Length;
        var indexOffset = _indices.Length;

        if (!_vertices.CheckCapacity(4) || !_indices.CheckCapacity(6))
            return;

        var x = rect.X;
        var y = rect.Y;
        var w = rect.Width;
        var h = rect.Height;

        // Clamp radii to half the rect size to avoid overlap
        var maxR = MathF.Min(w, h) / 2;
        var rTL = MathF.Min(borderRadius.TopLeft, maxR);
        var rTR = MathF.Min(borderRadius.TopRight, maxR);
        var rBL = MathF.Min(borderRadius.BottomLeft, maxR);
        var rBR = MathF.Min(borderRadius.BottomRight, maxR);

        var radii = new Vector4(rTL, rTR, rBL, rBR);
        var rectSize = new Vector2(w, h);

        // Simple 4-vertex quad - shader handles everything
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x, y),
            UV = new Vector2(0, 0),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x + w, y),
            UV = new Vector2(1, 0),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x + w, y + h),
            UV = new Vector2(1, 1),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = new Vector2(x, y + h),
            UV = new Vector2(0, 1),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });

        AddQuadIndices(vertexOffset);

        Graphics.SetLayer(_uiLayer);
        Graphics.SetShader(_shader);
        Graphics.SetTexture(texture ?? _whiteTexture, filter: texture?.Filter ?? TextureFilter.Point);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(6, indexOffset, order: order);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddQuadIndices(int baseVertex)
    {
        _indices.Add((ushort)baseVertex);
        _indices.Add((ushort)(baseVertex + 1));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 2));
        _indices.Add((ushort)(baseVertex + 3));
        _indices.Add((ushort)baseVertex);
    }

    public static void Flush()
    {
        if (_indices.Length == 0 || !_initialized || _shader == null)
            return;

        Graphics.Driver.BindMesh(_mesh);
        Graphics.Driver.UpdateMesh(_mesh, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }
}
