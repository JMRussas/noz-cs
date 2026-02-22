//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class WidgetStyle
{
    // Shared base styles - used by all widgets
    internal static ContainerStyle _root = new() { MinWidth = 40f, MinHeight = 40f, Height = 40f };
    internal static ContainerStyle _fill = new() { Color = Color.FromRgb(0x1d1d1d), Border = new BorderStyle { Radius = 10f } };
    internal static ContainerStyle _fillHovered = _fill with { Color = Color.FromRgb(0x2a2a2a) };
    internal static ContainerStyle _fillChecked = _fill with { Color = Color.FromRgb(0x1d1d1d) };
    internal static ContainerStyle _fillDisabled = _fill with { Color = Color.FromRgb(0x1d1d1d) };
    internal static LabelStyle _text = new() { FontSize = 16f, Color = Color.FromRgb(0xebebeb), AlignY = Align.Center };
    internal static LabelStyle _textHovered = _text with { Color = Color.White };
    internal static LabelStyle _textChecked = _text with { Color = Color.White };
    internal static LabelStyle _textDisabled = _text with { Color = Color.FromRgb(0x3e3e3e) };
    internal static ImageStyle _icon = new() { AlignY = Align.Center, Width = 16f, Height = 16f, Color = Color.FromRgb(0xebebeb) };
    internal static ImageStyle _iconHovered = _icon with { Color = Color.White };
    internal static ImageStyle _iconChecked = _icon with { Color = Color.White };
    internal static ImageStyle _iconDisabled = _icon with { Color = Color.FromRgb(0x3e3e3e) };

    public static ref ContainerStyle Root => ref _root;
    public static ref ContainerStyle Fill => ref _fill;
    public static ref ContainerStyle FillHovered => ref _fillHovered;
    public static ref ContainerStyle FillChecked => ref _fillChecked;
    public static ref ContainerStyle FillDisabled => ref _fillDisabled;
    public static ref LabelStyle Text => ref _text;
    public static ref LabelStyle TextHovered => ref _textHovered;
    public static ref LabelStyle TextChecked => ref _textChecked;
    public static ref LabelStyle TextDisabled => ref _textDisabled;
    public static ref ImageStyle Icon => ref _icon;
    public static ref ImageStyle IconHovered => ref _iconHovered;
    public static ref ImageStyle IconChecked => ref _iconChecked;
    public static ref ImageStyle IconDisabled => ref _iconDisabled;

    public const float Spacing = 5f;
    public const float IconSize = 16f;
    public const float ContentPadding = 10f;

    public static class Button
    {
        internal static ContainerStyle _content = new() { Padding = EdgeInsets.LeftRight(ContentPadding), Spacing = Spacing };
        public static ref ContainerStyle Content => ref _content;
    }

    public static class Slider
    {
        internal static ContainerStyle _track = new() { Height = 6f, Color = Color.FromRgb(0x1d1d1d), Border = new BorderStyle { Radius = 3f }, AlignY = Align.Center };
        internal static ContainerStyle _fill = new() { Height = 6f, Color = Color.Cyan, Border = new BorderStyle { Radius = 3f }, AlignY = Align.Center };
        internal static ContainerStyle _thumb = new() { Width = 16f, Height = 16f, Color = Color.White, Border = new BorderStyle { Radius = 8f }, AlignY = Align.Center, AlignX = Align.Min };

        public static ref ContainerStyle Track => ref _track;
        public static ref ContainerStyle TrackFill => ref _fill;
        public static ref ContainerStyle Thumb => ref _thumb;

        public const float ThumbSize = 16f;
    }

    public static class Toggle
    {
        internal static ContainerStyle _root = new() { Width = 40f, Height = 40f, AlignY = Align.Center };
        internal static ContainerStyle _fill = new() { Border = new BorderStyle { Radius = 10f, Width = 3, Color = Color.FromRgb(0x3a3a3a) } };
        internal static ContainerStyle _fillHovered = new() { Border = new BorderStyle { Radius = 10f, Width = 3, Color = Color.FromRgb(0x4a4a4a) } };
        internal static ContainerStyle _fillDisabled = new() { Border = new BorderStyle { Radius = 10f, Width = 3, Color = Color.FromRgb(0x1d1d1d) } };
        internal static ImageStyle _checkIcon = new() { Color = Color.Cyan, AlignX = Align.Center, AlignY = Align.Center, Width = 30f, Height = 30f };

        public static ref ContainerStyle ToggleRoot => ref _root;
        public static ref ContainerStyle ToggleFill => ref _fill;
        public static ref ContainerStyle FillHovered => ref _fillHovered;
        public static ref ContainerStyle FillDisabled => ref _fillDisabled;
        public static ref ImageStyle CheckIcon => ref _checkIcon;
    }

    public static class Popup
    {
        internal static ContainerStyle _root = new()
        {
            AlignX = Align.Center,
            AlignY = Align.Center,
            Padding = Spacing,
            Color = Color.Black,
            Border = new BorderStyle { Radius = 10f, Width = 2f, Color = Color.FromRgb(0x4a4a4a) }
        };
        internal static ContainerStyle _item = WidgetStyle._root;
        internal static ContainerStyle _itemContent = new() { Padding = EdgeInsets.LeftRight(ContentPadding) };
        internal static ContainerStyle _separator = new() { Height = 1, Margin = EdgeInsets.Symmetric(2, 4), Color = Color.FromRgb(0x2a2a2a) };
        internal static ContainerStyle _checkContent = new() { Width = 20f, AlignY = Align.Center };

        public static ref ContainerStyle PopupRoot => ref _root;
        public static ref ContainerStyle Item => ref _item;
        public static ref ContainerStyle ItemContent => ref _itemContent;
        public static ref ContainerStyle Separator => ref _separator;
        public static ref ContainerStyle CheckContent => ref _checkContent;
    }

    public static class ToolTip
    {
        internal static ContainerStyle _root = new()
        {
            Size = Size2.Fit,
            Color = Color.White,
            Padding = EdgeInsets.Symmetric(4, 8),
            Border = new BorderStyle { Radius = 6 }
        };
        internal static LabelStyle _text = new() { FontSize = 13f, Color = Color.Black, AlignX = Align.Center, AlignY = Align.Center };

        public static ref ContainerStyle ToolTipRoot => ref _root;
        public static ref LabelStyle ToolTipText => ref _text;
    }

    public static class Dialog
    {
        internal static PopupStyle _popup = new() { AutoClose = false };
        internal static ContainerStyle _darken = new() { Color = Color.Black.WithAlpha(0.7f) };
        internal static ContainerStyle _root = new()
        {
            Size = Size2.Fit,
            Color = Color.Black,
            AlignX = Align.Center,
            AlignY = Align.Center,
            MinWidth = 100,
            MinHeight = 100,
            Border = new BorderStyle { Radius = 20f, Width = 2, Color = Color.FromRgb(0x4a4a4a) }
        };
        internal static ContainerStyle _closeButton = new()
        {
            Color = Color.Red,
            Width = 30,
            Height = 30,
            Padding = 4,
            Border = new BorderStyle { Radius = 10f },
            AlignX = Align.Max,
            Margin = EdgeInsets.TopRight(-7.5f)
        };
        internal static ImageStyle _closeButtonIcon = new() { Color = Color.Black };
        internal static ImageStyle _closeButtonIconHovered = new() { Color = Color.FromRgb(0x3a3a3a) };

        public static ref PopupStyle DialogPopup => ref _popup;
        public static ref ContainerStyle Darken => ref _darken;
        public static ref ContainerStyle DialogRoot => ref _root;
        public static ref ContainerStyle CloseButton => ref _closeButton;
        public static ref ImageStyle CloseButtonIcon => ref _closeButtonIcon;
        public static ref ImageStyle CloseButtonIconHovered => ref _closeButtonIconHovered;
    }

    public static class Tab
    {
        internal static ContainerStyle _content = new() { Padding = EdgeInsets.LeftRight(20f, 5f), Spacing = 10f };
        internal static ContainerStyle _fillHovered = WidgetStyle._fillHovered with { Border = default };
        internal static ContainerStyle _fillChecked = new() { Color = Color.Cyan.MultiplyValue(0.7f), Border = default };
        internal static ContainerStyle _fillCheckedHovered = new() { Color = Color.Cyan.MultiplyValue(0.75f), Border = default };
        internal static ContainerStyle _underline = new() { Width = Spacing, Color = Color.Cyan };

        public static ref ContainerStyle Content => ref _content;
        public static ref ContainerStyle TabFillHovered => ref _fillHovered;
        public static ref ContainerStyle FillChecked => ref _fillChecked;
        public static ref ContainerStyle FillCheckedHovered => ref _fillCheckedHovered;
        public static ref ContainerStyle Underline => ref _underline;
    }

    public static class ColorPicker
    {
        public const int SVSize = 160;
        public const int HueBarWidth = 20;
        public const int HueBarHeight = SVSize;
        public const int AlphaBarWidth = SVSize + HueBarWidth + 6;
        public const int AlphaBarHeight = 14;
        public const int PickerSpacing = 6;
        public const int SwatchCellSize = 28;
        public const int SwatchColumns = 8;

        internal static ContainerStyle _svArea = new()
        {
            Width = SVSize,
            Height = SVSize,
            BorderWidth = 1,
            BorderColor = Color.Black20Pct,
            BorderRadius = 3,
            Clip = true
        };

        internal static ContainerStyle _hueBar = new()
        {
            Width = HueBarWidth,
            Height = HueBarHeight,
            BorderWidth = 1,
            BorderColor = Color.Black20Pct,
            BorderRadius = 3,
            Clip = true
        };

        internal static ContainerStyle _alphaBar = new()
        {
            Width = AlphaBarWidth,
            Height = AlphaBarHeight,
            BorderWidth = 1,
            BorderColor = Color.Black20Pct,
            BorderRadius = 3,
            Clip = true
        };

        internal static ContainerStyle _previewRow = new() { Spacing = 4, Height = 24 };
        internal static ContainerStyle _hexRow = new() { Spacing = 4 };

        public static ref ContainerStyle SVArea => ref _svArea;
        public static ref ContainerStyle HueBar => ref _hueBar;
        public static ref ContainerStyle AlphaBar => ref _alphaBar;
        public static ref ContainerStyle PreviewRow => ref _previewRow;
        public static ref ContainerStyle HexRow => ref _hexRow;
    }
}
