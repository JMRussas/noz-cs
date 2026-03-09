//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static partial class AssetBrowser
{
    private const int MaxItems = 128;
    private const float MaxListHeight = EditorStyle.Control.Height * 8;

    private static partial class WidgetIds
    {
        public static partial WidgetId Trigger { get; }
        public static partial WidgetId Popup { get; }
        public static partial WidgetId Search { get; }
        public static partial WidgetId List { get; }
        public static partial WidgetId Item { get; }
    }

    private static WidgetId _openId;
    private static string _filterText = "";
    private static string _lastFilterText = "";
    private static readonly string[] _filtered = new string[MaxItems];
    private static int _filteredCount;
    private static string? _selected;
    private static string[]? _items;

    public static bool IsOpen => _openId != WidgetId.None;

    public static string? Show(WidgetId id, string[] items, string label = "+ Add Reference")
    {
        _selected = null;
        _items = items;
        var isOpen = _openId == id;

        TriggerUI(id, label, isOpen);

        if (isOpen)
            PopupUI(id);

        return _selected;
    }

    private static void Open(WidgetId id)
    {
        _openId = id;
        _filterText = "";
        _lastFilterText = "";
        _filteredCount = 0;
        UpdateFilter();
        UI.SetHot(WidgetIds.Search);
    }

    private static void Close()
    {
        if (!IsOpen) return;
        _openId = WidgetId.None;
        _filterText = "";
        _lastFilterText = "";
        _filteredCount = 0;
        _items = null;
        UI.ClearHot();
    }

    private static void TriggerUI(WidgetId id, string label, bool isOpen)
    {
        var s = EditorStyle.DropDown;
        var flags = WidgetFlags.None;
        if (isOpen) flags |= WidgetFlags.Checked;

        if (s.Resolve != null)
            s = s.Resolve(s, flags);

        using (UI.BeginContainer(id, new ContainerStyle
        {
            Height = s.Height,
            Color = s.Color,
            BorderRadius = s.BorderRadius,
            Padding = EdgeInsets.LeftRight(10),
            AlignY = Align.Center,
            Spacing = s.Spacing,
        }))
        {
            UI.Text(label, new LabelStyle
            {
                FontSize = s.FontSize,
                Color = s.ContentColor,
                AlignY = Align.Center,
            });
            UI.Flex();
            if (s.ArrowIcon != null)
                UI.Image(s.ArrowIcon, new ImageStyle
                {
                    Size = s.ArrowSize,
                    Color = s.ContentColor,
                    Align = Align.Center,
                });

            if (UI.WasPressed())
            {
                if (isOpen)
                    Close();
                else
                    Open(id);
            }
        }
    }

    private static void PopupUI(WidgetId id)
    {
        var anchorRect = UI.GetElementWorldRect(id);
        var popupStyle = new PopupStyle
        {
            AnchorX = Align.Min,
            AnchorY = Align.Max,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Min,
            Spacing = 2.0f,
            ClampToScreen = true,
            AnchorRect = anchorRect,
            MinWidth = MathF.Max(anchorRect.Width, 260),
        };

        using (UI.BeginPopup(WidgetIds.Popup, popupStyle))
        {
            if (UI.IsClosed())
            {
                Close();
                return;
            }

            using (UI.BeginColumn(EditorStyle.Popup.Root with
            {
                Width = Size.Fit,
                Height = Size.Fit,
                MinWidth = MathF.Max(anchorRect.Width, 260),
                Clip = true,
            }))
            {
                SearchUI();
                UI.Container(EditorStyle.Popup.Separator);
                ListUI();
            }
        }
    }

    private static void SearchUI()
    {
        using (UI.BeginRow(EditorStyle.Popup.Item with { Spacing = 4.0f }))
        {
            using (UI.BeginContainer(EditorStyle.Control.IconContainer))
                UI.Image(EditorAssets.Sprites.IconSearch, EditorStyle.Control.Icon);

            using (UI.BeginFlex())
                _filterText = UI.TextInput(WidgetIds.Search, _filterText, EditorStyle.CommandPalette.SearchTextBox, "Search...");
        }

        if (_filterText != _lastFilterText)
        {
            UpdateFilter();
            _lastFilterText = _filterText;
        }
    }

    private static void ListUI()
    {
        var listHeight = MathF.Min(_filteredCount * EditorStyle.Control.Height + 8, MaxListHeight);
        if (_filteredCount == 0)
            listHeight = EditorStyle.Control.Height;

        using (UI.BeginContainer(new ContainerStyle { Height = listHeight }))
        using (UI.BeginScrollable(WidgetIds.List))
        using (UI.BeginColumn(new ContainerStyle { Padding = EdgeInsets.Symmetric(4, 0) }))
        {
            if (_filteredCount == 0)
            {
                using (UI.BeginContainer(new ContainerStyle
                {
                    Height = EditorStyle.Control.Height,
                    AlignX = Align.Center,
                    AlignY = Align.Center,
                }))
                {
                    UI.Text("No matches", new LabelStyle
                    {
                        FontSize = EditorStyle.Control.TextSize,
                        Color = EditorStyle.Palette.Placeholder,
                        AlignY = Align.Center,
                    });
                }
                return;
            }

            for (var i = 0; i < _filteredCount; i++)
            {
                var name = _filtered[i];
                var itemId = WidgetIds.Item + i;
                var hovered = UI.IsHovered(itemId);

                using (UI.BeginRow(itemId, EditorStyle.Popup.Item with
                {
                    Spacing = 8,
                    Color = hovered ? EditorStyle.Palette.Active : Color.Transparent,
                    BorderRadius = hovered ? 2 : 0,
                }))
                {
                    using (UI.BeginContainer(EditorStyle.Control.IconContainer))
                        UI.Image(EditorAssets.Sprites.AssetIconSprite, new ImageStyle
                        {
                            Size = EditorStyle.Control.IconSize,
                            Color = hovered ? EditorStyle.Palette.HeaderText : EditorStyle.Palette.Label,
                            Align = Align.Center,
                        });

                    UI.Text(name, new LabelStyle
                    {
                        FontSize = EditorStyle.Control.TextSize,
                        Color = EditorStyle.Palette.Content,
                        AlignY = Align.Center,
                    });

                    if (UI.WasPressed())
                    {
                        _selected = name;
                        Close();
                    }
                }
            }
        }
    }

    private static void UpdateFilter()
    {
        _filteredCount = 0;
        if (_items == null) return;

        var filter = _filterText.Trim();
        foreach (var item in _items)
        {
            if (_filteredCount >= MaxItems) break;
            if (string.IsNullOrEmpty(filter) || item.Contains(filter, StringComparison.OrdinalIgnoreCase))
                _filtered[_filteredCount++] = item;
        }
    }
}
