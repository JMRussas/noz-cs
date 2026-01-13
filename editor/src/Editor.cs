//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz.editor;

namespace noz;

internal class EditorVtable : IApplicationVtable
{
    public void Update() => Editor.Update();
    public void LoadAssets() =>  EditorAssets.LoadAssets();
    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static class Editor
{
    private static InputSet? _input;

    public static EditorConfig? Config { get; private set; }

    public static void Init(string? projectPath, bool clean)
    {
        _input = new InputSet();
        Input.PushInputSet(_input);

        // Register document types
        TextureDocument.RegisterDef();
        ShaderDocument.RegisterDef();

        Config = string.IsNullOrEmpty(projectPath)
            ? EditorConfig.FindAndLoad()
            : EditorConfig.Load(projectPath);

        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        InitWorkspace(Config.SourcePaths, Config.OutputPath, clean);
    }

    private static void InitWorkspace(string[] sourcePaths, string outputPath, bool clean = false)
    {
        DocumentManager.Init(sourcePaths, outputPath);
        Importer.Init(clean);

        if (Config != null)
        {
            PaletteManager.Init(Config);
            AssetManifest.Generate(Config);
        }
    }

    public static void Shutdown()
    {
        PaletteManager.Shutdown();
        Importer.Shutdown();
        DocumentManager.Shutdown();
        _input = null;
        Config = null;
    }

    public static void Update()
    {
    }

    public static void LoadAsset(Asset asset)
    {
    }

    public static void UnloadAsset(Asset asset)
    {
    }

    public static void ReloadAsset(Asset asset)
    {
    }
} 