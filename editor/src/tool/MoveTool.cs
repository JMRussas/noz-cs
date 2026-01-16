//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class MoveTool : Tool
{
    private readonly Action<Vector2> _update;
    private readonly Action<Vector2> _commit;
    private readonly Action _cancel;

    private Vector2 _startWorld;
    private Vector2 _deltaScale = Vector2.One;

    public MoveTool(Action<Vector2> update, Action<Vector2> commit, Action cancel)
    {
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
            var delta = Workspace.MouseWorldPosition - _startWorld;
            delta *= _deltaScale;
            _commit(delta);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyX))
            _deltaScale = _deltaScale.X > 0 ? new Vector2(1, 0) : Vector2.One;
        if (Input.WasButtonPressed(InputCode.KeyY))
            _deltaScale = _deltaScale.Y > 0 ? new Vector2(0, 1) : Vector2.One;

        var updateDelta = Workspace.MouseWorldPosition - _startWorld;
        updateDelta *= _deltaScale;
        _update(updateDelta);
    }

    public override void Draw()
    {
        if (_deltaScale.X == 0 || _deltaScale.Y == 0)
        {
            Render.PushState();
            Render.SetLayer(EditorLayer.Tool);
            Render.SetColor(EditorStyle.SelectionColor.WithAlpha(0.5f));

            var camera = Workspace.Camera;
            var bounds = camera.WorldBounds;
            var thickness = EditorStyle.Workspace.BoundsLineWidth / Workspace.Zoom;

            if (_deltaScale.X > 0)
                Render.Draw(bounds.X, Workspace.MouseWorldPosition.Y - thickness, bounds.Width, thickness * 2);
            else
                Render.Draw(Workspace.MouseWorldPosition.X - thickness, bounds.Y, thickness * 2, bounds.Height);

            Render.PopState();
        }
    }

    public override void Cancel()
    {
        _cancel();
    }
}
