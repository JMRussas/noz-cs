//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class DocumentEditor(Document document) : IDisposable
{
    public Document Document { get; } = document;

    public Command[]? Commands { get; protected set; }
    public ContextMenuDef? ContextMenu { get; protected set; }

    public virtual void Update() { }
    public virtual void UpdateUI() { }
    public virtual void LateUpdate() { }
    public virtual void OnUndoRedo() { }
    public virtual void Dispose()
    {
        GC.SuppressFinalize(this);        
    }
}
