//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public class Sprite : Asset 
{
    private Sprite(string name) : base(AssetType.Sprite, name)
    {
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