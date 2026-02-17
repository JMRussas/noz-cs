//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class TextureEditor : DocumentEditor
{
    [ElementId("InspectorRoot")]
    [ElementId("Field", 16)]
    private static partial class ElementId { }

    private int _nextFieldId;
    private float _savedScale;
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

        ContextMenu = new PopupMenuDef
        {
            Title = "Texture",
            Items =
            [
                PopupMenuItem.FromCommand(scaleCommand),

                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(exitEditCommand),
            ]
        };
    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.DrawBounds(selected: false);
        Document.Draw();
    }

    public override void UpdateUI()
    {
        _nextFieldId = ElementId.Field;

        using (UI.BeginColumn(ElementId.InspectorRoot, EditorStyle.Inspector.Root))
        {
            using (UI.BeginColumn(EditorStyle.Inspector.Content))
            {
                var isSprite = Document.IsSprite;
                if (EditorUI.ToggleField(NextFieldId(), "Sprite", ref isSprite))
                {
                    Document.IsSprite = isSprite;
                    Document.MarkMetaModified();
                    AssetManifest.IsModified = true;

                    if (isSprite)
                        AtlasManager.AddSource(Document);
                    else
                        AtlasManager.RemoveSource(Document);
                }

                var isReference = Document.IsEditorOnly;
                if (EditorUI.ToggleField(NextFieldId(), "Reference", ref isReference))
                {
                    Document.IsEditorOnly = isReference;
                    Document.MarkMetaModified();
                    AssetManifest.IsModified = true;
                }
            }
        }
    }

    private int NextFieldId(int count = 1)
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
                Document.MarkMetaModified();
            },
            cancel: Undo.Cancel
        ));
    }
}
