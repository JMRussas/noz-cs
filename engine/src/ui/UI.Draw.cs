//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

public static partial class UI
{
    private const int MaxUIVertices = 16384;
    private const int MaxUIIndices = 32768;
    private static RenderMesh _mesh;
    private static NativeArray<UIVertex> _vertices;
    private static NativeArray<ushort> _indices;
    private static Shader _shader = null!;
    private static float _drawOpacity = 1.0f;

    internal static RectInt? SceneViewport { get; set; }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    internal static Color ApplyOpacity(Color c) => c.WithAlpha(c.A * _drawOpacity);

    internal static void DrawTexturedRect(
        in Rect rect,
        in Matrix3x2 transform,
        Texture? texture,
        Color color,
        BorderRadius borderRadius = default,
        float borderWidth = 0,
        Color borderColor = default,
        ushort order = 0)
    {
        var vertexOffset = _vertices.Length;
        var indexOffset = _indices.Length;

        if (!_vertices.CheckCapacity(4) || !_indices.CheckCapacity(6))
            return;

        var w = rect.Width;
        var h = rect.Height;

        // Clamp radii to half the rect size to avoid overlap
        var maxR = MathF.Min(w, h) / 2;
        var radii = new Vector4(
            MathF.Min(borderRadius.TopLeft, maxR),
            MathF.Min(borderRadius.TopRight, maxR),
            MathF.Min(borderRadius.BottomLeft, maxR),
            MathF.Min(borderRadius.BottomRight, maxR));
        var rectSize = new Vector2(w, h);

        var p0 = Vector2.Transform(new Vector2(rect.X, rect.Y), transform);
        var p1 = Vector2.Transform(new Vector2(rect.X + w, rect.Y), transform);
        var p2 = Vector2.Transform(new Vector2(rect.X + w, rect.Y + h), transform);
        var p3 = Vector2.Transform(new Vector2(rect.X, rect.Y + h), transform);

        // Simple 4-vertex quad - shader handles everything
        _vertices.Add(new UIVertex
        {
            Position = p0,
            UV = new Vector2(0, 0),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p1,
            UV = new Vector2(1, 0),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p2,
            UV = new Vector2(1, 1),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });
        _vertices.Add(new UIVertex
        {
            Position = p3,
            UV = new Vector2(0, 1),
            Normal = rectSize,
            Color = color,
            BorderRatio = borderWidth,
            BorderColor = borderColor,
            CornerRadii = radii
        });

        _indices.Add((ushort)vertexOffset);
        _indices.Add((ushort)(vertexOffset + 1));
        _indices.Add((ushort)(vertexOffset + 2));
        _indices.Add((ushort)(vertexOffset + 2));
        _indices.Add((ushort)(vertexOffset + 3));
        _indices.Add((ushort)vertexOffset);

        using var _ = Graphics.PushState();
        Graphics.SetTexture(texture ?? Graphics.WhiteTexture, filter: texture?.Filter ?? TextureFilter.Point);
        Graphics.SetMesh(_mesh);
        Graphics.DrawElements(6, indexOffset, order: order);
    }

    public static void Flush()
    {
        if (_indices.Length == 0) return;
        Graphics.Driver.BindMesh(_mesh.Handle);
        Graphics.Driver.UpdateMesh(_mesh.Handle, _vertices.AsByteSpan(), _indices.AsSpan());
        _vertices.Clear();
        _indices.Clear();
    }
}
