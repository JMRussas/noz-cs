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
    private static bool _initialized;

    public static void Init(UIConfig config)
    {
        if (_initialized) return;

        _shader = Asset.Get<Shader>(AssetType.Shader, config.UIShader);
        _vertices = new NativeArray<UIVertex>(MaxUIVertices);
        _indices = new NativeArray<ushort>(MaxUIIndices);

        _mesh = Graphics.Driver.CreateMesh<UIVertex>(
            MaxUIVertices,
            MaxUIIndices,
            BufferUsage.Dynamic,
            "UIRender"
        );

        _initialized = true;
    }

    public static void Shutdown()
    {
        if (!_initialized) return;

        _vertices.Dispose();
        _indices.Dispose();

        Graphics.Driver.DestroyMesh(_mesh);
        _initialized = false;
    }

    public static void DrawRect(
        in Rect rect,
        Color color,
        float borderRadius = 0,
        float borderWidth = 0,
        Color borderColor = default)
    {
        DrawRect(rect, color, BorderRadius.Circular(borderRadius), borderWidth, borderColor);
    }

    public static void DrawRect(
        in Rect rect,
        Color color,
        BorderRadius borderRadius,
        float borderWidth = 0,
        Color borderColor = default)
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

        Graphics.SetShader(_shader);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(6, indexOffset);
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

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void AddSimpleQuad(float x0, float y0, float x1, float y1, Color c)
    {
        if (x1 <= x0 || y1 <= y0) return;
        var vo = _vertices.Length;
        // Simple quad with no rounding - uses the same shader but with zero radii
        var size = new Vector2(x1 - x0, y1 - y0);
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y0), UV = new Vector2(0, 0), Normal = size, Color = c, BorderRatio = 0, BorderColor = default, CornerRadii = Vector4.Zero });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y0), UV = new Vector2(1, 0), Normal = size, Color = c, BorderRatio = 0, BorderColor = default, CornerRadii = Vector4.Zero });
        _vertices.Add(new UIVertex { Position = new Vector2(x1, y1), UV = new Vector2(1, 1), Normal = size, Color = c, BorderRatio = 0, BorderColor = default, CornerRadii = Vector4.Zero });
        _vertices.Add(new UIVertex { Position = new Vector2(x0, y1), UV = new Vector2(0, 1), Normal = size, Color = c, BorderRatio = 0, BorderColor = default, CornerRadii = Vector4.Zero });
        AddQuadIndices(vo);
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
