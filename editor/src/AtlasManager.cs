//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ.Editor;

public static class AtlasManager
{
    private readonly static List<AtlasDocument> _atlases = new(32);
    private readonly static List<SpriteDocument> _sprites = new(32);

    public static void Init()
    {
        UpdateAssets();
        Update();
        DocumentManager.SaveAll();
    }

    public static void Shutdown()
    {
        _atlases.Clear();
        _sprites.Clear();
    }

    private static string GetAtlasName(int index) => $"{EditorApplication.Config.AtlasPrefix}{index:000}.atlas";

    private static void UpdateAssets()
    {
        _atlases.Clear();

        for (int i = 0, c = DocumentManager.Count; i < c; i++ )
        {
            var doc = DocumentManager.Get(i);
            if (doc is AtlasDocument atlas)
            {
                atlas.Index = _atlases.Count;
                atlas.ResolveSprites();
                _atlases.Add(atlas);
            }
            else if (doc is SpriteDocument sprite)
                _sprites.Add(sprite);
        }

        if (_atlases.Count == 0)
        {
            var atlas = DocumentManager.New(AssetType.Atlas, GetAtlasName(0)) as AtlasDocument;
            Debug.Assert(atlas != null);
            atlas.Index = 0;
            _atlases.Add(atlas);
        }
    }

    public static void Update()
    {
        for (int spriteIndex = 0; spriteIndex < _sprites.Count; spriteIndex++)
        {
            var sprite = _sprites[spriteIndex];
            if (sprite.Atlas != null) 
                continue;

            AddSprite(sprite);
        }

        for (int atlasIndex = 0; atlasIndex < _atlases.Count; atlasIndex++ )
            _atlases[atlasIndex].Update();
    }

    private static void AddSprite(SpriteDocument sprite)
    {
        Debug.Assert(sprite.Atlas == null);

        for (int i = 0; i < _atlases.Count; i++)
        {
            var atlas = _atlases[i];
            if (atlas.TryAddSprite(sprite))
            {
                atlas.MarkModified();
                return;
            }
        }
    }

    public static void Rebuild()
    {
        
    }
}


