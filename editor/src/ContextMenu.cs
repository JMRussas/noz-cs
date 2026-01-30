//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public struct ContextMenuDef(
    ContextMenuItem[] items,
    string? title = null,
    Sprite? icon = null,
    bool showChecked = false,
    bool showIcons = true)
{
    public string? Title = title;
    public Sprite? Icon = icon;
    public ContextMenuItem[] Items = items;
    public bool ShowChecked = showChecked;
    public bool ShowIcons = showIcons;

    public static implicit operator ContextMenuDef(ContextMenuItem[] items) => new(items);
}

public struct ContextMenuItem
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

    public static ContextMenuItem Item(string label, Action handler, InputCode key = InputCode.None, bool ctrl = false, bool alt = false, bool shift = false, int level = 0, Func<bool>? enabled = null, Func<bool>? isChecked = null, Sprite? icon = null) =>
        new() { Label = label, Handler = handler, Level = level, Key = key, Ctrl = ctrl, Alt = alt, Shift = shift, GetEnabled = enabled, GetChecked = isChecked, Icon = icon, ShowChecked = true };

    public static ContextMenuItem Submenu(string label, int level = 0, bool showChecked = false, bool showIcons = true) =>
        new() { Label = label, Handler = null, Level = level, ShowChecked = showChecked, ShowIcons = showIcons };

    public static ContextMenuItem Separator(int level = 0) =>
        new() { Label = null, Handler = null, Level = level };

    public static ContextMenuItem FromCommand(Command cmd, int level = 0, Func<bool>? enabled = null, Func<bool>? isChecked = null) =>
        new() { Label = cmd.Name, Handler = cmd.Handler, Level = level, Key = cmd.Key, Ctrl = cmd.Ctrl, Alt = cmd.Alt, Shift = cmd.Shift, GetEnabled = enabled, GetChecked = isChecked, Icon = cmd.Icon, ShowChecked = true };
}

public static class ContextMenu
{
    private struct LevelState
    {
        public int OpenSubmenu;
        public bool ShowChecked;
        public bool ShowIcons;
    }

    private static readonly ElementId MenuIdStart = 10;
    private static readonly ElementId ItemIdStart = 20;
    private const int MaxItems = 64;
    private const int MaxSubmenuDepth = 8;

    private static bool _visible;
    private static Vector2 _position;
    private static Vector2 _worldPosition;
    private static ContextMenuItem[]? _items;
    private static int _itemCount;
    private static string? _title;
    private static InputScope _scope;
    private static readonly LevelState[] _levels = new LevelState[MaxSubmenuDepth];

    public static bool IsVisible => _visible;
    public static Vector2 WorldPosition => _worldPosition;

    public static void Init()
    {
    }

    public static void Shutdown()
    {
        _visible = false;
        _items = null;
        _itemCount = 0;
        _title = null;
    }

    public static void Open(ContextMenuDef def)
    {
        Open(def.Items, def.Title, showChecked: def.ShowChecked, showIcons: def.ShowIcons);
    }

    public static void Open(ContextMenuItem[] items, string? title = null, bool showChecked = true, bool showIcons = true)
    {
        _items = items;
        _itemCount = Math.Min(items.Length, MaxItems);
        _title = title;
        _position = UI.ScreenToUI(Input.MousePosition);
        _worldPosition = Workspace.MouseWorldPosition;
        _visible = true;
        for (var i = 0; i < MaxSubmenuDepth; i++)
            _levels[i] = new LevelState { OpenSubmenu = -1, ShowChecked = true, ShowIcons = true };
        _levels[0].ShowChecked = showChecked;
        _levels[0].ShowIcons = showIcons;
        UI.SetFocus(0, EditorStyle.CanvasId.ContextMenu);

        _scope = Input.PushScope();
    }

    public static void Open(Command[] commands, string? title = null)
    {
        var items = new ContextMenuItem[commands.Length];
        for (var i = 0; i < commands.Length; i++)
            items[i] = ContextMenuItem.FromCommand(commands[i]);
        Open(items, title);
    }

    public static void Close()
    {
        _visible = false;
        _items = null;
        _itemCount = 0;
        _title = null;
        UI.ClearFocus();
        Input.PopScope(_scope);
    }

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
        if (!_visible || _items == null)
            return;

        Action? executed = null;
        var shouldClose = false;

        using (UI.BeginCanvas(id: EditorStyle.CanvasId.ContextMenu))
            MenuUI(0, -1, new Rect(_position, Vector2.Zero), ref executed, ref shouldClose);

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
        Rect anchorRect,
        ref Action? executed,
        ref bool shouldClose)
    {
        var startIndex = level == 0 ? 0 : parentIndex + 1;
        var parentLevel = level == 0 ? -1 : _items![parentIndex].Level;

        anchorRect.Y -= 8;

        using var _ = UI.BeginPopup((byte)(MenuIdStart + level), new PopupStyle
        {
            AnchorX = Align.Max,
            AnchorY = Align.Min,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Min,
            Spacing = level == 0 ? 0 : EditorStyle.Control.Spacing,
            ClampToScreen = true,
            AnchorRect = anchorRect
        });

        if (UI.IsClosed())
            shouldClose = true;

        using (UI.BeginContainer(EditorStyle.ContextMenu.Menu))
        using (UI.BeginColumn(ContainerStyle.Fit))
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
                var itemId = (byte)(ItemIdStart + index);
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
                    EditorUI.Shortcut(key, ctrl, alt, shift, selected: UI.IsHovered() && itemEnabled);
                }

                if (EditorUI.PopupItem(itemId, item.Icon, item.Label, hasShortcut ? Content : null, selected: item.IsChecked, disabled: !enabled, showChecked: _levels[level].ShowChecked, showIcon: _levels[level].ShowIcons))
                    executed = item.Handler;
            }
        }
    }

    private static void SubmenuItemUI(int level, int index, byte itemId, ref ContextMenuItem item, bool enabled, bool isSubmenuOpen, ref Action? executed, ref bool shouldClose)
    {
        static void Content()
        {
            UI.Flex();
            EditorUI.ControlIcon(EditorAssets.Sprites.IconSubmenu);
        }

        if (EditorUI.PopupItem(itemId, item.Icon, item.Label, Content, selected: isSubmenuOpen, disabled: !enabled, showChecked: _levels[level].ShowChecked, showIcon: _levels[level].ShowIcons))
        {
            _levels[level].OpenSubmenu = isSubmenuOpen ? -1 : index;

            for (var l = level + 1; l < MaxSubmenuDepth; l++)
                _levels[l].OpenSubmenu = -1;
        }

        var hovered = UI.IsHovered() && enabled;
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
            var itemRect = UI.GetElementRectInCanvas(EditorStyle.CanvasId.ContextMenu, itemId);
            MenuUI(level + 1, index, itemRect, ref executed, ref shouldClose);
        }
    }
}
