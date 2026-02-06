//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class RenameTool(
    string originalName,
    Func<Vector2> getWorldPosition,
    Action<string> commit) : Tool
{
    [ElementId("TextBox")]
    private static partial class ElementId { }

    private readonly string _originalName = originalName;
    private readonly Func<Vector2> _getWorldPosition = getWorldPosition;
    private readonly Action<string> _commit = commit;
    private string _currentText = originalName;
    private bool _firstFrame = true;

    public object? Target { get; init; }

    public override void Begin()
    {
        base.Begin();
        _firstFrame = true;
        UI.SetFocus(ElementId.TextBox);
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
            var textBoxRect = UI.GetElementRect(ElementId.TextBox);
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
        var worldPos = _getWorldPosition();
        var screenPos = Workspace.Camera.WorldToScreen(worldPos);
        var uiPos = UI.ScreenToUI(screenPos);
        uiPos.X -= EditorStyle.RenameTool.Root.Width.Value * 0.5f;
        uiPos.Y -= EditorStyle.RenameTool.Root.Height.Value * 0.5f;
        using (UI.BeginContainer(ElementId.TextBox, EditorStyle.RenameTool.Root with { Margin = EdgeInsets.TopLeft(uiPos.Y, uiPos.X) }))
        using (UI.BeginContainer(EditorStyle.RenameTool.Content))
        {
            if (_firstFrame)
            {
                UI.SetTextBoxText(ElementId.TextBox, _originalName, selectAll: true);
                _firstFrame = false;
            }

            if (UI.TextBox(ElementId.TextBox, EditorStyle.RenameTool.Text with { Scope = Scope }))
                _currentText = new string(UI.GetTextBoxText(ElementId.TextBox));
        }
    }

    private void Commit()
    {
        _currentText = new string(UI.GetTextBoxText(ElementId.TextBox));

        if (!string.IsNullOrWhiteSpace(_currentText) && _currentText != _originalName)
            _commit(_currentText);
    }

    public override void Dispose()
    {
        base.Dispose();
        UI.ClearFocus();
    }
}
