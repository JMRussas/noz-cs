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
    public string AtlasPrefix { get; }
    public int PixelsPerUnit { get; }
    public int FrameRate { get; }
    public string? GenerateCs { get; }
    public string CsNamespace { get; }
    public string CsClass { get; }
    public string? GenerateLua { get; }
    public string LuaClass { get; }
    public string[] SourcePaths { get; }
    public IEnumerable<string> Names => _props.GetKeys("names");

    public EditorConfig(PropertySet props)
    {
        _props = props;

        OutputPath = ResolvePath(props.GetString("editor", "output_path", "./library"));
        SavePath = ResolvePath(props.GetString("editor", "save_path", "./assets"));
        Palette = props.GetString("editor", "palette", "palette");
        AtlasSize = props.GetInt("editor", "atlas_size", props.GetInt("atlas", "size", 2048));
        PixelsPerUnit = props.GetInt("editor", "pixels_per_unit", 64);

        AtlasPrefix = props.GetString("atlas", "prefix", "atlas");

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
