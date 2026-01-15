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
    private const byte TextBoxId = 201;
    private const byte ScrollableId = 202;
    private const int MaxFilteredCommands = 32;

    private static string? _prefix;
    private static string? _placeholder;
    private static bool _hideWhenEmpty;
    private static bool _enabled;
    private static string _text = string.Empty;
    private static string _lastFilterText = string.Empty;
    private static InputSet _input = null!;
    private static bool _popInput;
    private static readonly Command?[] _filteredCommands = new Command?[MaxFilteredCommands];
    private static int _filteredCount;
    private static int _selectedIndex;

    public static bool IsEnabled => _enabled;

    public static void Init()
    {
        _input = new InputSet("CommandPalette");
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

        Input.PushInputSet(_input);
        _popInput = true;

        UpdateFilteredCommands();
        UI.SetFocus(TextBoxId, EditorStyle.CanvasId.CommandPalette);
    }

    public static void End()
    {
        if (!_enabled)
            return;

        if (_popInput)
        {
            Input.PopInputSet();
            _popInput = false;
        }

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
            End();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyEnter))
        {
            ExecuteSelectedCommand();
            End();
            return;
        }

        if (Input.WasButtonPressed(InputCode.KeyUp))
        {
            _selectedIndex = Math.Max(0, _selectedIndex - 1);
            ScrollToSelection();
        }

        if (Input.WasButtonPressed(InputCode.KeyDown))
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

        UI.BeginCanvas(new CanvasStyle { Id = EditorStyle.CanvasId.CommandPalette });
        UI.BeginContainer(new ContainerStyle
        {
            Width = EditorStyle.CommandPalette.Width,
            Height = EditorStyle.CommandPalette.Height,
            Align = Align.Center,
            Padding = EdgeInsets.All(EditorStyle.CommandPalette.Padding),
            Color = EditorStyle.Overlay.BackgroundColor,
            Border = new BorderStyle 
            { 
                Radius = EditorStyle.CommandPalette.BorderRadius
            }
        });

        UI.BeginColumn(new ContainerStyle { Spacing = EditorStyle.CommandPalette.ListSpacing });

        UI.TextBox(ref _text, new TextBoxStyle
        {
            Height = EditorStyle.CommandPalette.InputHeight,
            FontSize = EditorStyle.CommandPalette.FontSize,
            BackgroundColor = EditorStyle.ButtonColor,
            TextColor = EditorStyle.Overlay.TextColor,
            Border = new BorderStyle { Radius = 6, Width = 1, Color = EditorStyle.ButtonColor },
            FocusBorder = new BorderStyle { Radius = 6, Width = 1, Color = EditorStyle.SelectionColor },
            Id = TextBoxId
        });

        if (_filteredCount > 0)
        {
            DrawCommandList();
        }

        UI.EndColumn();
        UI.EndContainer();
        UI.EndCanvas();
    }

    private static void DrawCommandList()
    {
        var maxListHeight = EditorStyle.CommandPalette.Height - EditorStyle.CommandPalette.InputHeight -
                            EditorStyle.CommandPalette.Padding * 2 - EditorStyle.CommandPalette.ListSpacing;
        var listHeight = Math.Min(_filteredCount * EditorStyle.CommandPalette.ItemHeight, maxListHeight);

        UI.BeginContainer(new ContainerStyle { Height = listHeight });
        UI.BeginScrollable(0, new ScrollableStyle { Id = ScrollableId });
        UI.BeginColumn();

        var selectedIndex = _selectedIndex;
        var execute = false;
        for (var i = 0; i < _filteredCount; i++)
        {
            var cmd = _filteredCommands[i];
            if (cmd == null) continue;

            var isSelected = i == selectedIndex;

            UI.BeginContainer(new ContainerStyle
            {
                Height = EditorStyle.CommandPalette.ItemHeight,
                Padding = EdgeInsets.LeftRight(EditorStyle.CommandPalette.ItemPadding),
                Color = isSelected ? EditorStyle.SelectionColor : Color.Red,
                Border = new BorderStyle { Radius = 8 },
                Id = (byte)(i + 10)
            });

            // UI.Label(cmd.Name, new LabelStyle
            // {
            //     FontSize = (int)EditorStyle.CommandPalette.ItemFontSize,
            //     Color = isSelected ? EditorStyle.SelectionTextColor : EditorStyle.Overlay.TextColor,
            //     Align = Align.CenterLeft
            // });

            // if (cmd.Key != InputCode.None)
            //     EditorUI.Shortcut(cmd);

            if (UI.WasClicked())
            {
                selectedIndex = i;
                execute = true;
            }

            UI.EndContainer();
        }

        _selectedIndex = selectedIndex;
        
        UI.EndColumn();
        UI.EndScrollable();
        UI.EndContainer();

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
        var maxListHeight = EditorStyle.CommandPalette.Height - EditorStyle.CommandPalette.InputHeight -
                            EditorStyle.CommandPalette.Padding * 2 - EditorStyle.CommandPalette.ListSpacing;
        var listHeight = Math.Min(_filteredCount * EditorStyle.CommandPalette.ItemHeight, maxListHeight);

        var itemTop = _selectedIndex * EditorStyle.CommandPalette.ItemHeight;
        var itemBottom = itemTop + EditorStyle.CommandPalette.ItemHeight;

        var currentScroll = UI.GetScrollOffset(ScrollableId, EditorStyle.CanvasId.CommandPalette);
        var viewTop = currentScroll;
        var viewBottom = currentScroll + listHeight;

        float newScroll = currentScroll;
        if (itemTop < viewTop)
            newScroll = itemTop;
        else if (itemBottom > viewBottom)
            newScroll = itemBottom - listHeight;

        if (newScroll != currentScroll)
            UI.SetScrollOffset(ScrollableId, newScroll, EditorStyle.CanvasId.CommandPalette);
    }
}
