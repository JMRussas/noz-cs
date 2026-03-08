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
        var worldPos = _getWorldPosition();
        var screenPos = Workspace.Camera.WorldToScreen(worldPos);
        var uiPos = UI.ScreenToUI(screenPos);
        uiPos.X -= EditorStyle.RenameTool.Root.Width.Value * 0.5f;
        uiPos.Y -= EditorStyle.RenameTool.Root.Height.Value * 0.5f;
        using (UI.BeginContainer(EditorStyle.RenameTool.Root with { Margin = EdgeInsets.TopLeft(uiPos.Y, uiPos.X) }))
        using (UI.BeginContainer(EditorStyle.RenameTool.Content))
        {
            _currentText = UI.TextInput(ElementId.TextBox, _currentText, EditorStyle.RenameTool.Text with { Scope = Scope });

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
