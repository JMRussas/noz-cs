//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using Silk.NET.Maths;

namespace NoZ.Editor;

public abstract class Document : IDisposable
{
    public DocumentDef Def { get; internal set; } = null!;
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int SourcePathIndex { get; set; } = -1;

    public Vector2 Position { get; set; }
    public Vector2 SavedPosition { get; set; }
    public Rect Bounds { get; set; } = new(-0.5f, -0.5f, 1f, 1f);

    public Matrix3x2 Transform => Matrix3x2.CreateTranslation(Position);
    
    public bool IsSelected { get; set; }
    public bool IsEditing { get; set; }
    public bool IsModified { get; set; }
    public bool IsMetaModified { get; set; }
    public bool IsClipped { get; set; }
    public bool Loaded { get; set; }
    public bool PostLoaded { get; set; }
    public bool IsEditorOnly { get; set; }

    public virtual void Load() { }
    public virtual void Reload() { }
    public virtual void PostLoad() { }
    public virtual void Save(string path) { }
    public virtual void LoadMetadata(PropertySet meta) { }
    public virtual void SaveMetadata(PropertySet meta) { }
    public virtual void Import(string outputPath, PropertySet config, PropertySet meta) { }
    public virtual void Draw() { }
    public virtual void Clone(Document source) { }
    public virtual void OnUndoRedo() { }

    public void MarkModified()
    {
        IsModified = true;
    }

    public void MarkMetaModified()
    {
        IsMetaModified = true;
    }

    public virtual void Dispose () { }

    public void DrawBounds(Color color)
    {
        using (Graphics.PushState())
        {
            Graphics.SetTransform(Transform);
            Graphics.SetTexture(Workspace.WhiteTexture);
            Graphics.SetLayer(EditorLayer.Selection);
            Graphics.SetColor(color);
            Gizmos.DrawRect(Bounds, EditorStyle.Workspace.BoundsLineWidth, outside: true);
        }
    }
}
