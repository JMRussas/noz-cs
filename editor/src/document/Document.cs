//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract class Document : IDisposable
{
    public DocumentDef Def { get; internal set; } = null!;
    public string Name { get; set; } = "";
    public string Path { get; set; } = "";
    public int SourcePathIndex { get; set; } = -1;
    public virtual bool IsPlaying { get; } = false;
    public virtual bool CanPlay => false;

    public Vector2 Position { get; set; }
    public Vector2 SavedPosition { get; set; }
    public Rect Bounds { get; set; } = new(-0.5f, -0.5f, 1f, 1f);

    public Matrix3x2 Transform => Matrix3x2.CreateTranslation(Position);
    
    public bool IsVisible { get; set; } = true;
    public bool IsSelected { get; set; }
    public bool IsEditing { get; set; }
    public bool IsModified { get; private set; }
    public bool IsMetaModified { get; private set; }
    public bool IsClipped { get; set; }
    public bool Loaded { get; set; }
    public bool PostLoaded { get; set; }
    public bool IsEditorOnly { get; set; }

    public virtual void Load() { }
    public virtual void Save(StreamWriter sw) { }
    public virtual void Reload() { }
    public virtual void PostLoad() { }
    public virtual void LoadMetadata(PropertySet meta) { }
    public virtual void SaveMetadata(PropertySet meta) { }
    public virtual void Import(string outputPath, PropertySet config, PropertySet meta) { }
    public virtual void Draw() { }
    public virtual void Clone(Document source) { }
    public virtual void OnUndoRedo() { }

    public virtual void Play() { }
    public virtual void Stop() { }

    public void TogglePlay()
    {
        if (!CanPlay) return;
        if (IsPlaying)
            Stop();
        else
            Play();
    }

    public void SaveMetadata()
    {
        var metaPath = Path + ".meta";
        var props = PropertySet.LoadFile(metaPath) ?? new PropertySet();

        props.SetVec2("editor", "position", Position);
        SaveMetadata(props);
        props.Save(metaPath);
        IsMetaModified = false;
    }

    public void LoadMetadata() 
    {
        var props = PropertySet.LoadFile(Path + ".meta");
        if (props == null)
            return;

        Position = props.GetVector2("editor", "position", default);
        LoadMetadata(props);
        IsMetaModified = false;
    }

    public void Save() 
    {
        using var sw = new StreamWriter(Path);
        Save(sw);
        IsModified = false;
    }

    public void MarkModified()
    {
        IsModified = true;
    }

    public void MarkMetaModified()
    {
        IsMetaModified = true;
    }

    public virtual void Dispose () { }

    public void DrawBounds(bool selected=false)
    {
        using (Gizmos.PushState(selected ? EditorLayer.Selection : EditorLayer.Document))
        {
            Graphics.SetTransform(Transform);
            Graphics.SetColor(selected
                ? EditorStyle.Workspace.SelectedDocumentBoundsColor
                : EditorStyle.Workspace.DocumentBoundsColor);
            Gizmos.DrawRect(Bounds, EditorStyle.Workspace.DocumentBoundsLineWidth, outside: true);
        }
    }
}
