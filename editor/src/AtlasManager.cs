//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

#define NOZ_ATLAS_DEBUG

using System.Diagnostics;
using System.Threading.Tasks;

namespace NoZ.Editor;

public static class AtlasManager
{
    private readonly static List<AtlasDocument> _atlases = new(32);
    private readonly static List<ISpriteSource> _sources = new(64);

    public static void Init()
    {
        UpdateAssets();
        Update();

        DocumentManager.DocumentAdded += HandleDocumentAdded;
    }

    public static void Shutdown()
    {
        _atlases.Clear();
        _sources.Clear();
    }

    private static void HandleDocumentAdded(Document doc)
    {
        if (doc is SpriteDocument sprite)
            AddSource(sprite);
        else if (doc is TextureDocument { IsSprite: true } texture)
            AddSource(texture);
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
                LogAtlas($"Rebuild: {atlas.Name} Atlas Modified", () => atlas.IsModified);
                rebuild |= atlas.IsModified;
                minIndex = Math.Min(minIndex, atlas.Index);
                maxIndex = Math.Max(maxIndex, atlas.Index);
                atlas.IsVisible = false;
                _atlases.Add(atlas);
            }
            else if (doc is SpriteDocument sprite)
                _sources.Add(sprite);
            else if (doc is TextureDocument { IsSprite: true } texture)
                _sources.Add(texture);
        }

        if (!rebuild)
        {
            for (int i = 0, c = _sources.Count; !rebuild && i < c; i++)
                rebuild = _sources[i].Atlas == null;

            LogAtlas($"Rebuild: One or more unatlased sprites", () => rebuild);
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
        for (int i = 0; i < _sources.Count; i++)
        {
            if (_sources[i].Atlas != null)
                continue;

            Add(_sources[i]);
        }

        for (int atlasIndex = 0; atlasIndex < _atlases.Count; atlasIndex++)
            _atlases[atlasIndex].Update();
    }

    internal static void UpdateSource(ISpriteSource source) => UpdateSource(source, null);

    internal static void UpdateSource(ISpriteSource source, PixelData<Color32>?[]? pixels)
    {
        Debug.Assert(source.Atlas != null);

        // See if the source can remain in its current atlas
        if (source.Atlas.TryUpdate(source))
        {
            source.Atlas.Update(source, pixels);
            source.Reimport();
            source.Atlas.Reimport();
            return;
        }

        // Source no longer fits in its atlas, need to relocate it
        var oldAtlas = source.Atlas;
        source.Atlas = null;
        Add(source);

        // Update both the old atlas (to clear the old rect) and the new one
        oldAtlas.Update();
        oldAtlas.Reimport();
        if (source.Atlas != oldAtlas)
        {
            source.Atlas!.Update(source, pixels);
            source.Atlas.Reimport();
        }
    }

    internal static void AddSource(ISpriteSource source)
    {
        if (!_sources.Contains(source))
            _sources.Add(source);

        if (source.Atlas != null)
            return;

        Add(source);

        if (source is SpriteDocument { IsSDF: true } spriteDoc)
            spriteDoc.UpdateAtlas();
        else
            Update();
    }

    internal static void RemoveSource(ISpriteSource source)
    {
        if (source.Atlas != null)
        {
            source.Atlas.Remove(source);
            source.Atlas.Update();
            source.Atlas.Reimport();
            source.Atlas = null;
            source.ClearAtlasUVs();
        }

        _sources.Remove(source);
        source.Reimport();
    }

    private static void Add(ISpriteSource source)
    {
        Debug.Assert(source.Atlas == null);

        for (int i = 0; i < _atlases.Count; i++)
        {
            if (_atlases[i].TryAdd(source))
            {
                _atlases[i].MarkModified();
                return;
            }
        }

        // No existing atlas has space — create a new one and pack into it
        var atlas = DocumentManager.New(AssetType.Atlas, GetAtlasName(_atlases.Count)) as AtlasDocument;
        Debug.Assert(atlas != null);
        atlas.IsVisible = false;
        _atlases.Add(atlas);

        if (!atlas.TryAdd(source))
            Log.Warning($"Add: failed to add '{source.Name}' to new atlas");
        else
            atlas.MarkModified();
    }

    public static void Rebuild()
    {
        for (int i = 0; i < _atlases.Count; i++)
            _atlases[i].Clear();

        for (int i = 0; i < _sources.Count; i++)
        {
            Debug.Assert(_sources[i].Atlas == null);
            Add(_sources[i]);
        }

        for (int atlasIndex = _atlases.Count - 1; atlasIndex > 1; atlasIndex--)
            if (_atlases[atlasIndex].RectCount == 0)
                DocumentManager.Delete(_atlases[atlasIndex]);

        for (int atlasIndex = 0; atlasIndex < _atlases.Count; atlasIndex++)
            _atlases[atlasIndex].Update();

        DocumentManager.SaveAll();
    }

    [Conditional("NOZ_ATLAS_DEBUG")]
    public static void LogAtlas(string msg, Func<bool>? condition = null)
    {
        if (condition == null || condition())
            Log.Debug($"[ATLAS] {msg}");
    }
}
