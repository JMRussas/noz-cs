//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class RotateTool(
    in Vector2 pivotWorld,
    in Vector2 pivotLocal,
    in Vector2 originWorld,
    in Vector2 originLocal,
    in Matrix3x2 invTransform,
    Action<float> update,
    Action<float> commit,
    Action cancel) : Tool
{
    private readonly Vector2 _pivotWorld = pivotWorld;
    private readonly Vector2 _pivotLocal = pivotLocal;
    private readonly Vector2 _originWorld = originWorld;
    private readonly Vector2 _originLocal = originLocal;
    private readonly Matrix3x2 _invTransform = invTransform;
    private readonly Action<float> _update = update;
    private readonly Action<float> _commit = commit;
    private readonly Action _cancel = cancel;

    private Vector2 _startMouseLocal;

    public override void Begin()
    {
        _startMouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
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

    private static float GetAngleFromPivot(Vector2 localPos, Vector2 pivotLocal)
    {
        var dir = localPos - pivotLocal;
        return MathF.Atan2(dir.Y, dir.X);
    }

    private Vector2 GetCurrentPivotLocal() => Input.IsShiftDown() ? _originLocal : _pivotLocal;
    private Vector2 GetCurrentPivotWorld() => Input.IsShiftDown() ? _originWorld : _pivotWorld;

    private float GetCurrentAngle()
    {
        var pivotLocal = GetCurrentPivotLocal();
        var startAngle = GetAngleFromPivot(_startMouseLocal, pivotLocal);
        var mouseLocal = Vector2.Transform(Workspace.MouseWorldPosition, _invTransform);
        var currentAngle = GetAngleFromPivot(mouseLocal, pivotLocal);
        var angle = currentAngle - startAngle;

        if (Input.IsCtrlDown())
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
            var pivotWorld = GetCurrentPivotWorld();
            Graphics.SetTransform(Matrix3x2.Identity);
            Graphics.SetColor(EditorStyle.Tool.PointColor);
            Gizmos.DrawCircle(pivotWorld, EditorStyle.Tool.PointSize, order: 2);
            Graphics.SetColor(EditorStyle.Tool.LineColor);
            Gizmos.DrawDashedLine(pivotWorld, Workspace.MouseWorldPosition, order: 1);
        }
    }

    public override void Cancel()
    {
        _cancel();
    }
}
