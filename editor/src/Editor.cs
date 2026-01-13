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
        Log.Info($"Working Directory: {Environment.CurrentDirectory}");
        
        EditorStyle.Init();
        Workspace.Init();

        // Register document types
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

        var refSize = Workspace.GetRefSize();
        UI.Begin(refSize.X, refSize.Y);
        UpdateUI();
        UI.End();

        Workspace.DrawOverlay();

        // Composite pass - renders scene to screen with Y flip
        Workspace.DrawComposite();
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

    private static void UpdateUI()
    {
        // UI.BeginCanvas();
        // UI.EndCanvas();
    }
} 