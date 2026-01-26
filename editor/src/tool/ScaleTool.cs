//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class ScaleTool : Tool
{
    private readonly Vector2 _pivot;
    private readonly Vector2 _origin;
    private readonly Action<Vector2> _update;
    private readonly Action<Vector2> _commit;
    private readonly Action _cancel;

    private Vector2 _startWorld;
    private Vector2 _scaleConstraint = Vector2.One;

    public ScaleTool(Vector2 pivot, Vector2 origin, Action<Vector2> update, Action<Vector2> commit, Action cancel)
    {
        _pivot = pivot;
        _origin = origin;
        _update = update;
        _commit = commit;
        _cancel = cancel;
    }

    public override void Begin()
    {
        _startWorld = Workspace.MouseWorldPosition;
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
            var finalScale = GetCurrentScale();
            _commit(finalScale);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyX))
            _scaleConstraint = _scaleConstraint.X > 0 && _scaleConstraint.Y == 0 ? Vector2.One : new Vector2(1, 0);
        if (Input.WasButtonPressed(InputCode.KeyY))
            _scaleConstraint = _scaleConstraint.Y > 0 && _scaleConstraint.X == 0 ? Vector2.One : new Vector2(0, 1);

        var scale = GetCurrentScale();
        _update(scale);
    }

    private Vector2 GetCurrentPivot() => Input.IsShiftDown() ? _origin : _pivot;

    private Vector2 GetCurrentScale()
    {
        var pivot = GetCurrentPivot();
        var startDistance = Vector2.Distance(pivot, _startWorld);
        if (startDistance < 0.001f)
            startDistance = 1f;
        var currentDistance = Vector2.Distance(pivot, Workspace.MouseWorldPosition);
        var ratio = currentDistance / startDistance;

        var scale = new Vector2(ratio, ratio);

        if (_scaleConstraint.X == 0)
            scale.X = 1f;
        if (_scaleConstraint.Y == 0)
            scale.Y = 1f;

        return scale;
    }

    public override void Draw()
    {
        using var _ = Gizmos.PushState(EditorLayer.Tool);

        var pivot = GetCurrentPivot();

        Gizmos.SetColor(EditorStyle.Tool.PointColor);
        Gizmos.DrawCircle(pivot, EditorStyle.Tool.PointSize, order: 10);

        var thickness = EditorStyle.Workspace.DocumentBoundsLineWidth / Workspace.Zoom;

        if (_scaleConstraint.X == 0 || _scaleConstraint.Y == 0)
        {
            Graphics.SetColor(EditorStyle.SelectionColor.WithAlpha(0.5f));

            var camera = Workspace.Camera;
            var bounds = camera.WorldBounds;

            if (_scaleConstraint.X > 0)
                Graphics.Draw(bounds.X, pivot.Y - thickness, bounds.Width, thickness * 2);
            if (_scaleConstraint.Y > 0)
                Graphics.Draw(pivot.X - thickness, bounds.Y, thickness * 2, bounds.Height);
        }

        Gizmos.SetColor(EditorStyle.Tool.LineColor);
        Gizmos.DrawDashedLine(pivot, Workspace.MouseWorldPosition);
    }

    public override void Cancel()
    {
        _cancel();
    }
}
