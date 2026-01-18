//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class Command
{
    public required string Name { get; init; }
    public required string ShortName { get; init; }
    public required Action Handler { get; init; }

    public InputCode Key { get; init; } = InputCode.None;
    public bool Ctrl { get; init; }
    public bool Alt { get; init; }
    public bool Shift { get; init; }
}

public readonly struct ParsedCommand
{
    public const int MaxArgs = 4;
    public const int MaxArgSize = 128;

    public readonly string Name;
    public readonly string[] Args;

    public ParsedCommand(string name, string[] args)
    {
        Name = name;
        Args = args;
    }

    public string GetArg(int index) => index < Args.Length ? Args[index] : string.Empty;
    public int ArgCount => Args.Length;
}

public struct CommandInputOptions
{
    public string? Prefix;
    public string? Placeholder;
    public string? InitialText;
    public bool HideWhenEmpty;
}

public static class CommandPalette
{
    private const byte SearchId = 201;
    private const byte CommandListId = 202;
    private const int MaxFilteredCommands = 32;

    private static string? _prefix;
    private static string? _placeholder;
    private static bool _hideWhenEmpty;
    private static bool _enabled;
    private static string _text = string.Empty;
    private static string _lastFilterText = string.Empty;
    private static readonly Command?[] _filteredCommands = new Command?[MaxFilteredCommands];
    private static int _filteredCount;
    private static int _selectedIndex;

    public static bool IsEnabled => _enabled;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
    }

    public static void Begin(CommandInputOptions options)
    {
        _enabled = true;
        _prefix = options.Prefix;
        _placeholder = options.Placeholder;
        _hideWhenEmpty = options.HideWhenEmpty;
        _text = options.InitialText ?? string.Empty;
        _lastFilterText = string.Empty;
        _selectedIndex = 0;
        _filteredCount = 0;

        UpdateFilteredCommands();
        UI.SetFocus(SearchId, EditorStyle.CanvasId.CommandPalette);
    }

    public static void End()
    {
        if (!_enabled)
            return;

        UI.ClearFocus();

        _enabled = false;
        _prefix = null;
        _placeholder = null;
        _text = string.Empty;
        _lastFilterText = string.Empty;
        _filteredCount = 0;
        _selectedIndex = 0;
    }

    public static void Update()
    {
        if (!_enabled)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);
            End();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            ExecuteSelectedCommand();
            End();
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
        if (!_enabled)
            return;

        if (_hideWhenEmpty && string.IsNullOrEmpty(_text))
            return;

        using (UI.BeginCanvas(id:EditorStyle.CanvasId.CommandPalette))
        using (UI.BeginContainer(EditorStyle.CommandPalette.RootContainer))
        using (UI.BeginColumn(EditorStyle.CommandPalette.ListColumn))
        {
            using (UI.BeginContainer(EditorStyle.CommandPalette.SearchContainer))
                UI.TextBox(ref _text, style: EditorStyle.CommandPalette.SearchTextBox, id: SearchId, placeholder: "Search...");

            EditorUI.PopupSeparator(16);

            using (UI.BeginExpanded())
                CommandList();
        }
    }

    private static void CommandList()
    {
        //var maxListHeight = EditorStyle.CommandPalette.Height - EditorStyle.CommandPalette.InputHeight -
        //                    EditorStyle.CommandPalette.Padding * 2 - EditorStyle.CommandPalette.ListSpacing;
        //var listHeight = Math.Min(_filteredCount * EditorStyle.CommandPalette.ItemHeight, maxListHeight);
        var execute = false;

        using (UI.BeginContainer())using (UI.BeginScrollable(offset: 0, id: CommandListId))
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
                        ? EditorStyle.CommandPalette.SelectedCommandContainer
                        : EditorStyle.CommandPalette.CommandContainer))
                {
                    using (UI.BeginRow())
                    {
                        using (UI.BeginContainer(EditorStyle.CommandPalette.CommandIconContainer))
                            ;

                        UI.Label(cmd.Name, style: isSelected
                            ? EditorStyle.CommandPalette.SelectedCommandText
                            : EditorStyle.CommandPalette.CommandText);

                        UI.Expanded();

                        if (cmd.Key != InputCode.None)
                            EditorUI.Shortcut(cmd, selected: isSelected);
                    }

                    if (UI.WasClicked())
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
        var scrollableRect = UI.GetElementRect(CommandListId, EditorStyle.CanvasId.CommandPalette);

        if (scrollableRect.Height <= 0)
            return;

        var itemTop = _selectedIndex * EditorStyle.List.ItemHeight;
        var itemBottom = itemTop + EditorStyle.List.ItemHeight;

        var currentScroll = UI.GetScrollOffset(CommandListId, EditorStyle.CanvasId.CommandPalette);
        var viewTop = currentScroll;
        var viewBottom = currentScroll + scrollableRect.Height;
        var newScroll = currentScroll;

        if (itemTop < viewTop)
            newScroll = itemTop;
        else if (itemBottom > viewBottom)
            newScroll = itemBottom - scrollableRect.Height;

        if (newScroll != currentScroll)
            UI.SetScrollOffset(CommandListId, newScroll, EditorStyle.CanvasId.CommandPalette);
    }
}
