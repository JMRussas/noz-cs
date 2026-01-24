//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public abstract class Tool : IDisposable
{
    public bool HideSelected { get; init; }

    public virtual void Begin() { }
    public virtual void Cancel() => Dispose();
    public virtual void Update() { }
    public virtual void Draw() { }

    public virtual void Dispose() { }
}
