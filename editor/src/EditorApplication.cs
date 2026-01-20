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
        EditorApplication.PostLoad();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static class EditorApplication
{
    public static EditorConfig Config { get; private set; } = null!;

    public static void Init(string? projectPath, bool clean)
    {
        Log.Info($"Working Directory: {Environment.CurrentDirectory}");

        Application.RegisterAssetTypes();
        
        AtlasDocument.RegisterDef();
        TextureDocument.RegisterDef();
        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();
        SpriteDocument.RegisterDef();
        FontDocument.RegisterDef();

        Config = string.IsNullOrEmpty(projectPath)
            ? EditorConfig.FindAndLoad()!
            : EditorConfig.Load(projectPath)!;

        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        ShaderCompiler.Initialize();
        DocumentManager.Init(Config.SourcePaths, Config.OutputPath);
        PaletteManager.Init(Config);
        AtlasManager.Init();
        Importer.Init(clean);
        AssetManifest.Generate(Config);
    }

    internal static void PostLoad()
    {
        if (Config == null)
            return;

        DocumentManager.PostLoad();
        EditorStyle.Init();
        CommandPalette.Init();
        ContextMenu.Init();
        Notifications.Init();
        Workspace.Init();
        UserSettings.Load();

        DocumentManager.SaveAll();
    }

    public static void Shutdown()
    {
        UserSettings.Save();
        DocumentManager.SaveAll();

        Workspace.Shutdown();
        Notifications.Shutdown();
        ContextMenu.Shutdown();
        CommandPalette.Shutdown();
        EditorStyle.Shutdown();
        PaletteManager.Shutdown();
        Importer.Shutdown();
        DocumentManager.Shutdown();
        ShaderCompiler.Shutdown();
        UserSettings.Save();
        Config = null!;
    }

    public static void Update()
    {
        CommandPalette.Update();
        ContextMenu.Update();
        Notifications.Update();
        Workspace.Update();
    }

    public static void UpdateUI()
    {
        Workspace.UpdateUI();
        Notifications.UpdateUI();
        ContextMenu.UpdateUI();
        CommandPalette.UpdateUI();
    }
}
