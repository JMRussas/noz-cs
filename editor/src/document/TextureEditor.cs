//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal class TextureEditor : DocumentEditor
{
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

        ContextMenu = new ContextMenuDef
        {
            Title = "Reference",
            Items = [                
                ContextMenuItem.FromCommand(scaleCommand),

                ContextMenuItem.Separator(),
                ContextMenuItem.FromCommand(exitEditCommand),
            ]
        };

    }

    public override void Update()
    {
        Graphics.SetTransform(Document.Transform);
        Document.DrawBounds(selected: false);
        Document.Draw();
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
