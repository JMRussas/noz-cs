//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public struct PopupMenuItem
{
    public string? Label;
    public Action? Handler;
    public int Level;
    public InputCode Key;
    public bool Ctrl;
    public bool Alt;
    public bool Shift;
    public Sprite? Icon;
    public Func<bool>? GetEnabled;
    public Func<bool>? GetChecked;
    public bool ShowChecked;
    public bool ShowIcons;

    public readonly bool IsEnabled => GetEnabled?.Invoke() ?? true;
    public readonly bool IsChecked => GetChecked?.Invoke() ?? false;

    public static PopupMenuItem Item(string label, Action handler, InputCode key = InputCode.None, bool ctrl = false, bool alt = false, bool shift = false, int level = 0, Func<bool>? enabled = null, Func<bool>? isChecked = null, Sprite? icon = null) =>
        new() { Label = label, Handler = handler, Level = level, Key = key, Ctrl = ctrl, Alt = alt, Shift = shift, GetEnabled = enabled, GetChecked = isChecked, Icon = icon, ShowChecked = true };

    public static PopupMenuItem Submenu(string label, int level = 0, bool showChecked = false, bool showIcons = true, Func<bool>? isChecked = null) =>
        new() { Label = label, Handler = null, Level = level, ShowChecked = showChecked, ShowIcons = showIcons, GetChecked = isChecked };

    public static PopupMenuItem Separator(int level = 0) =>
        new() { Label = null, Handler = null, Level = level };

    public static PopupMenuItem FromCommand(Command cmd, int level = 0, Func<bool>? enabled = null, Func<bool>? isChecked = null) =>
        new() { Label = cmd.Name, Handler = cmd.Handler, Level = level, Key = cmd.Key, Ctrl = cmd.Ctrl, Alt = cmd.Alt, Shift = cmd.Shift, GetEnabled = enabled, GetChecked = isChecked, Icon = cmd.Icon, ShowChecked = true };
}

public static partial class PopupMenu
{
    private const int MaxItems = 64;
    private const int MaxSubmenuDepth = 8;

    private struct LevelState
    {
        public int OpenSubmenu;
        public bool ShowChecked;
        public bool ShowIcons;
    }

    [ElementId("Menu", count: MaxSubmenuDepth)]
    [ElementId("Item", count: MaxItems)]
    private static partial class ElementId { }

    private static int _id;
    private static bool _visible;
    private static Vector2 _worldPosition;
    private static PopupMenuItem[] _items = new PopupMenuItem[MaxItems];
    private static int _itemCount;
    private static string? _title;
    private static InputScope _scope;
    private static PopupStyle _popupStyle;
    private static readonly LevelState[] _levels = new LevelState[MaxSubmenuDepth];

