//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BoxSelectTool(Action<Rect> callback) : Tool
{
    private readonly Action<Rect> _callback = callback;
    private Rect _selection;

    public override void Update()
    {
        if (!Workspace.IsDragging)
        {
            Commit();
            return;
        }

        var p0 = Workspace.DragWorldPosition;
        var p1 = Workspace.MouseWorldPosition;
        var min = Vector2.Min(p0, p1);
        var max = Vector2.Max(p0, p1);
        _selection = Rect.FromMinMax(min, max);

        if (Input.WasButtonReleased(InputCode.MouseLeft, Scope))
            Commit();
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetColor(EditorStyle.BoxSelect.FillColor);
            Graphics.Draw(_selection);
            Graphics.SetColor(EditorStyle.BoxSelect.LineColor);
            Gizmos.DrawRect(_selection, EditorStyle.BoxSelect.LineWidth);
        }
    }

    private void Commit()
    {
        Input.ConsumeButton(InputCode.MouseLeft);
        _callback(_selection);
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
    }
}
