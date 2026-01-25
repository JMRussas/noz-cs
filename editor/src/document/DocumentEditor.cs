//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class DocumentEditor(Document document) : IDisposable
{
    public Document Document { get; } = document;

    public virtual Command[]? GetCommands() => null;
    public virtual ContextMenuItem[]? GetContextMenuItems() => null;
    public virtual void Update() { }
    public virtual void UpdateUI() { }
    public virtual void LateUpdate() { }
    public virtual void OnUndoRedo() { }
    public virtual void Dispose() { }
}
