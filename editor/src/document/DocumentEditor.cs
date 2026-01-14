//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class DocumentEditor(Document document) : IDisposable
{
    public Document Document { get; } = document;

    public virtual void Update() { }
    public virtual void Draw() { }
    public virtual void UpdateUI() { }
    public virtual void Dispose() { }
}
