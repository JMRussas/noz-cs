//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class RotateTool(
    in Vector2 pivotWorld,
    in Vector2 pivotLocal,
    in Matrix3x2 invTransform,
    Action<float> update,
    Action<float> commit,
    Action cancel) : Tool
{
    private readonly Vector2 _pivotWorld = pivotWorld;
    private readonly Vector2 _pivotLocal = pivotLocal;
    private readonly Matrix3x2 _invTransform = invTransform;
    private readonly Action<float> _update = update;
    private readonly Action<float> _commit = commit;
    private readonly Action _cancel = cancel;

    private float _startAngle;

    public override void Begin()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        _startAngle = GetAngleFromPivot(mouseLocal);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.KeyEnter))
        {
            _commit(GetCurrentAngle());
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        _update(GetCurrentAngle());
    }

    private float GetAngleFromPivot(Vector2 localPos)
    {
        var dir = localPos - _pivotLocal;
        return MathF.Atan2(dir.Y, dir.X);
    }

    private float GetCurrentAngle()
    {
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var currentAngle = GetAngleFromPivot(mouseLocal);
        var angle = currentAngle - _startAngle;

        if (Input.IsShiftDown())
        {
            var snap = MathF.PI / 12f; // 15 degrees
            angle = MathF.Round(angle / snap) * snap;
        }

        return angle;
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetTransform(Matrix3x2.Identity);
            Graphics.SetColor(EditorStyle.SelectionColor);
            Gizmos.DrawRect(_pivotWorld, EditorStyle.Shape.AnchorSize * 1.5f, order: 10);
            Graphics.SetColor(EditorStyle.SelectionColor.WithAlpha(0.7f));
            Gizmos.DrawLine(_pivotWorld, Workspace.MouseWorldPosition, EditorStyle.Shape.SegmentWidth * 2, order: 9);
        }
    }

    public override void Cancel()
    {
        _cancel();
    }
}
