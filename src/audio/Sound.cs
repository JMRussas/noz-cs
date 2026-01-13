//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public class Sound : Asset
{
    private Sound(string name) : base(AssetType.Sound, name)
    {
    }

    private static Asset Load(Stream stream, string name)
    {
        var sprite = new Sound(name);
        return sprite;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sound, typeof(Sound), Load));
    }
}