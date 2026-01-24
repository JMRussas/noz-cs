//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public static class Gizmos
{
    private const float DefaultLineWidth = 0.02f;
    private const float DefaultVertexSize = 0.12f;
    
    public static float ZoomRefScale => 1f / Workspace.Zoom;

    public static void SetColor(Color color) => Graphics.SetColor(color);
    
    public static Graphics.AutoState PushState(ushort layer)
    {
        var state = Graphics.PushState();
        Graphics.SetLayer(layer);
        Graphics.SetTexture(Workspace.WhiteTexture);
        Graphics.SetShader(EditorAssets.Shaders.Texture);
        return state;
    }

    public static void DrawRect(Document doc, float width = 1.0f)
    {
        DrawRect(doc.Bounds, width);
    }

    public static void DrawRect(Rect rect, float width = 1.0f, ushort order=0, bool outside=false)
    {
        var topLeft = new Vector2(rect.Left, rect.Top);
        var topRight = new Vector2(rect.Right, rect.Top);
        var bottomLeft = new Vector2(rect.Left, rect.Bottom);
        var bottomRight = new Vector2(rect.Right, rect.Bottom);

        if (outside)
        {
            var scaledWidth = width * ZoomRefScale;
            topLeft += new Vector2(-scaledWidth, -scaledWidth);
            topRight += new Vector2(scaledWidth, -scaledWidth);
            bottomLeft += new Vector2(-scaledWidth, scaledWidth);
            bottomRight += new Vector2(scaledWidth, scaledWidth);
        }

        DrawLine(topLeft, topRight, width, extendEnds: true, order: order);
        DrawLine(topRight, bottomRight, width, extendEnds: true, order: order);
        DrawLine(bottomRight, bottomLeft, width, extendEnds: true, order: order);
        DrawLine(bottomLeft, topLeft, width, extendEnds: true, order: order);
    }

    public static void DrawRotatedRect(Rect bounds, float rotation, float width = 1.0f, ushort order = 0)
    {
        Graphics.PushState();
        Graphics.SetTransform(Matrix3x2.CreateRotation(rotation) * Graphics.Transform);
        DrawRect(bounds, width, order);
        Graphics.PopState();
    }

    public static void DrawRotatedRect(Rect bounds, Vector2 pivot, float rotation, float width = 1.0f, ushort order = 0)
    {
        Graphics.PushState();
        Graphics.SetTransform(Matrix3x2.CreateRotation(rotation, pivot) * Graphics.Transform);
        DrawRect(bounds, width, order);
        Graphics.PopState();
    }

    public static void DrawLine(Vector2 v0, Vector2 v1, float width, bool extendEnds = false, ushort order=0)
    {
        var delta = v1 - v0;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var perp = new Vector2(-dir.Y, dir.X);
        var halfWidth = width * ZoomRefScale;

        var start = v0;
        var end = v1;
        if (extendEnds)
        {
            start -= dir * halfWidth;
            end += dir * halfWidth;
        }

        var p0 = start - perp * halfWidth;
        var p1 = start + perp * halfWidth;
        var p2 = end + perp * halfWidth;
        var p3 = end - perp * halfWidth;

        Graphics.Draw(p0, p1, p2, p3, order);
    }

    public static void DrawRect(in Vector2 position, float size, ushort order=0)
    {
        var scaledSize = ZoomRefScale * size;
        var halfSize = scaledSize * 0.5f;
        Graphics.Draw(position.X - halfSize, position.Y - halfSize, scaledSize, scaledSize, order);
    }

    public static void DrawCircle(Vector2 pos, float radius)
    {
        Graphics.Draw(pos.X - radius, pos.Y - radius, radius * 2, radius * 2);
    }
    
    public static float GetVertexSize(float size=1.0f)
    {
        return DefaultVertexSize * size;
    }
    
    public static float GetLineWidth(float width=1.0f)
    {
        return DefaultLineWidth * width;
    }

    public static void DrawBone(Vector2 start, Vector2 end, float width, Color color)
    {
        Gizmos.SetColor(color);
        Gizmos.DrawLine(start, end, width);
    }

    public static void DrawDashedLine(Vector2 start, Vector2 end, float width, float dashLength, Color color)
    {
        var delta = end - start;
        var length = delta.Length();
        if (length < 0.0001f)
            return;

        var dir = delta / length;
        var pos = 0f;
        var drawing = true;

        dashLength *= ZoomRefScale;

        SetColor(color);

        while (pos < length)
        {
            var segmentEnd = MathF.Min(pos + dashLength, length);
            if (drawing)
            {
                var p0 = start + dir * pos;
                var p1 = start + dir * segmentEnd;
                DrawLine(p0, p1, width);
            }
            pos = segmentEnd;
            drawing = !drawing;
        }
    }
}
