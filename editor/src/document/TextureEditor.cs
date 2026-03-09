//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class TextureEditor : DocumentEditor
{
    private static partial class WidgetIds 
    {
        public static partial WidgetId InspectorRoot { get; }
        public static partial WidgetId Field { get; }
    }

    private WidgetId _nextFieldId;
    private float _savedScale;
    private PopupMenuItem[] _contextMenuItems;
    public new TextureDocument Document => (TextureDocument)base.Document;

    public TextureEditor(TextureDocument doc) : base(doc)
    {
        var scaleCommand = new Command { Name = "Scale", Handler = HandleScale, Key = InputCode.KeyS };
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab };

        Commands =
        [
            scaleCommand,
            exitEditCommand
        ];

        //_contextMenuItems =
        //[
        //    PopupMenuItem.FromCommand(scaleCommand),
        //    PopupMenuItem.Separator(),
        //    PopupMenuItem.FromCommand(exitEditCommand),
        //];
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.DrawBounds(selected: false);
        Document.Draw();
    }

    public override void UpdateUI()
    {
        _nextFieldId = WidgetIds.Field;

        using (UI.BeginColumn(WidgetIds.InspectorRoot, EditorStyle.Inspector.Root))
        {
            using (UI.BeginColumn(EditorStyle.Inspector.Content))
            {
                var isSprite = Document.IsSprite;
                UI.SetChecked(isSprite);
                if (UI.Toggle(NextFieldId(), "Sprite", isSprite, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    isSprite = !isSprite;
                    Undo.Record(Document);
                    Document.IsSprite = isSprite;
                    AssetManifest.IsModified = true;

                    if (isSprite)
                        AtlasManager.AddSource(Document);
                    else
                        AtlasManager.RemoveSource(Document);
                }

                var isReference = Document.IsEditorOnly;
                UI.SetChecked(isReference);
                if (UI.Toggle(NextFieldId(), "Reference", isReference, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
                {
                    isReference = !isReference;
                    Undo.Record(Document);
                    Document.IsEditorOnly = isReference;
                    AssetManifest.IsModified = true;
                }
            }
        }
    }

    private WidgetId NextFieldId(int count = 1)
    {
        var id = _nextFieldId;
        _nextFieldId += count;
        return id;
    }

    private void HandleScale()
    {
        Undo.Record(Document);
        _savedScale = Document.Scale;
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);
        Workspace.BeginTool(new ScaleTool(
            worldOrigin,
            worldOrigin,
            update: scale =>
            {
                Document.Scale = _savedScale * scale.X;
                Document.UpdateBounds();
            },
            commit: _ =>
            {                
            },
            cancel: Undo.Cancel
        ));
    }
}
