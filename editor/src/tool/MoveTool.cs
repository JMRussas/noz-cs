//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class MoveTool(Action<Vector2> update, Action<Vector2> commit, Action cancel) : Tool
{
    private readonly Action<Vector2> _update = update;
    private readonly Action<Vector2> _commit = commit;
    private readonly Action _cancel = cancel;

    private Vector2 _startWorld;
    private Vector2 _deltaScale = Vector2.One;
    private InputScope _inputScope;

    public override void Begin()
    {
        _startWorld = Workspace.MouseWorldPosition;
        _inputScope = Input.PushScope();
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, _inputScope) ||
            Input.WasButtonPressed(InputCode.MouseRight, _inputScope))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft, _inputScope) ||
            Input.WasButtonPressed(InputCode.KeyEnter, _inputScope))
        {
            var delta = Workspace.MouseWorldPosition - _startWorld;
            delta *= _deltaScale;
            _commit(delta);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyX, _inputScope))
            _deltaScale = _deltaScale.X > 0 ? new Vector2(1, 0) : Vector2.One;
        if (Input.WasButtonPressed(InputCode.KeyY, _inputScope))
            _deltaScale = _deltaScale.Y > 0 ? new Vector2(0, 1) : Vector2.One;

        var updateDelta = Workspace.MouseWorldPosition - _startWorld;
        updateDelta *= _deltaScale;
        _update(updateDelta);
    }

    public override void Draw()
    {
        if (_deltaScale.X == 0 || _deltaScale.Y == 0)
        {
            Graphics.PushState();
            Graphics.SetLayer(EditorLayer.Tool);
            Graphics.SetColor(EditorStyle.SelectionColor.WithAlpha(0.5f));

            var camera = Workspace.Camera;
            var bounds = camera.WorldBounds;
            var thickness = EditorStyle.Workspace.DocumentBoundsLineWidth / Workspace.Zoom;

            if (_deltaScale.X > 0)
                Graphics.Draw(bounds.X, Workspace.MouseWorldPosition.Y - thickness, bounds.Width, thickness * 2);
            else
                Graphics.Draw(Workspace.MouseWorldPosition.X - thickness, bounds.Y, thickness * 2, bounds.Height);

            Graphics.PopState();
        }
    }

    public override void Cancel()
    {
        _cancel();
        base.Cancel();
    }

    public override void Dispose()
    {
        Input.PopScope(_inputScope);
        base.Dispose();
    }
}
