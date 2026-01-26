//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class RenameTool : Tool
{
    private static readonly ElementId TextBoxId = new(1);

    private readonly Document _document;
    private readonly string _originalName;
    private string _currentText;
    private bool _firstFrame = true;
    private InputScope _scope;

    public Document Document => _document;

    public RenameTool(Document document)
    {
        _document = document;
        _originalName = document.Name;
        _currentText = document.Name;
    }

    public override void Begin()
    {
        _firstFrame = true;
        _scope = Input.PushScope();
        UI.SetFocus(TextBoxId, EditorStyle.CanvasId.Workspace);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
        {
            Input.ConsumeButton(InputCode.KeyEnter);
            Commit();
            Workspace.EndTool();
            return;
        }

        // Click outside the TextBox commits the rename (skip on first frame)
        if (!_firstFrame && Input.WasButtonPressed(InputCode.MouseLeft))
        {
            var textBoxRect = UI.GetElementRect(EditorStyle.CanvasId.Workspace, 99);
            var mousePos = UI.ScreenToUI(Input.MousePosition);
            if (textBoxRect.Width > 0 && !textBoxRect.Contains(mousePos))
            {
                Input.ConsumeButton(InputCode.MouseLeft);
                Commit();
                Workspace.EndTool();
                return;
            }
        }
    }

    public override void UpdateUI()
    {
        // name world position
        // todo: move to a workspace function (pass a world position and it returns the name center position)
        var padding = EditorStyle.Workspace.NamePadding / Workspace.Zoom;
        var bounds = _document.Bounds.Translate(_document.Position);
        var worldX = bounds.Center.X;
        var worldY = bounds.Bottom + padding;

        // Convert world to screen position
        var screenPos = Workspace.Camera.WorldToScreen(new Vector2(worldX, worldY));
        var uiPos = UI.ScreenToUI(screenPos);
        uiPos.X -= EditorStyle.RenameTool.Root.Width.Value * 0.5f;
        uiPos.Y -= EditorStyle.RenameTool.Root.Height.Value * 0.5f;
        using (UI.BeginCanvas(id: EditorStyle.CanvasId.Workspace))
        using (UI.BeginContainer(99, EditorStyle.RenameTool.Root with { Margin = EdgeInsets.TopLeft(uiPos.Y, uiPos.X) }))
        using (UI.BeginContainer(EditorStyle.RenameTool.TextContainer))
        {
            if (_firstFrame)
            {
                UI.SetTextBoxText(EditorStyle.CanvasId.Workspace, TextBoxId, _originalName, selectAll: true);
                _firstFrame = false;
            }

            if (UI.TextBox(TextBoxId, EditorStyle.RenameTool.Text with { Scope = _scope }))
                _currentText = new string(UI.GetTextBoxText(EditorStyle.CanvasId.Workspace, TextBoxId));
        }
    }

    private void Commit()
    {
        _currentText = new string(UI.GetTextBoxText(EditorStyle.CanvasId.Workspace, TextBoxId));

        if (!string.IsNullOrWhiteSpace(_currentText) && _currentText != _originalName)
        {
            if (DocumentManager.Rename(_document, _currentText))
                Notifications.Add($"renamed to '{_currentText}'");
            else
                Notifications.AddError($"renamed failed");
        }
    }

    public override void Cancel()
    {
        // Name stays unchanged
    }

    public override void Dispose()
    {
        Input.PopScope(_scope);
        UI.ClearFocus();
    }
}
