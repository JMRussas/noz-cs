//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public struct SortOrderDef
{
    public string Id;
    public byte SortOrder;
    public string? DisplayName;
    public string Label;
    public string SortOrderLabel;
}

public struct SpriteSize(Vector2Int size, string label)
{
    public Vector2Int Size = size; 
    public string Label = label;
} 

public class EditorConfig
{
    private readonly PropertySet _props;

    public string OutputPath { get; }
    public string SavePath { get; }
    public string Palette { get; }
    public int AtlasSize { get; }
    public int AtlasPadding { get; }
    public string AtlasPrefix { get; }
    public int AtlasMaxSpriteSize { get; }
    public int PixelsPerUnit { get; }
    public float PixelsPerUnitInv { get; }
    public int FrameRate { get; }
    public string? GenerateCs { get; }
    public string CsNamespace { get; }
    public string CsClass { get; }
    public string? GenerateLua { get; }
    public string LuaClass { get; }
    public string GenerationServer { get; }
    public string[] SourcePaths { get; }
    public SpriteSize[] SpriteSizes { get; }
    public SortOrderDef[] SortOrders { get; }
    public IEnumerable<string> Names => _props.GetKeys("names");

    public EditorConfig(PropertySet props)
    {
        _props = props;

        OutputPath = ResolvePath(props.GetString("editor", "output_path", "./library"));
        SavePath = ResolvePath(props.GetString("editor", "save_path", "./assets"));
        Palette = props.GetString("editor", "palette", "palette");
        AtlasSize = props.GetInt("atlas", "size", 2048);
        AtlasPadding = props.GetInt("atlas", "padding", 2);
        AtlasPrefix = props.GetString("atlas", "prefix", "sprites");
        AtlasMaxSpriteSize = props.GetInt("atlas", "max_sprite_size", 256);
        AtlasMaxSpriteSize = Math.Min(AtlasSize / 4 * 3, AtlasMaxSpriteSize);
        PixelsPerUnit = props.GetInt("editor", "pixels_per_unit", 64);
        PixelsPerUnitInv = 1.0f / PixelsPerUnit;
        FrameRate = props.GetInt("animation", "frame_rate", 12);


        GenerationServer = props.GetString("generate", "server", "http://127.0.0.1:5555");

        var generateCs = props.GetString("manifest", "generate_cs", "");
        GenerateCs = string.IsNullOrEmpty(generateCs) ? null : ResolvePath(generateCs);
        CsNamespace = props.GetString("manifest", "cs_namespace", "noz");
        CsClass = props.GetString("manifest", "cs_class", "Assets");

        var generateLua = props.GetString("manifest", "generate_lua", "");
        GenerateLua = string.IsNullOrEmpty(generateLua) ? null : ResolvePath(generateLua);
        LuaClass = props.GetString("manifest", "lua_class", "Assets");

        SourcePaths = props.GetKeys("source").Select(ResolvePath).ToArray();
        SpriteSizes = ParseSpriteSizes(props);
        SortOrders = ParseSortOrders(props);
    }

    private static SpriteSize[] ParseSpriteSizes(PropertySet props)
    {
        var sizes = new List<SpriteSize>();
        foreach (var key in props.GetKeys("sprite_sizes"))
        {
            var parts = key.Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var w) &&
                int.TryParse(parts[1], out var h))
            {
                sizes.Add(new SpriteSize(new Vector2Int(w, h), $"{w} x {h}"));
            }
        }
        return sizes.ToArray();
    }

    private static SortOrderDef[] ParseSortOrders(PropertySet props)
    {
        // Try new section name first, fall back to legacy
        var section = props.GetKeys("sort_orders").Any() ? "sort_orders" : "sprite_layers";
        return [.. props.GetKeys(section)
            .Select(id =>
            {
                var value = props.GetString(section, id, "0");
                var tk = new Tokenizer(value);

                byte sortOrder = 0;
                string? displayName = null;

                if (tk.ExpectInt(out var intVal))
                    sortOrder = (byte)intVal;

                displayName = tk.ExpectQuotedString();

                return new SortOrderDef
                {
                    Id = id,
                    SortOrder = sortOrder,
                    DisplayName = displayName,
                    Label = displayName ?? id,
                    SortOrderLabel = $"({sortOrder})"
                };
            })
            .Where(def => def.SortOrder != 0)
            .OrderByDescending(def => def.SortOrder)];
    }

    public IEnumerable<string> GetKeys(string section) => _props.GetKeys(section);

    public string GetString(string section, string key, string defaultValue) =>
        _props.GetString(section, key, defaultValue);

    public string GetCollectionName(string id) => _props.GetString("collections", id, id);

    public IEnumerable<string> GetCollectionIds() => _props.GetKeys("collections");

    public bool TryGetSortOrder(byte sortOrder, out SortOrderDef def)
    {
        foreach (var s in SortOrders)
            if (s.SortOrder == sortOrder)
            {
                def = s;
                return true;
            }

        def = default;
        return false;
    }

    public bool TryGetSortOrder(string? id, out SortOrderDef def)
    {
        if (!string.IsNullOrEmpty(id))
            foreach (var s in SortOrders)
                if (s.Id == id)
                {
                    def = s;
                    return true;
                }
        def = default;
        return false;
    }

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(EditorApplication.ProjectPath, path));
    }

    public static EditorConfig? Load(string path)
    {
        if (!File.Exists(path)) return null;

        Log.Info($"Loading Config: {path}");

        var props = PropertySet.LoadFile(path);
        if (props == null)
            return null;

        return new EditorConfig(props);
    }
}
