//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal class SpriteSelectTool : Tool
{
    private readonly Action<SpriteDocument> _commit;
    private SpriteDocument? _hoverSprite;

    public SpriteSelectTool(Action<SpriteDocument> commit) => _commit = commit;

    public override void Begin()
    {
        Cursor.Set(SystemCursor.Crosshair);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) ||
            Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        UpdateHover();

        if (Input.WasButtonPressed(InputCode.MouseLeft))
        {
            if (_hoverSprite != null)
            {
                _commit(_hoverSprite);
                Input.ConsumeButton(InputCode.MouseLeft);
            }
            Workspace.EndTool();
        }
    }

    private void UpdateHover()
    {
        _hoverSprite = null;

        var mousePos = Workspace.MouseWorldPosition;

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not SpriteDocument sprite)
                continue;

            if (!doc.Loaded || !doc.PostLoaded || doc.IsClipped)
                continue;

            var worldBounds = new Rect(
                sprite.Bounds.X + sprite.Position.X,
                sprite.Bounds.Y + sprite.Position.Y,
                sprite.Bounds.Width,
                sprite.Bounds.Height);

            if (worldBounds.Contains(mousePos))
            {
                _hoverSprite = sprite;
                return;
            }
        }
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            if (_hoverSprite != null)
            {
                var worldBounds = new Rect(
                    _hoverSprite.Bounds.X + _hoverSprite.Position.X,
                    _hoverSprite.Bounds.Y + _hoverSprite.Position.Y,
                    _hoverSprite.Bounds.Width,
                    _hoverSprite.Bounds.Height);

                Graphics.SetColor(EditorStyle.SelectionColor);
                Gizmos.DrawRect(worldBounds, EditorStyle.Workspace.DocumentBoundsLineWidth, outside: true);
            }
            else
            {
                Graphics.SetColor(EditorStyle.SelectionColor.WithAlpha(0.5f));
                Gizmos.DrawRect(Workspace.MouseWorldPosition, EditorStyle.Shape.AnchorSize * 2f);
            }
        }
    }
}
