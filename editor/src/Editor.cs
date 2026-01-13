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
    public static EditorConfig? Config { get; private set; }

    public static void Init(string? projectPath, bool clean)
    {
        EditorStyle.Init();
        Workspace.Init();

        // Register document types
        TextureDocument.RegisterDef();
        ShaderDocument.RegisterDef();
        SoundDocument.RegisterDef();

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
        PaletteManager.Init(Config);
        AssetManifest.Generate(Config);
    }

    public static void Shutdown()
    {
        Workspace.Shutdown();
        EditorStyle.Shutdown();
        PaletteManager.Shutdown();
        Importer.Shutdown();
        DocumentManager.Shutdown();
        Config = null;
    }

    public static void Update()
    {
        Workspace.Update();
        Workspace.Draw();
        Workspace.DrawOverlay();
    }
} 