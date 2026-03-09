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
    private static partial class ElementId 
    {
        public static partial WidgetId TextBox { get; }
    }

    private readonly string _originalName = originalName;
    private readonly Func<Vector2> _getWorldPosition = getWorldPosition;
    private readonly Action<string> _commit = commit;
    private string _currentText = originalName;

    public object? Target { get; init; }

    public override void Begin()
    {
        base.Begin();
        UI.SetHot(ElementId.TextBox);
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
    }

    public override void UpdateUI()
    {
        var textStyle = EditorStyle.RenameTool.Text with { Scope = Scope };
        var font = textStyle.Font ?? UI.DefaultFont;
        var textSize = TextRender.Measure(_currentText.AsSpan(), font, textStyle.FontSize);
        var padding = EditorStyle.RenameTool.Content.Padding;
        var textInputHeight = textStyle.Height.IsFixed ? textStyle.Height.Value : textStyle.FontSize * 1.8f;

        var worldPos = _getWorldPosition();
        var screenPos = Workspace.Camera.WorldToScreen(worldPos);
        var uiPos = UI.ScreenToUI(screenPos);

        // Offset so text center aligns with the world position
        var border = EditorStyle.RenameTool.Content.BorderWidth;
        uiPos.X -= textSize.X * 0.5f + padding.L + border;
        uiPos.Y -= textInputHeight * 0.5f + padding.T + border;

        using (UI.BeginContainer(EditorStyle.RenameTool.Content with { AlignX = Align.Min, AlignY = Align.Min, Margin = EdgeInsets.TopLeft(uiPos.Y, uiPos.X) }))
        {
            _currentText = UI.TextInput(ElementId.TextBox, _currentText, textStyle);

            if (UI.HotEnter())
                UI.SetElementText(ElementId.TextBox, _originalName, selectAll: true);

            if (UI.HotExit())
            {
                Commit();
                Workspace.EndTool();
            }
        }
    }

    private void Commit()
    {
        if (!string.IsNullOrWhiteSpace(_currentText) && _currentText != _originalName)
            _commit(_currentText);
    }

    public override void Dispose()
    {
        base.Dispose();
        UI.ClearHot();
    }
}
