//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz.editor;

namespace noz;

internal class EditorVtable : IApplicationVtable
{
    public void Update() => Editor.Update();
    public void UpdateUI() => Editor.UpdateUI();

    public void LoadAssets()
    {
        Importer.WaitForAllTasks();
        EditorAssets.LoadAssets();
        Editor.PostLoadInit();
    }

    public void UnloadAssets() => EditorAssets.UnloadAssets();
    public void ReloadAssets() => EditorAssets.ReloadAssets();
}

public static class Editor
{
    public static EditorConfig? Config { get; private set; }

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
        CheckShortcuts();

        Workspace.Update();
        Workspace.Draw();

        DrawDocuments();
        DrawSelectionBounds();

        if (Workspace.State == WorkspaceState.Edit && Workspace.ActiveDocument != null)
        {
            Workspace.ActiveDocument.UpdateEdit();
            Workspace.ActiveDocument.DrawEdit();
        }

        Workspace.DrawOverlay();
    }

    public static void UpdateUI()
    {
    }

    private static void CheckShortcuts()
    {
        if (Input.WasButtonPressed(InputCode.KeyTab))
            Workspace.ToggleEdit();

        if (Input.WasButtonPressed(InputCode.KeyF))
            Workspace.FrameSelected();

        if (Input.WasButtonPressed(InputCode.KeyS) && Input.IsCtrlDown())
            DocumentManager.SaveAll();
    }

    private static void DrawDocuments()
    {
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;

            if (doc.IsEditing)
                continue;

            doc.Draw();
        }
    }

    private static void DrawSelectionBounds()
    {
        if (Workspace.ActiveDocument != null)
        {
            EditorRender.DrawBounds(Workspace.ActiveDocument, EditorStyle.EdgeColor);
            return;
        }

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.Loaded || !doc.PostLoaded)
                continue;

            if (doc.IsSelected)
                EditorRender.DrawBounds(doc, EditorStyle.SelectionColor32);
        }
    }
}
