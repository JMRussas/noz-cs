//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public struct SpriteLayerDef
{
    public string Id;
    public byte Layer;
    public string? DisplayName;
    public string Label;
    public string LayerLabel;
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
    public string[] SourcePaths { get; }
    public Vector2Int[] SpriteSizes { get; }
    public SpriteLayerDef[] SpriteLayers { get; }
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


        var generateCs = props.GetString("manifest", "generate_cs", "");
        GenerateCs = string.IsNullOrEmpty(generateCs) ? null : ResolvePath(generateCs);
        CsNamespace = props.GetString("manifest", "cs_namespace", "noz");
        CsClass = props.GetString("manifest", "cs_class", "Assets");

        var generateLua = props.GetString("manifest", "generate_lua", "");
        GenerateLua = string.IsNullOrEmpty(generateLua) ? null : ResolvePath(generateLua);
        LuaClass = props.GetString("manifest", "lua_class", "Assets");

        SourcePaths = props.GetKeys("source").Select(ResolvePath).ToArray();
        SpriteSizes = ParseSpriteSizes(props);
        SpriteLayers = ParseSpriteLayers(props);
    }

    private static Vector2Int[] ParseSpriteSizes(PropertySet props)
    {
        var sizes = new List<Vector2Int>();
        foreach (var key in props.GetKeys("sprite_sizes"))
        {
            var parts = key.Split('x');
            if (parts.Length == 2 &&
                int.TryParse(parts[0], out var w) &&
                int.TryParse(parts[1], out var h))
            {
                sizes.Add(new Vector2Int(w, h));
            }
        }
        return sizes.ToArray();
    }

    private static SpriteLayerDef[] ParseSpriteLayers(PropertySet props) =>
        [.. props.GetKeys("sprite_layers")
            .Select(id =>
            {
                var value = props.GetString("sprite_layers", id, "0");
                var tk = new Tokenizer(value);

                byte layer = 0;
                string? displayName = null;

                if (tk.ExpectInt(out var intVal))
                    layer = (byte)intVal;

                displayName = tk.ExpectQuotedString();

                return new SpriteLayerDef
                {
                    Id = id,
                    Layer = layer,
                    DisplayName = displayName,
                    Label = displayName ?? id,
                    LayerLabel = $"({layer})"
                };
            })
            .Where(def => def.Layer != 0)
            .OrderByDescending(def => def.Layer)];

    public int GetPaletteIndex(string name) => _props.GetInt("palettes", name, 0);

    public IEnumerable<string> GetPaletteNames() => _props.GetKeys("palettes");

    public string GetCollectionName(string id) => _props.GetString("collections", id, id);

    public IEnumerable<string> GetCollectionIds() => _props.GetKeys("collections");

    public bool TryGetSpriteLayer(byte layer, out SpriteLayerDef layerDef)
    {
        foreach (var l in SpriteLayers)
            if (l.Layer == layer)
            {
                layerDef = l;
                return true;
            }

        layerDef = default;
        return false;
    }

    public bool TryGetSpriteLayer(string? id, out SpriteLayerDef layerDef)
    {
        if (!string.IsNullOrEmpty(id))
            foreach (var sg in SpriteLayers)
                if (sg.Id == id)
                {
                    layerDef = sg;
                    return true;
                }
        layerDef = default;
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
