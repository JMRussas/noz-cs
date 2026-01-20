//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class EditorConfig
{
    private readonly PropertySet _props;
    private readonly string _basePath;

    public string OutputPath { get; }
    public string SavePath { get; }
    public string Palette { get; }
    public int AtlasSize { get; }
    public int AtlasPadding { get; }
    public string AtlasPrefix { get; }
    public int SpriteDpi { get; }
    public bool AtlasAntialias { get; }
    public string AtlasFilter { get; }
    public int FrameRate { get; }
    public string? GenerateCs { get; }
    public string CsNamespace { get; }
    public string CsClass { get; }
    public string? GenerateLua { get; }
    public string LuaClass { get; }
    public string[] SourcePaths { get; }
    public IEnumerable<string> Names => _props.GetKeys("names");

    public EditorConfig(PropertySet props, string basePath)
    {
        _props = props;
        _basePath = basePath;

        OutputPath = ResolvePath(props.GetString("editor", "output_path", "./build"));
        SavePath = ResolvePath(props.GetString("editor", "save_path", "./assets"));
        Palette = props.GetString("editor", "palette", "palette");
        AtlasSize = props.GetInt("editor", "atlas_size", props.GetInt("atlas", "size", 2048));

        AtlasPadding = props.GetInt("atlas", "padding", 1);
        AtlasPrefix = props.GetString("atlas", "prefix", "atlas");
        SpriteDpi = props.GetInt("atlas", "dpi", 64);
        AtlasAntialias = props.GetBool("atlas", "antialias", false);
        AtlasFilter = props.GetString("atlas", "filter", "point");

        FrameRate = props.GetInt("animation", "frame_rate", 12);

        var generateCs = props.GetString("manifest", "generate_cs", "");
        GenerateCs = string.IsNullOrEmpty(generateCs) ? null : ResolvePath(generateCs);
        CsNamespace = props.GetString("manifest", "cs_namespace", "noz");
        CsClass = props.GetString("manifest", "cs_class", "Assets");

        var generateLua = props.GetString("manifest", "generate_lua", "");
        GenerateLua = string.IsNullOrEmpty(generateLua) ? null : ResolvePath(generateLua);
        LuaClass = props.GetString("manifest", "lua_class", "Assets");

        SourcePaths = props.GetKeys("source").Select(ResolvePath).ToArray();
    }

    public int GetPaletteIndex(string name) => _props.GetInt("palettes", name, 0);

    public IEnumerable<string> GetPaletteNames() => _props.GetKeys("palettes");

    private string ResolvePath(string path)
    {
        if (Path.IsPathRooted(path))
            return path;
        return Path.GetFullPath(Path.Combine(_basePath, path));
    }

    public static EditorConfig? Load(string path)
    {
        var basePath = Path.GetFullPath(path);
        var configPath = Path.Combine(basePath, "editor.cfg");

        Log.Info($"Loading Config: {configPath}");
        
        var props = PropertySet.LoadFile(configPath);
        if (props == null)
            return null;

        return new EditorConfig(props, basePath);
    }

    public static EditorConfig? FindAndLoad(string? startPath = null)
    {
        var dir = startPath ?? Directory.GetCurrentDirectory();

        while (!string.IsNullOrEmpty(dir))
        {
            var configPath = Path.Combine(dir, "editor.cfg");
            if (File.Exists(configPath))
                return Load(configPath);

            dir = Path.GetDirectoryName(dir);
        }

        return null;
    }
}
