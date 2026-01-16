//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BoxSelectTool : Tool
{
    private readonly Action<Rect> _callback;
    private Rect _selection;

    public BoxSelectTool(Action<Rect> callback)
    {
        _callback = callback;
    }

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
    }

    public override void Draw()
    {
        Render.PushState();
        Render.SetLayer(EditorLayer.Tool);
        Render.SetColor(EditorStyle.BoxSelect.FillColor);
        Render.Draw(_selection);
        Render.SetColor(EditorStyle.BoxSelect.LineColor);
        Gizmos.DrawRect(_selection, EditorStyle.BoxSelect.LineWidth);
        Render.PopState();
    }

    private void Commit()
    {
        _callback(_selection);
        Workspace.EndTool();
        Input.ConsumeButton(InputCode.MouseLeft);
    }
}
