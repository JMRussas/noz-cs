//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class EditorRender
{
    public const int GridLayer = 100;
    public const int PixelGridLayer = 1000;
    public const int GizmoLayer = 1100;
    
    private const float DefaultLineWidth = 0.02f;
    private const float DefaultVertexSize = 0.12f;
    private const byte BoundsLayer = 200;

    public static float ZoomRefScale => 1f / Workspace.Zoom;

    public static void SetColor(Color color) => Render.SetColor(color);
    
    public static void DrawBounds(Document doc, float expand = 0f)
    {
        var bounds = doc.Bounds.Expand(expand).Offset(doc.Position);
        DrawBounds(bounds);
    }

    public static void DrawBounds(Rect bounds)
    {
        var topLeft = new Vector2(bounds.Left, bounds.Top);
        var topRight = new Vector2(bounds.Right, bounds.Top);
        var bottomLeft = new Vector2(bounds.Left, bounds.Bottom);
        var bottomRight = new Vector2(bounds.Right, bounds.Bottom);

        DrawLine(topLeft, topRight);
        DrawLine(topRight, bottomRight);
        DrawLine(bottomRight, bottomLeft);
        DrawLine(bottomLeft, topLeft);
    }

    public static void DrawLine(Vector2 v0, Vector2 v1, float width = 1.0f)
    {
        var delta = v1 - v0;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var perp = new Vector2(-dir.Y, dir.X);
        var halfWidth = DefaultLineWidth * width * ZoomRefScale;

        var p0 = v0 - perp * halfWidth;
        var p1 = v0 + perp * halfWidth;
        var p2 = v1 + perp * halfWidth;
        var p3 = v1 - perp * halfWidth;

        Render.DrawQuad(p0, p1, p2, p3);
    }

    public static void DrawVertex(Vector2 position, float size = 1.0f)
    {
        var scaledSize = DefaultVertexSize * size * ZoomRefScale;
        var halfSize = scaledSize * 0.5f;
        Render.DrawQuad(position.X - halfSize, position.Y - halfSize, scaledSize, scaledSize);
    }

    public static void DrawCircle(Vector2 pos, float radius)
    {
        Render.DrawQuad(pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
    }
}
