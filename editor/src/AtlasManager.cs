//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

// #define NOZ_ATLAS_DEBUG

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

        DocumentManager.DocumentAdded += HandleDocumentAdded;
    }

    public static void Shutdown()
    {
        _atlases.Clear();
        _sprites.Clear();
    }

    private static void HandleDocumentAdded(Document doc)
    {
        if (doc is not SpriteDocument sprite) return;
        AddSprite(sprite);
        Update();
    }

    private static string GetAtlasName(int index) => $"{EditorApplication.Config.AtlasPrefix}{index:000}.atlas";
    internal static int GetAtlasIndex(string name)
    {
        if (!name.StartsWith(EditorApplication.Config.AtlasPrefix))
            return -1;
        var indexStr = name.Substring(EditorApplication.Config.AtlasPrefix.Length);
        if (int.TryParse(indexStr, out var index))
            return index;
        return -1;
    }

    private static void UpdateAssets()
    {
        _atlases.Clear();

        var rebuild = false;
        var minIndex = int.MaxValue;
        var maxIndex = int.MinValue;
        for (int i = 0, c = DocumentManager.Count; i < c; i++ )
        {
            var doc = DocumentManager.Get(i);
            if (doc is AtlasDocument atlas)
            {
                if (!atlas.Name.StartsWith(EditorApplication.Config.AtlasPrefix)) continue;
                LogAtlas($"Rebuild: {atlas.Name} Rect Count 0", () => atlas.RectCount == 0);
                rebuild |= atlas.RectCount == 0;
                atlas.ResolveSprites();
                minIndex = Math.Min(minIndex, atlas.Index);
                maxIndex = Math.Max(maxIndex, atlas.Index);
                atlas.IsVisible = false;
                _atlases.Add(atlas);
            }
            else if (doc is SpriteDocument sprite)
                _sprites.Add(sprite);
        }

        if (!rebuild && (minIndex != 0 || maxIndex != _atlases.Count - 1))
        {
            LogAtlas($"Rebuild: No Atlases", () => _atlases.Count == 0);
            LogAtlas($"Rebuild: Atlas index mismatch", () => _atlases.Count > 0);
            rebuild = true;
        }

        if (rebuild)
            Rebuild();
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

    public static void UpdateSprite(SpriteDocument sprite)
    {
        Debug.Assert(sprite.Atlas != null);

        // See if the sprite can remain in its current atlas
        if (sprite.Atlas.TryUpdate(sprite))
        {
            sprite.Atlas.Update();
            return;
        }
    }

    private static void AddSprite(SpriteDocument sprite)
    {
        Debug.Assert(sprite.Atlas == null);

        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].TryAddSprite(sprite))
            {
                _atlases[i].MarkModified();
                return;
            }
        }

        var atlas = DocumentManager.New(AssetType.Atlas, GetAtlasName(_atlases.Count)) as AtlasDocument;
        Debug.Assert(atlas != null);
        atlas.IsVisible = false;
        _atlases.Add(atlas);
    }

    public static void Rebuild()
    {
        for (int i = 0; i < _atlases.Count; i++)
            _atlases[i].Clear();

        for (int spriteIndex = 0; spriteIndex < _sprites.Count; spriteIndex++)
        {
            Debug.Assert(_sprites[spriteIndex].Atlas == null);
            AddSprite(_sprites[spriteIndex]);
        }

        for (int atlasIndex = _atlases.Count - 1; atlasIndex > 1; atlasIndex--)
            if (_atlases[atlasIndex].RectCount == 0)
                DocumentManager.Delete(_atlases[atlasIndex]);

        for (int atlasIndex = 0; atlasIndex < _atlases.Count; atlasIndex++)
            _atlases[atlasIndex].Update();
    }

    [Conditional("NOZ_ATLAS_DEBUG")]
    public static void LogAtlas(string msg, Func<bool>? condition = null)
    {
        if (condition == null || condition())
            Log.Debug($"[ATLAS] {msg}");
    }
}


