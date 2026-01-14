//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace noz;

public class Sprite : Asset
{
    public const int Version = 1;
    
    public TextureHandle Texture { get; private set; }
    public int Width { get; private set; }
    public int Height { get; private set; }
    public Vector2 UV0 { get; private set; }
    public Vector2 UV1 { get; private set; }
    public Vector2 Pivot { get; private set; }

    private Sprite(string name) : base(AssetType.Sprite, name)
    {
        UV0 = Vector2.Zero;
        UV1 = Vector2.One;
        Pivot = new Vector2(0.5f, 0.5f);
    }

    internal void SetTexture(TextureHandle texture, int width, int height)
    {
        Texture = texture;
        Width = width;
        Height = height;
    }

    internal void SetRegion(float u0, float v0, float u1, float v1)
    {
        UV0 = new Vector2(u0, v0);
        UV1 = new Vector2(u1, v1);
    }

    internal void SetPivot(float x, float y)
    {
        Pivot = new Vector2(x, y);
    }

    private static Asset Load(Stream stream, string name)
    {
        var sprite = new Sprite(name);
        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sprite, typeof(Sprite), Load));
    }
}