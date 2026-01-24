//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class CommandPalette
{
    private static readonly ElementId CloseId = new(1);
    private static readonly ElementId SearchId = new(2);
    private static readonly ElementId CommandListId = new(3);
    private const int MaxFilteredCommands = 32;

    private static string _text = string.Empty;
    private static string _lastFilterText = string.Empty;
    private static readonly Command?[] _filteredCommands = new Command?[MaxFilteredCommands];
    private static int _filteredCount;
    private static int _selectedIndex;

    public static bool IsOpen { get; private set; }

    public static void Open()
    {
        if (IsOpen) return;

        IsOpen = true;
        _text = string.Empty;
        _lastFilterText = string.Empty;
        _selectedIndex = 0;
        _filteredCount = 0;

        UpdateFilteredCommands();
        UI.SetFocus(SearchId, EditorStyle.CanvasId.CommandPalette);
    }

    public static void Close()
    {
        if (!IsOpen) return;

        UI.ClearFocus();

        IsOpen = false;
        _text = string.Empty;
        _lastFilterText = string.Empty;
        _filteredCount = 0;
        _selectedIndex = 0;
    }

    public static void Update()
    {
        if (!IsOpen)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            Close();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            ExecuteSelectedCommand();
            Close();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyUp, allowRepeat: true))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyDown, allowRepeat: true))
        {
            _selectedIndex = Math.Min(_filteredCount - 1, _selectedIndex + 1);
            ScrollToSelection();
        }

        if (_text != _lastFilterText)
        {
            UpdateFilteredCommands();
            _lastFilterText = _text;
        }
    }

    public static void UpdateUI()
    {
        if (!IsOpen) return;

        using (UI.BeginCanvas(id:EditorStyle.CanvasId.CommandPalette))
        {
            using (UI.BeginContainer(id:CloseId))
                if (UI.WasPressed())
                    Close();

            using (UI.BeginColumn(EditorStyle.CommandPalette.Root))
            {
                using (UI.BeginRow(EditorStyle.Popup.Item with { Spacing = 4.0f }))
                {
                    using (UI.BeginContainer(EditorStyle.CommandPalette.Icon))
                        UI.Image(EditorAssets.Sprites.AssetIconShader);

                    using (UI.BeginFlex())
                        if (UI.TextBox(SearchId, style: EditorStyle.CommandPalette.SearchTextBox, placeholder: "Search..."))
                            _text = new string(UI.GetTextBoxText(EditorStyle.CanvasId.CommandPalette, SearchId));
                }

                UI.Container(EditorStyle.Popup.Separator);

                CommandList();
            }
        }
    }

    private static void CommandList()
    {
        var execute = false;

        using (UI.BeginContainer(EditorStyle.CommandPalette.CommandList))
        using (UI.BeginScrollable(CommandListId))
        using (UI.BeginColumn())
        {
            var selectedIndex = _selectedIndex;
            for (var i = 0; i < _filteredCount; i++)
            {
                var cmd = _filteredCommands[i];
                if (cmd == null) continue;

                var isSelected = i == selectedIndex;

                using (UI.BeginContainer(
                    id: (byte)(i + 10),
                    style: isSelected
                        ? EditorStyle.CommandPalette.SelectedCommand
                        : EditorStyle.CommandPalette.Command))
                {
                    using (UI.BeginRow())
                    {
                        using (UI.BeginContainer(EditorStyle.CommandPalette.Icon))
                            if (cmd.Icon != null)
                                UI.Image(cmd.Icon);

                        UI.Label(cmd.Name, style: EditorStyle.Control.Text);
                        UI.Flex();

                        if (cmd.Key != InputCode.None)
                            EditorUI.Shortcut(cmd, selected: isSelected);
                    }

                    if (UI.WasPressed())
                    {
                        selectedIndex = i;
                        execute = true;
                    }
                }
            }

            _selectedIndex = selectedIndex;
        }

        if (execute)
            ExecuteSelectedCommand();
    }

    private static void ExecuteSelectedCommand()
    {
        if (_selectedIndex < 0 || _selectedIndex >= _filteredCount)
            return;

        var cmd = _filteredCommands[_selectedIndex];
        cmd?.Handler();
    }

    private static void UpdateFilteredCommands()
    {
        _filteredCount = 0;
        _selectedIndex = 0;

        var filter = _text.Trim().ToLowerInvariant();

        foreach (var cmd in CommandManager.GetActiveCommands())
        {
            if (_filteredCount >= MaxFilteredCommands)
                break;

            if (string.IsNullOrEmpty(filter) ||
                cmd.Name.Contains(filter, StringComparison.OrdinalIgnoreCase) ||
                cmd.ShortName.Contains(filter, StringComparison.OrdinalIgnoreCase))
            {
                _filteredCommands[_filteredCount++] = cmd;
            }
        }
    }

    private static void ScrollToSelection()
    {
        var scrollableRect = UI.GetElementRect(EditorStyle.CanvasId.CommandPalette, CommandListId);

        if (scrollableRect.Height <= 0)
            return;

        var itemTop = _selectedIndex * EditorStyle.List.ItemHeight;
        var itemBottom = itemTop + EditorStyle.List.ItemHeight;

        var currentScroll = UI.GetScrollOffset(EditorStyle.CanvasId.CommandPalette, CommandListId);
        var viewTop = currentScroll;
        var viewBottom = currentScroll + scrollableRect.Height;
        var newScroll = currentScroll;

        if (itemTop < viewTop)
            newScroll = itemTop;
        else if (itemBottom > viewBottom)
            newScroll = itemBottom - scrollableRect.Height;

        if (newScroll != currentScroll)
            UI.SetScrollOffset(EditorStyle.CanvasId.CommandPalette, CommandListId, newScroll);
    }
}
