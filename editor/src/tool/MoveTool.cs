//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class MoveTool : Tool
{
    private Vector2 _deltaScale = Vector2.One;

    public override void Begin()
    {
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.IsSelected)
                continue;
            doc.SavedPosition = doc.Position;
        }
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft) || Input.WasButtonPressed(InputCode.KeyEnter))
        {
            Commit();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        // Axis constraints (Blender-style X/Y key locking)
        if (Input.WasButtonPressed(InputCode.KeyX))
            _deltaScale = _deltaScale.X > 0 ? new Vector2(1, 0) : Vector2.One;
        if (Input.WasButtonPressed(InputCode.KeyY))
            _deltaScale = _deltaScale.Y > 0 ? new Vector2(0, 1) : Vector2.One;

        var delta = Workspace.MouseWorldPosition - Workspace.DragWorldPosition;
        delta *= _deltaScale;

        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.IsSelected)
                continue;

            var newPos = doc.SavedPosition + delta;
            newPos = Input.IsCtrlDown() ? Grid.SnapToGrid(newPos) : Grid.SnapToPixelGrid(newPos);
            doc.Position = newPos;
        }
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
            {
                // X-axis constraint - draw horizontal line
                Render.Draw(bounds.X, Workspace.MouseWorldPosition.Y - thickness, bounds.Width, thickness * 2);
            }
            else
            {
                // Y-axis constraint - draw vertical line
                Render.Draw(Workspace.MouseWorldPosition.X - thickness, bounds.Y, thickness * 2, bounds.Height);
            }

            Render.PopState();
        }
    }

    public override void Cancel()
    {
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.IsSelected)
                continue;
            doc.Position = doc.SavedPosition;
        }
    }

    private void Commit()
    {
        foreach (var doc in DocumentManager.Documents)
        {
            if (!doc.IsSelected)
                continue;
            if (doc.Position != doc.SavedPosition)
                doc.MarkMetaModified();
        }
        Workspace.EndTool();
    }
}
