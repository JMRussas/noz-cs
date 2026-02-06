//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class CommandPalette
{
    private const int MaxFilteredCommands = 32;

    [ElementId("Close")]
    [ElementId("Search")]
    [ElementId("CommandList")]
    [ElementId("Command", MaxFilteredCommands)]
    private static partial class ElementId { }

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
        UI.SetFocus(ElementId.Search);
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

        using (UI.BeginContainer(ElementId.Close))
            if (UI.WasPressed())
                Close();

        using (UI.BeginColumn(EditorStyle.CommandPalette.Root))
        {
            using (UI.BeginRow(EditorStyle.Popup.Item with { Spacing = 4.0f }))
            {
                using (UI.BeginContainer(EditorStyle.Control.IconContainer))
                    UI.Image(EditorAssets.Sprites.IconSearch, EditorStyle.Control.Icon);
                   
                using (UI.BeginFlex())
                    if (UI.TextBox(ElementId.Search, style: EditorStyle.CommandPalette.SearchTextBox, placeholder: "Search..."))
                        _text = new string(UI.GetTextBoxText(ElementId.Search));
            }

            UI.Container(EditorStyle.Popup.Separator);

            CommandList();
        }
    }

    private static void CommandList()
    {
        var execute = false;

        using (UI.BeginContainer(EditorStyle.CommandPalette.CommandList))
        using (UI.BeginScrollable(ElementId.CommandList))
        using (UI.BeginColumn())
        {
            var selectedIndex = _selectedIndex;
            for (var i = 0; i < _filteredCount; i++)
            {
                var cmd = _filteredCommands[i];
                if (cmd == null) continue;

                var isSelected = i == selectedIndex;

                using (UI.BeginContainer(
                    id: ElementId.Command + i,
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

            if (execute)
                ExecuteSelectedCommand();
        }
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
            if (cmd == null) continue;

            if (_filteredCount >= MaxFilteredCommands)
                break;

            if (string.IsNullOrEmpty(filter) || cmd.Name.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _filteredCommands[_filteredCount++] = cmd;
        }
        
        _filteredCommands.AsSpan(0, _filteredCount).Sort((a, b) => StringComparer.OrdinalIgnoreCase.Compare(a!.Name, b!.Name));
    }

    private static void ScrollToSelection()
    {
        var scrollableRect = UI.GetElementRect(ElementId.CommandList);

        if (scrollableRect.Height <= 0)
            return;

        var itemTop = _selectedIndex * EditorStyle.List.ItemHeight;
        var itemBottom = itemTop + EditorStyle.List.ItemHeight;

        var currentScroll = UI.GetScrollOffset(ElementId.CommandList);
        var viewTop = currentScroll;
        var viewBottom = currentScroll + scrollableRect.Height;
        var newScroll = currentScroll;

        if (itemTop < viewTop)
            newScroll = itemTop;
        else if (itemBottom > viewBottom)
            newScroll = itemBottom - scrollableRect.Height;

        if (newScroll != currentScroll)
            UI.SetScrollOffset(ElementId.CommandList, newScroll);
    }
}
