//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal class EditorVtable : IApplicationVtable
{
    public void Update() => EditorApplication.Update();
    public void UpdateUI() => EditorApplication.UpdateUI();

    public void LoadAssets()
    {
        Importer.WaitForAllTasks();
        EditorAssets.LoadAssets();
        EditorApplication.PostLoadInit();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static class EditorApplication
{
    public static EditorConfig? Config { get; private set; } = null!;

    public static void Init(string? projectPath, bool clean)
    {
        Log.Info($"Working Directory: {Environment.CurrentDirectory}");
        
        TextureDocument.RegisterDef();
        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();
        SpriteDocument.RegisterDef();

        Config = string.IsNullOrEmpty(projectPath)
            ? EditorConfig.FindAndLoad()
            : EditorConfig.Load(projectPath);

        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        DocumentManager.Init(Config.SourcePaths, Config.OutputPath);
        Importer.Init(clean);
        AssetManifest.Generate(Config);
    }

    internal static void PostLoadInit()
    {
        if (Config == null)
            return;

        EditorStyle.Init();
        Workspace.Init();
        PaletteManager.Init(Config);
        UserSettings.Load();
    }

    public static void Shutdown()
    {
        UserSettings.Save();
        
        Workspace.Shutdown();
        EditorStyle.Shutdown();
        PaletteManager.Shutdown();
        Importer.Shutdown();
        DocumentManager.Shutdown();
        UserSettings.Save();
        Config = null;
    }

    public static void Update()
    {
        Workspace.Update();
    }

    public static void UpdateUI()
    {
        Workspace.UpdateUI();
    }
}