    public static bool IsVisible => _visible;
    public static Vector2 WorldPosition => _worldPosition;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
        _visible = false;
        _itemCount = 0;
        _title = null;
    }

    public static void Open(
        int id,
        ReadOnlySpan<PopupMenuItem> items,
        string? title = null) => Open(
            id,
            items,
            new PopupStyle() { 
                AnchorRect = new Rect(UI.ScreenToUI(Input.MousePosition), Vector2.Zero)
            },
            title);

    public static void Open(
        int id,
        ReadOnlySpan<PopupMenuItem> items,
        PopupStyle style,
        string? title = null)
    {
        _itemCount = Math.Min(items.Length, MaxItems);
        for (var i = 0; i < _itemCount; i++)
            _items[i] = items[i];

        _id = id;
        _title = title;
        _popupStyle = style;
        _worldPosition = Workspace.MouseWorldPosition;
        _visible = true;
        for (var i = 0; i < MaxSubmenuDepth; i++)
            _levels[i] = new LevelState { OpenSubmenu = -1, ShowChecked = true, ShowIcons = true };
        _levels[0].ShowChecked = style.ShowChecked;
        _levels[0].ShowIcons = style.ShowIcons;
        UI.ClearFocus();

        _scope = Input.PushScope();
    }

    public static void Close()
    {
        _visible = false;
        _itemCount = 0;
        _title = null;
        _id = 0;
        UI.ClearFocus();
        Input.PopScope(_scope);
    }

    public static bool IsOpen(int id) => _visible && _id == id;

    public static void Update()
    {
        if (!_visible)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape, _scope))
        {
            Input.ConsumeButton(InputCode.KeyEscape);

            // Close deepest open submenu first, or close menu if none open
            var closedSubmenu = false;
            for (var level = MaxSubmenuDepth - 1; level >= 0; level--)
            {
                if (_levels[level].OpenSubmenu >= 0)
                {
                    _levels[level].OpenSubmenu = -1;
                    closedSubmenu = true;
                    break;
                }
            }

            if (!closedSubmenu)
                Close();
        }
    }

    public static void UpdateUI()
    {
        if (!_visible)
            return;

        Action? executed = null;
        var shouldClose = false;

        MenuUI(0, -1, _popupStyle, ref executed, ref shouldClose);

        if (shouldClose)
        {
            Input.ConsumeButton(InputCode.MouseLeft);
            Close();
        }
        else if (executed != null)
        {
            Close();
            executed();
        }
    }

    private static bool HasChildren(int index)
    {
        if (index < 0 || index >= _itemCount - 1) return false;
        return _items![index + 1].Level > _items[index].Level;
    }

    private static void MenuUI(
        int level,
        int parentIndex,
        PopupStyle style,
        ref Action? executed,
        ref bool shouldClose)
    {
        var startIndex = level == 0 ? 0 : parentIndex + 1;
        var parentLevel = level == 0 ? -1 : _items![parentIndex].Level;
        
        using var _ = UI.BeginPopup((byte)(ElementId.Menu+ level), style);

        if (UI.IsClosed())
            shouldClose = true;

        using (UI.BeginContainer(EditorStyle.ContextMenu.Menu))
        using (UI.BeginColumn(ContainerStyle.Fit with { MinWidth = style.MinWidth }))
        {
            if (level == 0 && _title != null)
            {
                using (UI.BeginContainer(EditorStyle.Popup.TitleItem))
                    UI.Label(_title, EditorStyle.Popup.Title);
                UI.Spacer(EditorStyle.Control.Spacing);
                UI.Container(EditorStyle.Popup.Separator);
            }

            for (var index = startIndex; index < _itemCount; index++)
            {
                ref var item = ref _items![index];
                if (item.Level <= parentLevel) break;
                if (item.Level != parentLevel + 1) continue;

                if (item.Label == null)
                {
                    UI.Container(EditorStyle.Popup.Separator);
                    continue;
                }

                var hasChildren = HasChildren(index);
                var isSubmenuOpen = level < MaxSubmenuDepth && _levels[level].OpenSubmenu == index;
                var itemId = ElementId.Item + index;
                var enabled = item.IsEnabled;

                if (hasChildren)
                {
                    SubmenuItemUI(level, index, itemId, ref item, enabled, isSubmenuOpen, ref executed, ref shouldClose);
                    continue;
                }

                var key = item.Key;
                var ctrl = item.Ctrl;
                var alt = item.Alt;
                var shift = item.Shift;
                var hasShortcut = key != InputCode.None;
                var itemEnabled = enabled;

                void Content()
                {
                    UI.Flex();
                    EditorUI.Shortcut(key, ctrl, alt, shift, selected: EditorUI.IsHovered() && itemEnabled);
                }

                if (EditorUI.PopupItem(itemId, item.Icon, item.Label, hasShortcut ? Content : null, selected: item.IsChecked, disabled: !enabled, showChecked: _levels[level].ShowChecked, showIcon: _levels[level].ShowIcons))
                    executed = item.Handler;
            }
        }
    }

    private static void SubmenuItemUI(int level, int index, int itemId, ref PopupMenuItem item, bool enabled, bool isSubmenuOpen, ref Action? executed, ref bool shouldClose)
    {
        static void Content()
        {
            UI.Flex();
            EditorUI.ControlIcon(EditorAssets.Sprites.IconSubmenu);
        }

        if (EditorUI.PopupItem(itemId, item.Icon, item.Label, Content, selected: item.IsChecked, disabled: !enabled, showChecked: _levels[level].ShowChecked, showIcon: _levels[level].ShowIcons))
        {
            _levels[level].OpenSubmenu = isSubmenuOpen ? -1 : index;

            for (var l = level + 1; l < MaxSubmenuDepth; l++)
                _levels[l].OpenSubmenu = -1;
        }

        var hovered = UI.IsHovered(itemId) && enabled;
        if (hovered && _levels[level].OpenSubmenu >= 0 && _levels[level].OpenSubmenu != index)
        {
            _levels[level].OpenSubmenu = index;
            for (var l = level + 1; l < MaxSubmenuDepth; l++)
                _levels[l].OpenSubmenu = -1;
        }

        if (isSubmenuOpen)
        {
            _levels[level + 1].ShowChecked = item.ShowChecked;
            _levels[level + 1].ShowIcons = item.ShowIcons;
            var itemRect = UI.GetElementWorldRect(itemId);

            var style = new PopupStyle
            {
                AnchorX = Align.Max,
                AnchorY = Align.Min,
                PopupAlignX = Align.Min,
                PopupAlignY = Align.Min,
                Spacing = level == 0 ? 0 : EditorStyle.Control.Spacing,
                ClampToScreen = true,
                AnchorRect = itemRect
            };

            MenuUI(level + 1, index, style, ref executed, ref shouldClose);
        }
    }
}
