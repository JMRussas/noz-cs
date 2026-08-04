//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public static partial class Graphics
{
    public static void Draw(in Rect rect, ushort order = 0, int bone = -1) =>
        Draw(rect.X, rect.Y, rect.Width, rect.Height, order: order, bone: bone);

    public static void Draw(
        in Rect rect,
        in Rect uv,
        ushort order = 0,
        int bone = -1) =>
        Draw(rect.X, rect.Y, rect.Width, rect.Height, uv.Left, uv.Top, uv.Right, uv.Bottom, order: order, bone: bone);

    public static void Draw(
        float x,
        float y,
        float width,
        float height,
        ushort order = 0,
        int bone = -1)
    {
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order: order, bone: bone);
    }

    public static void Draw(
        float x,
        float y,
        float width,
        float height,
        in Matrix3x2 transform,
        ushort order = 0,
        int bone = -1)
    {
        CurrentState.Transform = transform;
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order: order, bone: bone);
    }

    public static void Draw(
        float x,
        float y,
        float width,
        float height,
        float u0,
        float v0,
        float u1,
        float v1,
        ushort order = 0,
        int bone = -1)
    {
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), order: order, bone: bone);
    }

    public static void Draw(
        float x,
        float y,
        float width,
        float height,
        float u0,
        float v0,
        float u1,
        float v1,
        in Matrix3x2 transform,
        ushort order = 0,
        int bone = -1)
    {
        CurrentState.Transform = transform;
        var p0 = new Vector2(x, y);
        var p1 = new Vector2(x + width, y);
        var p2 = new Vector2(x + width, y + height);
        var p3 = new Vector2(x, y + height);
        AddQuad(p0, p1, p2, p3, new Vector2(u0, v0), new Vector2(u1, v0), new Vector2(u1, v1), new Vector2(u0, v1), order: order, bone: bone);
    }

    public static void Draw(
        Vector2 p0,
        Vector2 p1,
        Vector2 p2,
        Vector2 p3,
        ushort order = 0,
        int bone = -1)
    {
        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order: order, bone: bone);
    }

    public static void Draw(RenderTexture rt, Vector2 topLeft, Vector2 bottomRight, ushort order = 0)
    {
        if (rt == null) return;

        using var _ = PushState();
        SetTextureFilter(TextureFilter.Point);
        SetTexture(rt.Handle);
        SetBlendMode(BlendMode.Alpha);

        var p0 = topLeft;
        var p1 = new Vector2(bottomRight.X, topLeft.Y);
        var p2 = bottomRight;
        var p3 = new Vector2(topLeft.X, bottomRight.Y);

        AddQuad(p0, p1, p2, p3, new Vector2(0, 0), new Vector2(1, 0), new Vector2(1, 1), new Vector2(0, 1), order: order, bone: -1);
    }

    public static void Draw(Sprite sprite) => Draw(sprite, bone: sprite.BoneIndex);

    public static void Draw(Sprite sprite, int bone = -1, int frame = 0)
    {
        if (sprite == null || sprite.Atlas == null) return;

        ref readonly var sf = ref sprite.Frames[frame];
        var atlas = sprite.AtlasIndex;
        var drawBone = bone >= 0 ? bone : sprite.BoneIndex;

        Rect bounds;
        if (sf.Size.X > 0 && sf.Size.Y > 0)
            bounds = new Rect(sf.Offset.X * sprite.PixelsPerUnitInv, sf.Offset.Y * sprite.PixelsPerUnitInv, sf.Size.X * sprite.PixelsPerUnitInv, sf.Size.Y * sprite.PixelsPerUnitInv);
        else
            bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);

        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);
        var uv = sf.UV;

        SetTextureFilter(sprite.Filter);
        SetTexture(sprite.Atlas!);

        Span<MeshVertex> verts =
        [
            new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        AddTriangles(verts, indices, order: sprite.SortOrder, bone: drawBone);
    }

    public static void Draw(Sprite sprite, ushort order, int bone = -1, int frame = 0)
    {
        if (sprite == null || (sprite.Atlas == null)) return;

        ref readonly var sf = ref sprite.Frames[frame];
        var atlas = sprite.AtlasIndex;
        var uv = sf.UV;
        var meshBounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
        var p0 = new Vector2(meshBounds.Left, meshBounds.Top);
        var p1 = new Vector2(meshBounds.Right, meshBounds.Top);
        var p2 = new Vector2(meshBounds.Right, meshBounds.Bottom);
        var p3 = new Vector2(meshBounds.Left, meshBounds.Bottom);

        SetTextureFilter(sprite.Filter);
        SetTexture(sprite.Atlas!);

        Span<MeshVertex> verts =
        [
            new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        AddTriangles(verts, indices, order: order, bone: bone);
    }

    public static void DrawFlat(Sprite sprite, ushort order = 0, int bone = -1, int frame = 0)
    {
        if (sprite == null || (sprite.Atlas == null)) return;

        ref readonly var sf = ref sprite.Frames[frame];
        var atlas = sprite.AtlasIndex;

        Rect bounds;
        if (sf.Size.X > 0 && sf.Size.Y > 0)
            bounds = new Rect(sf.Offset.X * sprite.PixelsPerUnitInv, sf.Offset.Y * sprite.PixelsPerUnitInv, sf.Size.X * sprite.PixelsPerUnitInv, sf.Size.Y * sprite.PixelsPerUnitInv);
        else
            bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);

        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);
        var uv = sf.UV;

        SetTextureFilter(sprite.Filter);
        SetTexture(sprite.Atlas!);

        Span<MeshVertex> verts =
        [
            new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        AddTriangles(verts, indices, order: order, bone: bone);
    }

    public static void DrawRaw(Sprite sprite, ushort order = 0, int bone = -1, int frame = 0)
    {
        if (sprite == null) return;

        ref readonly var sf = ref sprite.Frames[frame];
        var atlas = sprite.AtlasIndex;
        var uv = sf.UV;
        var bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);

        Span<MeshVertex> verts =
        [
            new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
            new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = atlas, FrameCount = 1, Color = Color.White },
        ];
        ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
        AddTriangles(verts, indices, order: order, bone: bone);
    }

    public static void DrawAnimated(Sprite sprite, float time, bool loop = true, int bone = -1)
    {
        if (sprite == null) return;
        if (sprite.FrameCount <= 1) { Draw(sprite, bone); return; }
        var frameIndex = (int)(time * sprite.FrameRate);
        if (loop)
            frameIndex = ((frameIndex % sprite.FrameCount) + sprite.FrameCount) % sprite.FrameCount;
        else
            frameIndex = Math.Min(frameIndex, sprite.FrameCount - 1);
        Draw(sprite, bone, frameIndex);
    }

    public static void Draw(
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        ushort order = 0,
        int bone = -1)
    {
        AddTriangles(vertices, indices, order: order, bone: bone);
    }

    public static void Draw(
        Sprite sprite,
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        ushort order = 0,
        int bone = -1)
    {
        if (sprite == null || sprite.Atlas == null) return;
        SetTextureFilter(sprite.Filter);
        SetTexture(sprite.Atlas!);
        AddTriangles(vertices, indices, order: order, bone: bone);
    }

    public static void DrawText(in ReadOnlySpan<char> text, Font font, float fontSize, int order = 0) =>
        TextRender.Draw(text, font, fontSize, order);

    public static void DrawText(in ReadOnlySpan<char> text, Font font, float fontSize, Color outlineColor, float outlineWidth, float outlineSoftness, int order = 0)
    {
        TextRender.SetOutline(outlineColor, outlineWidth, outlineSoftness);
        TextRender.Draw(text, font, fontSize, order);
        TextRender.ClearOutline();
    }        

    public static void DrawText(in ReadOnlySpan<char> text, float fontSize, int order = 0) =>
        TextRender.Draw(text, UI.DefaultFont, fontSize, order);

    public static void DrawText(in ReadOnlySpan<char> text, Font font, float fontSize, Rect rect, TextOverflow overflow, int order = 0) =>
        DrawText(text, font, fontSize, rect, overflow, Color.Transparent, 0, 0, order);

    public static void DrawText(in ReadOnlySpan<char> text, Font font, float fontSize, Rect rect, TextOverflow overflow, Color outlineColor, float outlineWidth, float outlineSoftness, int order = 0)
    {
        switch (overflow)
        {
            case TextOverflow.Scale:
            {
                var textSize = TextRender.Measure(text, font, fontSize);
                if (textSize.X > rect.Width && rect.Width > 0)
                {
                    var scale = rect.Width / textSize.X;
                    fontSize *= scale;
                    textSize *= scale;
                }
                using var _ = PushState();
                MultiplyTransform(Matrix3x2.CreateTranslation(
                    rect.X + (rect.Width - textSize.X) * 0.5f,
                    rect.Y + (rect.Height - textSize.Y) * 0.5f));
                TextRender.Draw(text, font, fontSize, order);
                break;
            }
            default:
                TextRender.Draw(text, font, fontSize, order);
                break;
        }
    }

    public static void DrawText(in ReadOnlySpan<char> text, float fontSize, Rect rect, TextOverflow overflow, int order = 0) =>
        DrawText(text, UI.DefaultFont, fontSize, rect, overflow, order);

    public static void DrawSliced(Sprite sprite, in Rect targetRect, ushort order = 0, int bone = -1, int frame = 0)
    {
        if (sprite == null || (sprite.Atlas == null) || !sprite.IsSliced)
        {
            if (sprite != null) Draw(sprite, bone, frame);
            return;
        }

        if (frame >= sprite.Frames.Length) return;
        ref readonly var sf = ref sprite.Frames[frame];

        var edges = sprite.Edges;
        var mask = sprite.SliceMask != 0
            ? sprite.SliceMask
            : Sprite.CalculateSliceMask(sprite.Bounds, sprite.Edges);

        // UV splits proportional to edge pixel ratios
        var uv = sf.UV;
        var spriteW = (float)sprite.Size.X;
        var spriteH = (float)sprite.Size.Y;

        // Corners stay at native pixel size. Only shrink if the target is
        // smaller than the combined opposing edges, to avoid corner overlap.
        var maxEdgeX = edges.L + edges.R;
        var maxEdgeY = edges.T + edges.B;
        var shrinkX = maxEdgeX > 0 && targetRect.Width < maxEdgeX ? targetRect.Width / maxEdgeX : 1f;
        var shrinkY = maxEdgeY > 0 && targetRect.Height < maxEdgeY ? targetRect.Height / maxEdgeY : 1f;
        var shrink = MathF.Min(shrinkX, shrinkY);
        var edgeL = edges.L * shrink;
        var edgeR = edges.R * shrink;
        var edgeT = edges.T * shrink;
        var edgeB = edges.B * shrink;

        // 4 x-positions and 4 y-positions defining the 3x3 grid
        Span<float> xs = [targetRect.Left, targetRect.Left + edgeL, targetRect.Right - edgeR, targetRect.Right];
        Span<float> ys = [targetRect.Top, targetRect.Top + edgeT, targetRect.Bottom - edgeB, targetRect.Bottom];

        Span<float> us = [uv.Left, uv.Left + (edges.L / spriteW) * uv.Width, uv.Right - (edges.R / spriteW) * uv.Width, uv.Right];
        Span<float> vs = [uv.Top, uv.Top + (edges.T / spriteH) * uv.Height, uv.Bottom - (edges.B / spriteH) * uv.Height, uv.Bottom];

        var drawBone = bone >= 0 ? bone : sprite.BoneIndex;

        using (PushState())
        {
            SetTextureFilter(sprite.Filter);
            SetTexture(sprite.Atlas!);

            for (int row = 0; row < 3; row++)
            {
                for (int col = 0; col < 3; col++)
                {
                    if ((mask & (1 << (row * 3 + col))) == 0) continue;

                    var cellW = xs[col + 1] - xs[col];
                    var cellH = ys[row + 1] - ys[row];
                    if (cellW <= 0 || cellH <= 0) continue;

                    var cx = xs[col]; var cy = ys[row];
                    AddQuad(
                        new Vector2(cx, cy), new Vector2(cx + cellW, cy),
                        new Vector2(cx + cellW, cy + cellH), new Vector2(cx, cy + cellH),
                        new Vector2(us[col], vs[row]), new Vector2(us[col + 1], vs[row]),
                        new Vector2(us[col + 1], vs[row + 1]), new Vector2(us[col], vs[row + 1]),
                        order: order, atlasIndex: sprite.AtlasIndex, bone: drawBone);
                }
            }
        }
    }

    public static Vector2 MeasureText(ReadOnlySpan<char> text, float fontSize) =>
        TextRender.Measure(text, UI.DefaultFont, fontSize);

    public static Vector2 MeasureText(ReadOnlySpan<char> text, Font font, float fontSize) =>
        TextRender.Measure(text, font, fontSize);
}
