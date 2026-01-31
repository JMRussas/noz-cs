//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_GRAPHICS_DEBUG
// #define NOZ_GRAPHICS_DEBUG_VERBOSE

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

    public static void Draw(Sprite sprite) => Draw(sprite, bone: sprite.BoneIndex);

    public static void Draw(Sprite sprite, int bone = -1)
    {
        if (sprite == null || SpriteAtlas == null) return;

        var bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
        if (bone >= 0)
            bounds = new Rect(bounds.X - sprite.BoneOffset.X, bounds.Y - sprite.BoneOffset.Y, bounds.Width, bounds.Height);
        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);

        using (PushState())
        {
            SetTexture(SpriteAtlas);
            SetShader(_spriteShader!);
            SetTextureFilter(sprite.TextureFilter);

            // Draw all quads (one per sort group)
            foreach (ref readonly var quad in sprite.Meshes.AsSpan())
            {
                var uv = quad.UV;
                AddQuad(
                    p0, p1, p2, p3,
                    uv.TopLeft, new Vector2(uv.Right, uv.Top),
                    uv.BottomRight, new Vector2(uv.Left, uv.Bottom),
                    order: (ushort)quad.SortOrder,
                    atlasIndex: sprite.AtlasIndex,
                    bone: bone);
            }
        }
    }

    public static void Draw(Sprite sprite, ushort order, int bone = -1)
    {
        if (sprite == null || SpriteAtlas == null) return;

        // Draw with overridden order (uses first quad's UV for backwards compat)
        var uv = sprite.UV;
        var bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
        if (bone >= 0)
            bounds = new Rect(bounds.X - sprite.BoneOffset.X, bounds.Y - sprite.BoneOffset.Y, bounds.Width, bounds.Height);
        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);

        using (PushState())
        {
            SetTexture(SpriteAtlas);
            SetShader(_spriteShader!);
            SetTextureFilter(sprite.TextureFilter);
            AddQuad(
                p0, p1, p2, p3,
                uv.TopLeft, new Vector2(uv.Right, uv.Top),
                uv.BottomRight, new Vector2(uv.Left, uv.Bottom),
                order: order,
                atlasIndex: sprite.AtlasIndex,
                bone: bone);
        }
    }

    /// <summary>
    /// Draw a sprite using the currently bound shader (does not override shader/texture).
    /// Requires SetTexture and SetShader to be called before this.
    /// </summary>
    public static void DrawRaw(Sprite sprite, ushort order = 0, int bone = -1)
    {
        if (sprite == null) return;

        var uv = sprite.UV;
        var bounds = sprite.Bounds.ToRect().Scale(sprite.PixelsPerUnitInv);
        if (bone >= 0)
            bounds = new Rect(bounds.X + sprite.BoneOffset.X, bounds.Y + sprite.BoneOffset.Y, bounds.Width, bounds.Height);
        var p0 = new Vector2(bounds.Left, bounds.Top);
        var p1 = new Vector2(bounds.Right, bounds.Top);
        var p2 = new Vector2(bounds.Right, bounds.Bottom);
        var p3 = new Vector2(bounds.Left, bounds.Bottom);

        AddQuad(
            p0, p1, p2, p3,
            uv.TopLeft, new Vector2(uv.Right, uv.Top),
            uv.BottomRight, new Vector2(uv.Left, uv.Bottom),
            order: order,
            atlasIndex: sprite.AtlasIndex,
            bone: bone);
    }

    public static void Draw(
        ReadOnlySpan<MeshVertex> vertices,
        ReadOnlySpan<ushort> indices,
        ushort order = 0,
        int bone = -1)
    {
        AddTriangles(vertices, indices, order: order, bone: bone);
    }
}
