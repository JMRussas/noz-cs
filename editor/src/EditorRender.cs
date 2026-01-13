//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz.editor;

public static class EditorRender
{
    private const float DefaultLineWidth = 0.02f;
    private const float DefaultVertexSize = 0.12f;
    private const byte BoundsLayer = 200;

    public static float ZoomRefScale => 1f / Workspace.Zoom;

    public static void DrawBounds(Document doc, Color32 color, float expand = 0f)
    {
        var bounds = doc.Bounds.Expand(expand).Offset(doc.Position);
        DrawBounds(bounds, color);
    }

    public static void DrawBounds(Rect bounds, Color32 color)
    {
        var topLeft = new Vector2(bounds.Left, bounds.Top);
        var topRight = new Vector2(bounds.Right, bounds.Top);
        var bottomLeft = new Vector2(bounds.Left, bounds.Bottom);
        var bottomRight = new Vector2(bounds.Right, bounds.Bottom);

        DrawLine(topLeft, topRight, color);
        DrawLine(topRight, bottomRight, color);
        DrawLine(bottomRight, bottomLeft, color);
        DrawLine(bottomLeft, topLeft, color);
    }

    public static void DrawLine(Vector2 v0, Vector2 v1, Color32 color, float width = DefaultLineWidth)
    {
        var delta = v1 - v0;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var mid = (v0 + v1) * 0.5f;

        var scaleX = length * 0.5f;
        var scaleY = width * ZoomRefScale;

        var cos = dir.X;
        var sin = dir.Y;
        var transform = new Matrix3x2(
            cos * scaleX, sin * scaleX,
            -sin * scaleY, cos * scaleY,
            mid.X, mid.Y
        );

        Render.DrawQuad(-1, -1, 2, 2, transform, color, BoundsLayer);
    }

    public static void DrawVertex(Vector2 position, Color32 color, float size = DefaultVertexSize)
    {
        var scaledSize = size * ZoomRefScale;
        var halfSize = scaledSize * 0.5f;
        Render.DrawQuad(position.X - halfSize, position.Y - halfSize, scaledSize, scaledSize, color, BoundsLayer);
    }
}
