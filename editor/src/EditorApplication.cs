//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal class EditorApplicationInstance : IApplication
{
    public void Update() => EditorApplication.Update();
    public void UpdateUI() => EditorApplication.UpdateUI();
    public void LateUpdate() => EditorApplication.LateUpdate();

    public void LoadAssets()
    {
        Importer.Update();
        EditorAssets.LoadAssets();
        EditorApplication.PostLoad();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static class EditorApplication
{
    public static EditorConfig Config { get; private set; } = null!;
    public static string OutputPath { get; private set; } = null!;
    public static string EditorPath { get; private set; } = null!;
    public static string ProjectPath { get; private set; } = null!;

    public static void Init(string editorPath, string projectPath, bool clean)
    {
        EditorPath = editorPath;
        ProjectPath = projectPath;

        Application.RegisterAssetTypes();

        AtlasDocument.RegisterDef();
        TextureDocument.RegisterDef();
        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();
        SpriteDocument.RegisterDef();
        FontDocument.RegisterDef();
        SkeletonDocument.RegisterDef();
        AnimationDocument.RegisterDef();
        VfxDocument.RegisterDef();
        BinDocument.RegisterDef();

        Config = EditorConfig.Load(Path.Combine(ProjectPath, "editor.cfg"))!;           
        if (Config == null)
        {
            Log.Warning("editor.cfg not found");
            return;
        }

        OutputPath = System.IO.Path.Combine(ProjectPath, EditorApplication.Config.OutputPath);

        Log.Info($"OutputPath: {OutputPath}");

        CollectionManager.Init(Config);
        DocumentManager.Init(Config.SourcePaths, Config.OutputPath);
        PaletteManager.Init(Config);
        DocumentManager.LoadAll();
        AtlasManager.Init();
        Importer.Init(clean);
        AssetManifest.Generate();
    }

    internal static void PostLoad()
    {
        if (Config == null)
            return;

        DocumentManager.PostLoad();
        EditorStyle.Init();
        PopupMenu.Init();
        ConfirmDialog.Init();
        Notifications.Init();
        VfxSystem.Shader = EditorAssets.Shaders.Texture;
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
        ConfirmDialog.Shutdown();
        PopupMenu.Shutdown();
        EditorStyle.Shutdown();
        CollectionManager.Shutdown();
        PaletteManager.Shutdown();
        Importer.Shutdown();
        DocumentManager.Shutdown();
        Config = null!;
    }

    public static void Update()
    {
        Importer.Update();
        ConfirmDialog.Update();
        CommandPalette.Update();
        PopupMenu.Update();
        Notifications.Update();
        Workspace.Update();
    }

    public static void UpdateUI()
    {
        Workspace.UpdateUI();
        Notifications.UpdateUI();
        PopupMenu.UpdateUI();
        CommandPalette.UpdateUI();
        ConfirmDialog.UpdateUI();
    }

    public static void LateUpdate()
    {
        Workspace.LateUpdate();
    }
}
