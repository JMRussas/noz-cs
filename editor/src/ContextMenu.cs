//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public struct ContextMenuDef(ContextMenuItem[] items, string? title = null, Sprite? icon = null)
{
    public string? Title = title;
    public Sprite? Icon = icon;
    public ContextMenuItem[] Items = items;

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

    public readonly bool IsEnabled => GetEnabled?.Invoke() ?? true;
    public readonly bool IsChecked => GetChecked?.Invoke() ?? false;

    public static ContextMenuItem Item(string label, Action handler, InputCode key = InputCode.None, bool ctrl = false, bool alt = false, bool shift = false, int level = 0, Func<bool>? enabled = null, Func<bool>? isChecked = null) =>
        new() { Label = label, Handler = handler, Level = level, Key = key, Ctrl = ctrl, Alt = alt, Shift = shift, GetEnabled = enabled, GetChecked = isChecked };

    public static ContextMenuItem Submenu(string label, int level = 0) =>
        new() { Label = label, Handler = null, Level = level };

    public static ContextMenuItem Separator(int level = 0) =>
        new() { Label = null, Handler = null, Level = level };

    public static ContextMenuItem FromCommand(Command cmd, int level = 0, Func<bool>? enabled = null, Func<bool>? isChecked = null) =>
        new() { Label = cmd.Name, Handler = cmd.Handler, Level = level, Key = cmd.Key, Ctrl = cmd.Ctrl, Alt = cmd.Alt, Shift = cmd.Shift, GetEnabled = enabled, GetChecked = isChecked };
}

public static class ContextMenu
{
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
    private static readonly int[] _openSubmenu = new int[MaxSubmenuDepth];

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
        Open(def.Items, def.Title);
    }

    public static void Open(ContextMenuItem[] items, string? title = null)
    {
        _items = items;
        _itemCount = Math.Min(items.Length, MaxItems);
        _title = title;
        _position = UI.ScreenToUI(Input.MousePosition);
        _worldPosition = Workspace.MouseWorldPosition;
        _visible = true;
        for (var i = 0; i < MaxSubmenuDepth; i++)
            _openSubmenu[i] = -1;
        UI.SetFocus(0, EditorStyle.CanvasId.ContextMenu);
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
    }

    public static void Update()
    {
        if (!_visible)
            return;

        if (Input.WasButtonPressed(InputCode.KeyEscape))
        {
            Input.ConsumeButton(InputCode.KeyEscape);

            // Close deepest open submenu first, or close menu if none open
            var closedSubmenu = false;
            for (var level = MaxSubmenuDepth - 1; level >= 0; level--)
            {
                if (_openSubmenu[level] >= 0)
                {
                    _openSubmenu[level] = -1;
                    closedSubmenu = true;
                    break;
                }
            }

            if (!closedSubmenu)
                Close();
        }

        if (Input.WasButtonPressed(InputCode.MouseRight))
        {
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
        {
            MenuUI(0, -1, new Rect(_position, Vector2.Zero), ref executed, ref shouldClose);
        }

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

    private static void MenuUI(int level, int parentIndex, Rect anchorRect, ref Action? executed, ref bool shouldClose)
    {
        var startIndex = level == 0 ? 0 : parentIndex + 1;
        var parentLevel = level == 0 ? -1 : _items![parentIndex].Level;

        anchorRect.Y -= 8;

        using (UI.BeginPopup((byte)(MenuIdStart + level), new PopupStyle
        {
            AnchorX = Align.Max,
            AnchorY = Align.Min,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Min,
            Spacing = level == 0 ? 0 : EditorStyle.Control.Spacing,
            ClampToScreen = true,
            AnchorRect = anchorRect
        }))
        {
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
                    var isSubmenuOpen = level < MaxSubmenuDepth && _openSubmenu[level] == index;
                    var itemId = (byte)(ItemIdStart + index);

                    using (UI.BeginContainer(itemId, EditorStyle.Popup.Item))
                    {
                        var enabled = item.IsEnabled;
                        var hovered = UI.IsHovered() && enabled;

                        if (hovered || isSubmenuOpen)
                            UI.Container(new ContainerStyle { Color = EditorStyle.SelectionColor });

                        using (UI.BeginRow(EditorStyle.ContextMenu.Item))
                        {
                            using (UI.BeginContainer(EditorStyle.Popup.IconContainer))
                            {
                                if (item.IsChecked)
                                    UI.Label("\u2713", EditorStyle.Popup.Text);
                                else if (item.Icon != null)
                                    UI.Image(item.Icon, style: EditorStyle.Popup.Icon);
                            }
                            UI.Spacer(EditorStyle.Control.Spacing);

                            UI.Label(item.Label, enabled ? EditorStyle.Popup.Text : EditorStyle.Popup.DisabledText);
                            UI.Spacer(EditorStyle.Control.Spacing);
                            UI.Flex();

                            if (hasChildren)
                            {
                                UI.Spacer(EditorStyle.Control.Spacing);
                                using (UI.BeginContainer(EditorStyle.Popup.IconContainer with { Padding = EdgeInsets.TopBottom(EditorStyle.Popup.IconContainer.Padding.T) }))
                                    UI.Image(EditorAssets.Sprites.IconSubmenu, style: EditorStyle.Popup.Icon with { AlignX = Align.Max });
                            }
                            else if (item.Key != InputCode.None)
                            {
                                UI.Spacer(EditorStyle.Control.Spacing);
                                EditorUI.Shortcut(item.Key, item.Ctrl, item.Alt, item.Shift, selected: hovered, align: Align.Min);
                            }
                        }

                        if (UI.WasPressed() && enabled)
                        {
                            if (hasChildren)
                            {
                                if (isSubmenuOpen)
                                    _openSubmenu[level] = -1;
                                else
                                    _openSubmenu[level] = index;

                                for (var l = level + 1; l < MaxSubmenuDepth; l++)
                                    _openSubmenu[l] = -1;
                            }
                            else
                            {
                                executed = item.Handler;
                            }
                        }

                        if (hovered && hasChildren && enabled)
                        {
                            if (_openSubmenu[level] >= 0 && _openSubmenu[level] != index)
                            {
                                _openSubmenu[level] = index;
                                for (var l = level + 1; l < MaxSubmenuDepth; l++)
                                    _openSubmenu[l] = -1;
                            }
                        }

                        // Render submenu inline as a child of this item
                        if (hasChildren && isSubmenuOpen)
                        {
                            var itemRect = UI.GetElementRectInCanvas(EditorStyle.CanvasId.ContextMenu, itemId);
                            var submenuAnchor = new Rect(0, 0, itemRect.Width, itemRect.Height);
                            MenuUI(level + 1, index, submenuAnchor, ref executed, ref shouldClose);
                        }
                    }
                }
            }
        }
    }
}
