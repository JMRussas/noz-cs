//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public struct ContextMenuItem
{
    public string? Label;
    public Action? Handler;
    public bool Enabled;
    public int Level;
    public InputCode Key;
    public bool Ctrl;
    public bool Alt;
    public bool Shift;

    public static ContextMenuItem Item(string label, Action handler, InputCode key = InputCode.None, bool ctrl = false, bool alt = false, bool shift = false, int level = 0) =>
        new() { Label = label, Handler = handler, Enabled = true, Level = level, Key = key, Ctrl = ctrl, Alt = alt, Shift = shift };

    public static ContextMenuItem Submenu(string label, int level = 0) =>
        new() { Label = label, Handler = null, Enabled = true, Level = level };

    public static ContextMenuItem Separator(int level = 0) =>
        new() { Label = null, Handler = null, Enabled = true, Level = level };

    public static ContextMenuItem Disabled(string label, int level = 0) =>
        new() { Label = label, Handler = null, Enabled = false, Level = level };

    public static ContextMenuItem FromCommand(Command cmd, int level = 0) =>
        new() { Label = cmd.Name, Handler = cmd.Handler, Enabled = true, Level = level, Key = cmd.Key, Ctrl = cmd.Ctrl, Alt = cmd.Alt, Shift = cmd.Shift };
}

public static class ContextMenu
{
    private static readonly ElementId CloseId = 1;
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

        using (UI.BeginCanvas(id: EditorStyle.CanvasId.ContextMenu))
        {
            using (UI.BeginContainer(id: CloseId))
                if (UI.WasPressed())
                    Close();

            var menuWidth = RenderMenu(0, -1, _position, ref executed);

            // Track menu positions for each level
            Span<Vector2> menuPositions = stackalloc Vector2[MaxSubmenuDepth + 1];
            Span<float> menuWidths = stackalloc float[MaxSubmenuDepth + 1];
            menuPositions[0] = _position;
            menuWidths[0] = menuWidth;

            // Render open submenus
            for (var level = 0; level < MaxSubmenuDepth; level++)
            {
                var parentIndex = _openSubmenu[level];
                if (parentIndex < 0) break;

                var parentMenuPos = menuPositions[level];

                // Calculate Y position of parent item within its menu
                var itemY = parentMenuPos.Y + EditorStyle.Overlay.Padding;

                // Add title height only for root menu
                if (level == 0 && _title != null)
                    itemY += EditorStyle.ContextMenu.TextSize + EditorStyle.ContextMenu.SeparatorSpacing;

                // Count items before parent to get Y offset
                var searchParent = level == 0 ? -1 : _openSubmenu[level - 1];
                var searchStart = level == 0 ? 0 : searchParent + 1;
                var searchParentLevel = level == 0 ? -1 : _items[searchParent].Level;

                for (var i = searchStart; i < parentIndex; i++)
                {
                    ref var item = ref _items[i];
                    if (item.Level <= searchParentLevel) break;
                    if (item.Level == searchParentLevel + 1)
                    {
                        itemY += item.Label == null
                            ? EditorStyle.ContextMenu.SeparatorSpacing
                            : EditorStyle.Popup.Item.Height.Value;
                    }
                }

                // Position submenu to the right of parent menu
                var submenuPos = new Vector2(parentMenuPos.X + menuWidths[level] + 2, itemY);
                menuPositions[level + 1] = submenuPos;
                menuWidths[level + 1] = RenderMenu(level + 1, parentIndex, submenuPos, ref executed);
            }
        }

        if (executed != null)
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

    private static float RenderMenu(int level, int parentIndex, Vector2 menuPos, ref Action? executed)
    {
        var startIndex = level == 0 ? 0 : parentIndex + 1;
        var parentLevel = level == 0 ? -1 : _items![parentIndex].Level;

        using (UI.BeginContainer(
            style: EditorStyle.ContextMenu.Menu with 
            { 
                Margin = EdgeInsets.TopLeft(menuPos.Y, menuPos.X),
                Size = Size2.Fit
            },
            id: (byte)(MenuIdStart + level)))
        using (UI.BeginColumn(new ContainerStyle { Size = Size2.Fit }))
        {
            if (level == 0 && _title != null)
            {
                UI.Label(_title, EditorStyle.Popup.Text);
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

                using (UI.BeginContainer(EditorStyle.Popup.Item, id: (byte)(ItemIdStart + index)))
                {
                    var hovered = UI.IsHovered() && item.Enabled;
                    if (hovered || isSubmenuOpen)
                        UI.Container(new ContainerStyle
                        {
                            Margin = EdgeInsets.LeftRight(-4),
                            Color = EditorStyle.SelectionColor
                        });

                    using (UI.BeginRow(ContainerStyle.Default))
                    {
                        using (UI.BeginContainer(EditorStyle.ContextMenu.IconContainer))
                            UI.Image(EditorAssets.Sprites.AssetIconEvent, ImageStyle.Center);

                        using (UI.BeginContainer(EditorStyle.ContextMenu.ItemLeft))
                            UI.Label(item.Label, new LabelStyle
                            {
                                FontSize = EditorStyle.ContextMenu.TextSize,
                                Color = !item.Enabled
                                    ? EditorStyle.Overlay.DisabledTextColor
                                    : (hovered || isSubmenuOpen)
                                        ? EditorStyle.Overlay.AccentTextColor
                                        : EditorStyle.Overlay.TextColor,
                                AlignX = Align.Min,
                                AlignY = Align.Center
                            });

                        // Submenu arrow or shortcut
                        using (UI.BeginContainer(EditorStyle.ContextMenu.ItemRight))
                        {
                            if (hasChildren)
                            {
                                UI.Label(">", new LabelStyle
                                {
                                    FontSize = EditorStyle.ContextMenu.TextSize,
                                    Color = !item.Enabled
                                        ? EditorStyle.Overlay.DisabledTextColor
                                        : (hovered || isSubmenuOpen)
                                            ? EditorStyle.Overlay.AccentTextColor
                                            : EditorStyle.Overlay.TextColor,
                                    AlignX = Align.Min,
                                    AlignY = Align.Center
                                });
                            }
                            else if (item.Key != InputCode.None)
                            {
                                EditorUI.Shortcut(item.Key, item.Ctrl, item.Alt, item.Shift, selected: hovered, align: Align.Max);
                            }
                        }
                    }

                    if (UI.WasPressed() && item.Enabled)
                    {
                        if (hasChildren)
                        {
                            // Toggle submenu
                            if (isSubmenuOpen)
                                _openSubmenu[level] = -1;
                            else
                                _openSubmenu[level] = index;

                            // Close any deeper submenus
                            for (var l = level + 1; l < MaxSubmenuDepth; l++)
                                _openSubmenu[l] = -1;
                        }
                        else
                        {
                            executed = item.Handler;
                        }
                    }

                    // Open submenu on hover (after a parent is clicked open)
                    if (hovered && hasChildren && item.Enabled)
                    {
                        if (_openSubmenu[level] >= 0 && _openSubmenu[level] != index)
                        {
                            _openSubmenu[level] = index;
                            for (var l = level + 1; l < MaxSubmenuDepth; l++)
                                _openSubmenu[l] = -1;
                        }
                    }
                }
            }
        }

        var menuRect = UI.GetElementRect((byte)(MenuIdStart + level), EditorStyle.CanvasId.ContextMenu);
        return menuRect.Width > 0
            ? menuRect.Width
            : EditorStyle.ContextMenu.MinWidth + EditorStyle.Overlay.Padding * 2;
    }
}
