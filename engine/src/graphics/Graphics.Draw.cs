//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_GRAPHICS_DEBUG
// #define NOZ_GRAPHICS_DEBUG_VERBOSE

using System.Numerics;

namespace NoZ;

public static partial class Graphics
{
    private static Shader GetSpriteShader(Sprite sprite) =>
        sprite.IsSDF && _spriteSdfShader != null ? _spriteSdfShader : _spriteShader!;

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

    /// <summary>
    /// Draws a render texture as a textured quad between topLeft and bottomRight.
    /// </summary>
    public static void Draw(RenderTexture rt, Vector2 topLeft, Vector2 bottomRight, ushort order = 0)
    {
        if (!rt.IsValid) return;

        using var _ = PushState();
        SetShader(_spriteShader!);
        SetTexture(rt.Handle, filter: TextureFilter.Linear);
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
        if (sprite == null || SpriteAtlas == null) return;

        var fi = sprite.FrameTable[frame];

        using (PushState())
        {
            SetTexture(SpriteAtlas);
            SetShader(GetSpriteShader(sprite));
            SetTextureFilter(sprite.TextureFilter);

            var baseColor = CurrentState.Color;

            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

                // For SDF sprites, pass fill color as vertex color instead of setting graphics state
                var vertexColor = sprite.IsSDF ? mesh.FillColor : Color.White;

                // Use per-mesh bounds if available, otherwise fall back to sprite bounds
                Rect bounds;
                if (mesh.Size.X > 0 && mesh.Size.Y > 0)
                {
                    bounds = new Rect(
                        mesh.Offset.X * sprite.PixelsPerUnitInv,
                        mesh.Offset.Y * sprite.PixelsPerUnitInv,
                        mesh.Size.X * sprite.PixelsPerUnitInv,
                        mesh.Size.Y * sprite.PixelsPerUnitInv);
                }
                else
                {
                    bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
                }

                var p0 = new Vector2(bounds.Left, bounds.Top);
                var p1 = new Vector2(bounds.Right, bounds.Top);
                var p2 = new Vector2(bounds.Right, bounds.Bottom);
                var p3 = new Vector2(bounds.Left, bounds.Bottom);

                var uv = mesh.UV;
                // Use per-mesh bone if available, otherwise use sprite default or passed bone
                var meshBone = mesh.BoneIndex >= 0 ? mesh.BoneIndex : bone;

                Span<MeshVertex> verts =
                [
                    new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                ];
                ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
                AddTriangles(verts, indices, order: (ushort)mesh.SortOrder, bone: meshBone);
            }
        }
    }

    public static void Draw(Sprite sprite, ushort order, int bone = -1, int frame = 0)
    {
        if (sprite == null || SpriteAtlas == null) return;

        var fi = sprite.FrameTable[frame];

        using (PushState())
        {
            SetTexture(SpriteAtlas);
            SetShader(GetSpriteShader(sprite));
            SetTextureFilter(sprite.TextureFilter);

            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

                var vertexColor = sprite.IsSDF ? mesh.FillColor : Color.White;

                var uv = mesh.UV;
                var meshBounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
                var p0 = new Vector2(meshBounds.Left, meshBounds.Top);
                var p1 = new Vector2(meshBounds.Right, meshBounds.Top);
                var p2 = new Vector2(meshBounds.Right, meshBounds.Bottom);
                var p3 = new Vector2(meshBounds.Left, meshBounds.Bottom);

                Span<MeshVertex> verts =
                [
                    new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                ];
                ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
                AddTriangles(verts, indices, order: order, bone: bone);
            }
        }
    }

    public static void DrawFlat(Sprite sprite, ushort order = 0, int bone = -1, int frame = 0)
    {
        if (sprite == null || SpriteAtlas == null) return;

        var fi = sprite.FrameTable[frame];

        using (PushState())
        {
            SetTexture(SpriteAtlas);
            SetShader(GetSpriteShader(sprite));
            SetTextureFilter(sprite.TextureFilter);

            for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
            {
                ref readonly var mesh = ref sprite.Meshes[i];

                var vertexColor = sprite.IsSDF ? mesh.FillColor : Color.White;

                // Use per-mesh bounds if available, otherwise fall back to sprite bounds
                Rect bounds;
                if (mesh.Size.X > 0 && mesh.Size.Y > 0)
                {
                    bounds = new Rect(
                        mesh.Offset.X * sprite.PixelsPerUnitInv,
                        mesh.Offset.Y * sprite.PixelsPerUnitInv,
                        mesh.Size.X * sprite.PixelsPerUnitInv,
                        mesh.Size.Y * sprite.PixelsPerUnitInv);
                }
                else
                {
                    bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
                }

                var p0 = new Vector2(bounds.Left, bounds.Top);
                var p1 = new Vector2(bounds.Right, bounds.Top);
                var p2 = new Vector2(bounds.Right, bounds.Bottom);
                var p3 = new Vector2(bounds.Left, bounds.Bottom);

                var uv = mesh.UV;
                Span<MeshVertex> verts =
                [
                    new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                    new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                ];
                ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
                AddTriangles(verts, indices, order: order, bone: bone);
            }
        }
    }

    public static void DrawRaw(Sprite sprite, ushort order = 0, int bone = -1, int frame = 0)
    {
        if (sprite == null) return;

        var fi = sprite.FrameTable[frame];

        for (int i = fi.MeshStart; i < fi.MeshStart + fi.MeshCount; i++)
        {
            ref readonly var mesh = ref sprite.Meshes[i];

            var vertexColor = sprite.IsSDF ? mesh.FillColor : Color.White;

            var uv = mesh.UV;
            var bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
            var p0 = new Vector2(bounds.Left, bounds.Top);
            var p1 = new Vector2(bounds.Right, bounds.Top);
            var p2 = new Vector2(bounds.Right, bounds.Bottom);
            var p3 = new Vector2(bounds.Left, bounds.Bottom);

            Span<MeshVertex> verts =
            [
                new MeshVertex { Position = p0, UV = uv.TopLeft, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                new MeshVertex { Position = p1, UV = new Vector2(uv.Right, uv.Top), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                new MeshVertex { Position = p2, UV = uv.BottomRight, Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
                new MeshVertex { Position = p3, UV = new Vector2(uv.Left, uv.Bottom), Normal = Vector2.Zero, Atlas = sprite.AtlasIndex, FrameCount = 1, Color = vertexColor },
            ];
            ReadOnlySpan<ushort> indices = [0, 1, 2, 2, 3, 0];
            AddTriangles(verts, indices, order: order, bone: bone);
        }
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

    public static void DrawText(in ReadOnlySpan<char> text, Font font, float fontSize, int order = 0) =>
        TextRender.Draw(text, font, fontSize, order);

    public static void DrawText(in ReadOnlySpan<char> text, float fontSize, int order = 0) =>
        TextRender.Draw(text, UI.DefaultFont, fontSize, order);

    public static void DrawText(in ReadOnlySpan<char> text, Font font, float fontSize, Rect rect, TextOverflow overflow, int order = 0)
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

    public static Vector2 MeasureText(ReadOnlySpan<char> text, float fontSize) =>
        TextRender.Measure(text, UI.DefaultFont, fontSize);

    public static Vector2 MeasureText(ReadOnlySpan<char> text, Font font, float fontSize) =>
        TextRender.Measure(text, font, fontSize);
}
