//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public static class EditorStyle
{
    // :palette — Colors from Components.md style guide
    public static class Palette
    {
        // Surface Backgrounds
        public static readonly Color Canvas = Color.FromRgb(0x353535);
        public static readonly Color PageBG = Color.FromRgb(0x1A1A1A);
        public static readonly Color Body = Color.FromRgb(0x212121);
        public static readonly Color Grid = Color.FromRgb(0x3E3E3E);
        public static readonly Color Header = Color.FromRgb(0x2D2D2D);
        public static readonly Color Secondary = Color.FromRgb(0x333333);
        public static readonly Color Active = Color.FromRgb(0x3D3D3D);
        public static readonly Color Panel = Color.FromRgb(0x252526);
        public static readonly Color Primary = Color.FromRgb(0xE83A3A);
        public static readonly Color PrimaryHover = Color.FromRgb(0xF04848);

        // Text & Icon
        public static readonly Color Content = Color.FromRgb(0xE0E0E0);
        public static readonly Color HeaderText = Color.FromRgb(0xAAAAAA);
        public static readonly Color SecondaryText = Color.FromRgb(0x999999);
        public static readonly Color Label = Color.FromRgb(0x777777);
        public static readonly Color Disabled = Color.FromRgb(0x666666);
        public static readonly Color Placeholder = Color.FromRgb(0x555555);
        public static readonly Color DisabledLight = Color.FromRgb(0x333333);

        // State
        public static readonly Color TextSelection = Color.FromRgba(0xE83A3A44);
        public static readonly Color SelectionOutline = Color.FromRgba(0xE83A3A66);
        public static readonly Color SelectionFill = Color.FromRgba(0xE83A3A11);
        public static readonly Color FocusRing = Primary;

        // Separators / Grid
        public static readonly Color PanelSeparator = Color.FromRgb(0x232323);
        public static readonly Color MajorGrid = Color.FromRgb(0x2A2A2A);
    }

    // Error color
    public static readonly Color ErrorColor = Color.FromRgb(0xdf6b6d);

    // Mesh (used by shape/skeleton editors)
    public const float MeshEdgeWidth = 0.02f;
    public const float MeshVertexSize = 0.12f;
    public const float MeshWeightOutlineSize = 0.20f;
    public const float MeshWeightSize = 0.19f;



    public static readonly ContainerStyle Panel = new()
    {
        Color = Palette.Panel,
        Spacing = Control.Spacing
    };

    // :icon
    public static class Icon
    {
        private const float Size = 14.0f;
        private const float SmallSize = Size * SmallWidget.Scale;

        public static readonly ImageStyle Primary = new()
        {
            Color = Palette.Content,
            Size = Size,
            Align = Align.Center
        };

        public static readonly ImageStyle Secondary = Primary with { Color = Palette.Label };

        public static readonly ImageStyle PrimarySmall = Primary with { Size = SmallSize };
        public static readonly ImageStyle SecondarySmall = Secondary with { Size = SmallSize };
    }

    // :text
    public static class Text
    {
        public readonly static LabelStyle Primary = new()
        {
            FontSize = 16.0f,
            Color = Palette.Content,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public readonly static LabelStyle Secondary = Primary with { Color = Palette.Label };
        public readonly static LabelStyle Disabled = Primary with { Color = Palette.Placeholder };

        public readonly static LabelStyle SecondarySmall = Secondary with { FontSize = 9.0f };
    }

    // :workspace
    public static class Workspace
    {
        public static readonly Color SelectionColor = Palette.SelectionOutline;
        public static readonly Color FillColor = Palette.Canvas;
        public static readonly Color GridColor = Palette.Grid;
        public static readonly Color BoundsColor = Color.FromRgb(0x212121);
        public static readonly Color OriginColor = Color.FromRgb(0xff9f2c);
        public static readonly Color LineColor = Color.FromRgb(0x000000);
        public const float XrayAlpha = 0.5f;
        public const float OriginSize = 0.1f;
        public const float DocumentBoundsLineWidth = 0.015f;
        public static readonly Color NameColor = Color.White;
        public const float NameSize = 0.36f;
        public const float NamePadding = 0.26f;
        public const float NameOutline = 0.1f;
        public static readonly Color NameOutlineColor = Color.Black;
        public const float GridAlpha = 0.4f;
        public const float GridZeroAlpha = 0.5f;
        public const float Padding = 16f;
    }

    // :control — Global control dimensions (toolbar, popups, etc.)
    public static class Control
    {
        public const float TextSize = 16.0f;
        public const float IconSize = 14.0f;
        public const float Height = 40.0f;
        public const float BorderRadius = 4.0f;
        public const float Spacing = 6.0f;
        public const float ContentPadding = 4.0f;
        public const float SecondaryHeight = 36.0f;
        public const float ContentHeight = Height - ContentPadding * 2;

        public static readonly ContainerStyle Root = new()
        {
            Height = Height,
            Spacing = Spacing,
            Padding = EdgeInsets.Symmetric(ContentPadding, ContentPadding * 2)
        };

        public static readonly ContainerStyle RootHovered = Root with
        {
            BorderWidth = 1,
            BorderColor = Palette.FocusRing,
            BorderRadius = BorderRadius
        };

        public static readonly ContainerStyle Fill = new()
        {
            Color = Palette.PageBG,
            BorderRadius = BorderRadius,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Color = Color.Transparent };
                if ((f & WidgetFlags.Checked) != 0) return s with { Color = Palette.Active };
                if ((f & WidgetFlags.Hovered) != 0) return s with { Color = Palette.Active };
                return s;
            },
        };

        public static readonly ContainerStyle Content = new()
        {
            Padding = EdgeInsets.All(ContentPadding),
            Spacing = Spacing
        };

        // Legacy — used by popup system (EditorUI.cs) and ColorPicker
        public static readonly ContainerStyle HoverFill = Fill with { Color = Palette.Active, Resolve = null };
        public static readonly ContainerStyle SelectedFill = Fill with { Color = Palette.Active, Resolve = null };

        public readonly static LabelStyle Text = new()
        {
            FontSize = TextSize,
            Color = Palette.Content,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Color = Palette.DisabledLight };
                return s;
            },
        };
        // Legacy — used by popup system (EditorUI.cs)
        public readonly static LabelStyle DisabledText = Text with { Color = Palette.DisabledLight, Resolve = null };
        public readonly static LabelStyle HoveredText = Text with { Color = Palette.Content, Resolve = null };
        public readonly static LabelStyle SelectedText = Text with { Color = Palette.Content, Resolve = null };

        public readonly static LabelStyle PlaceholderText = new()
        {
            FontSize = TextSize,
            Color = Palette.Placeholder,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Checked) != 0) return s with { Color = Palette.HeaderText };
                if ((f & WidgetFlags.Hovered) != 0) return s with { Color = Palette.Label };
                return s;
            },
        };
        public static readonly ContainerStyle IconContainer = new()
        {
            Width = ContentHeight,
            Height = ContentHeight,
            Padding = EdgeInsets.All(2f),
        };

        public static readonly ImageStyle Icon = new()
        {
            Color = Palette.Content,
            Size = IconSize,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Color = Palette.DisabledLight };
                return s;
            },
        };

        public static readonly ImageStyle IconSecondary = Icon with { Color = Palette.Label };

        // Legacy — used by popup system (EditorUI.cs)
        public static readonly ImageStyle SelectedIcon = Icon with { Color = Palette.Content, Resolve = null };
        public static readonly ImageStyle DisabledIcon = Icon with { Color = Palette.DisabledLight, Resolve = null };
        public static readonly ImageStyle HoveredIcon = Icon with { Color = Palette.Content, Resolve = null };
    }

    // :smallcontrol
    public static class SmallWidget
    {
        public const float Scale = 0.75f;
        public const float Height = Control.Height * Scale;
        public const float BorderRadius = Control.BorderRadius;
        public const float Spacing = Control.Spacing * Scale;

        public static readonly ContainerStyle Root = new()
        {
            Height = Height,
            Spacing = Spacing
        };
    }


    // :panel
    public static class PanelOld
    {
        public const float BorderRadius = 6f;
        public const float BorderWidth = 1.0f;
        public const float ContentBorderRadius = 4f;
        public const float VerticalSpacing = 4.0f;

        public static readonly EdgeInsets BottomMargin = EdgeInsets.Bottom(-BorderRadius / 2);

        public static readonly ContainerStyle Root = new()
        {
            Color = Palette.Header,
            BorderRadius = BorderRadius, BorderWidth = BorderWidth, BorderColor = Color.Black20Pct,
            Padding = EdgeInsets.All(BorderWidth)
        };

        public static readonly ContainerStyle Content = new()
        {
            Spacing = Control.Spacing,
            Margin = EdgeInsets.TopBottom(VerticalSpacing),
            Padding = EdgeInsets.All(Control.Spacing)
        };

        public static readonly ContainerStyle UnpaddedContent = Content with
        {
            Padding = EdgeInsets.Zero
        };

        public static readonly ContainerStyle SeparatorHorizontal = new()
        {
            Height = 1,
            Color = Palette.PanelSeparator
        };

        public static readonly ContainerStyle SeparatorVertical = new()
        {
            Width = 1,
            Color = Palette.PanelSeparator
        };
    }

    // :popup
    public static class Popup
    {
        public static readonly PopupStyle Left = new()
        {
            AnchorX = Align.Min,
            AnchorY = Align.Min,
            Spacing = 2.0f,
            PopupAlignX = Align.Max,
            PopupAlignY = Align.Min,
            ClampToScreen = true,
        };

        public static readonly Color FillColor = Palette.Header;
        public static readonly Color BorderColor = Palette.Active;
        public readonly static ContainerStyle Root = new()
        {
            AlignX = Align.Center,
            AlignY = Align.Center,
            Padding = EdgeInsets.Symmetric(4, 0),
            Color = FillColor,
            BorderRadius = 6f, BorderWidth = 1.0f, BorderColor = Popup.BorderColor
        };
        public readonly static ContainerStyle Item = Control.Root;
        public readonly static ContainerStyle ItemContent = Control.Content;
        public readonly static ContainerStyle TitleItem = Item with
        {
            Padding = EdgeInsets.LeftRight(4.0f)
        };
        public readonly static ContainerStyle Separator = new() {
            Height = 1,
            Margin = EdgeInsets.Symmetric(2, 4),
            Color = Palette.Active
        };
        public readonly static LabelStyle Title = new()
        {
            FontSize = Control.TextSize,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Color = Palette.SecondaryText,
        };

        public readonly static ContainerStyle CheckContent = new()
        {
            Width = Control.Height / 2,
            AlignY = Align.Center
        };
    }

    // :button
    public static class Button
    {
        public const float IconSize = 16.0f;

        public static readonly ButtonStyle Primary = new()
        {
            Width = Size.Fit,
            MinWidth = 80.0f,
            Height = Control.Height,
            Color = Palette.Primary,
            ContentColor = Palette.Content,
            FontSize = Control.TextSize,
            IconSize = IconSize,
            Spacing = Control.Spacing,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.LeftRight(12),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with 
                {
                    Color = Palette.DisabledLight,
                    ContentColor = Palette.Placeholder
                };
                if ((f & WidgetFlags.Hovered) != 0) return s with
                {
                    Color = Palette.PrimaryHover
                };
                return s;
            },
        };

        public static readonly ButtonStyle Secondary = new()
        {
            Width = Size.Fit,
            MinWidth = 80.0f,
            Height = Control.Height,
            Color = Palette.Secondary,
            ContentColor = Palette.SecondaryText,
            FontSize = Control.TextSize,
            IconSize = IconSize,
            Spacing = Control.Spacing,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.LeftRight(12),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Disabled) != 0) return s with { Color = Palette.DisabledLight, ContentColor = Palette.Placeholder };
                if ((f & WidgetFlags.Hovered) != 0) return s with { Color = Palette.Active, ContentColor = Palette.Content };
                return s;
            },
        };

        // Toggle icon button (40x40, checked = Active bg, hover = border)
        public static readonly ButtonStyle ToggleIcon = new()
        {
            Width = Control.Height,
            Height = Control.Height,
            Color = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.All(4),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Checked) != 0) s.Color = Palette.Active;
                if ((f & WidgetFlags.Hovered) != 0) { s.BorderWidth = 1; s.BorderColor = Palette.FocusRing; }
                return s;
            },
        };

        // Small toggle icon button (30x30)
        public static readonly ButtonStyle SmallToggleIcon = new()
        {
            Width = SmallWidget.Height,
            Height = SmallWidget.Height,
            Color = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize * SmallWidget.Scale,
            BorderRadius = SmallWidget.BorderRadius,
            Padding = EdgeInsets.All(2),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Checked) != 0) s.Color = Palette.Active;
                if ((f & WidgetFlags.Hovered) != 0) { s.BorderWidth = 1; s.BorderColor = Palette.FocusRing; }
                return s;
            },
        };

        // Icon-only button (40x40, hover = Active bg)
        public static readonly ButtonStyle IconOnly = new()
        {
            Width = Control.Height,
            Height = Control.Height,
            Color = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize,
            BorderRadius = Control.BorderRadius,
            Padding = EdgeInsets.All(4),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0) s.Color = Palette.Active;
                return s;
            },
        };

        // Small icon-only button (30x30)
        public static readonly ButtonStyle SmallIconOnly = new()
        {
            Width = SmallWidget.Height,
            Height = SmallWidget.Height,
            Color = Color.Transparent,
            ContentColor = Palette.Content,
            IconSize = Control.IconSize * SmallWidget.Scale,
            BorderRadius = SmallWidget.BorderRadius,
            Padding = EdgeInsets.All(2),
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0) s.Color = Palette.Active;
                return s;
            },
        };

    }

    public static DropDownStyle DropDown = new()
    {
        Width = Size.Default,
        Height = Control.Height,
        Color = Palette.PageBG,
        ContentColor = Palette.Content,
        IconColor = Palette.Label,
        FontSize = Control.TextSize,
        IconSize = Control.IconSize,
        ArrowSize = 14.0f,
        Spacing = Control.Spacing,
        BorderRadius = Control.BorderRadius,
        Padding = EdgeInsets.LeftRight(10),
        ArrowIcon = null,
        Resolve = (s, f) =>
        {
            if ((f & WidgetFlags.Disabled) != 0) return s with
            {
                Color = Palette.PageBG,
                ContentColor = Palette.DisabledLight
            };
            if ((f & WidgetFlags.Checked) != 0) return s with { Color = Palette.Active, IconColor = Palette.HeaderText };
            if ((f & WidgetFlags.Hovered) != 0) return s with { Color = Palette.Active, IconColor = Palette.HeaderText };
            return s;
        },
    };

    private static readonly Func<TextInputStyle, WidgetFlags, TextInputStyle> TextInputResolve = (s, f) =>
    {
        if (f.HasFlag(WidgetFlags.Hot) || f.HasFlag(WidgetFlags.Hovered))
            s.BorderColor = Palette.FocusRing;
        return s;
    };

    public static readonly TextInputStyle TextInput = new()
    {
        Height = Control.Height,
        FontSize = Control.TextSize,
        TextColor = Palette.Content,
        PlaceholderColor = Palette.Placeholder,
        SelectionColor = Palette.TextSelection,
        BackgroundColor = Palette.PageBG,
        BorderRadius = Control.BorderRadius,
        BorderWidth = 1,
        BorderColor = Color.Transparent,
        Padding = EdgeInsets.Symmetric(0, 8),
        IconSize = Control.IconSize,
        IconColor = Palette.Label,
        Resolve = TextInputResolve,
    };

    public static readonly TextInputStyle TextArea = new()
    {
        Height = Control.Height * 3,
        FontSize = Control.TextSize,
        TextColor = Palette.Content,
        PlaceholderColor = Palette.Placeholder,
        SelectionColor = Palette.TextSelection,
        BackgroundColor = Palette.PageBG,
        BorderRadius = Control.BorderRadius,
        BorderWidth = 1,
        BorderColor = Color.Transparent,
        Padding = EdgeInsets.Symmetric(8, 10),
        MultiLine = true,
        PlaceholderMode = PlaceholderMode.FloatingLabel,
        LabelFontSize = 9,
        LabelColor = Palette.Label,
        Resolve = TextInputResolve,
    };

    public static class List
    {
        public const float ItemHeight = 28f;
        public const float ItemPadding = 8f;
        public static readonly Color ItemSelectedFillColor = Palette.Active;
        public static readonly Color ItemSelectedTextColor = Palette.Content;
        public static readonly Color ItemTextColor = Palette.Content;
        public static readonly Color HeaderTextColor = Palette.HeaderText;
    }

    public static class BoxSelect
    {
        public const float LineWidth = 0.02f;
        public static readonly Color LineColor = Palette.Primary;
        public static readonly Color FillColor = Palette.SelectionFill;
    }

    public static class CommandPalette
    {
        public static readonly ContainerStyle Root = Popup.Root with
        {
            Width = 450.0f,
            Height = Size.Fit,
            MinHeight = 100.0f
        };

        public static readonly ContainerStyle SearchContainer = Popup.Item;

        public static readonly TextInputStyle SearchTextBox = new()
        {
            FontSize = Control.TextSize,
            TextColor = Control.Text.Color,
            SelectionColor = Palette.TextSelection,
            PlaceholderColor = Control.PlaceholderText.Color
        };

        public static readonly ContainerStyle CommandList = new()
        {
            Height = Popup.Item.Height.Value * 10,
        };

        public static readonly ContainerStyle Command = new()
        {
            Height = Popup.Item.Height,
            Padding = EdgeInsets.Right(8),
        };

        public static readonly ContainerStyle Icon = new()
        {
            Width = Popup.Item.Height,
            Height = Popup.Item.Height,
            Padding = EdgeInsets.All(6f),
        };

        public static readonly ContainerStyle SelectedCommand =
            Command with { Color = Control.SelectedFill.Color };
    }

    // :shape
    public static class Shape
    {
        public const float AnchorSize = 0.14f;
        public const float AnchorHitSize = AnchorSize * 2.0f;
        public const float SegmentLineWidth = 0.02f;
        public const float SegmentHitSize = SegmentLineWidth * 12.0f;

        public static readonly Color ControlPointColor = Color.FromRgb(0xff7900);
        public static readonly Color ControlPointLineColor = Color.FromRgb(0xfd970e);
        public const float ControlPointSize = 0.06f;
        public const float ControlPointLineWidth = 0.01f;
    }

    // :skeleton
    public static class Skeleton
    {
        public static readonly Color BoneColor = Color.FromRgb(0x212121);
        public static readonly Color SelectedBoneColor = Palette.Primary;
        public const float BoneLineWidth = 0.015f;
        public const float BoneBaseRatio = 0.08f;
        public const float BoneHitThreshold = 0.2f;

        public static readonly Color JointColor = Color.Black;
        public const float JointSize = 0.22f;
        public const float JointHitSize = JointSize * 2.0f;

        public static readonly Color ParentLineColor = Color.FromRgb(0x212121);
    }

    // :toolbar
    public static class Toolbar
    {
        public static readonly ContainerStyle Root = new()
        {
            Padding = EdgeInsets.Symmetric(4, Control.Spacing + PanelOld.BorderWidth),
            Spacing = Control.Spacing,
            Height = Size.Fit,
            Margin = EdgeInsets.Top(PanelOld.VerticalSpacing)
        };

        public static readonly ContainerStyle Spacer = new()
        {
            Width = 1,
            Color = Palette.Active,
            Margin = EdgeInsets.LeftRight(2)
        };
    }

    public static class Shortcut
    {
        public static readonly ContainerStyle ListContainer = new()
        {
            Spacing = 2.0f,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Width = Size.Fit
        };

        public static readonly ContainerStyle RootContainer = new()
        {
            MinWidth = 24.0f,
            Height = 24.0f,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Padding = EdgeInsets.LeftRight(8),
            BorderRadius = 4f
        };

        public static readonly LabelStyle Text = Control.Text with { Color = Palette.Label };
    }

    // :contextmenu
    public static class ContextMenu
    {
        public const float SeparatorHeight = 1f;
        public const float SeparatorSpacing = 4f;
        public static readonly Color TitleColor = Palette.Disabled;
        public static readonly Color SeparatorColor = Palette.Active;
        public static readonly ContainerStyle Menu = Popup.Root with {
            Size = Size2.Fit,
            AlignX = Align.Min,
            AlignY = Align.Min,
        };

        public static readonly ContainerStyle Item = Popup.Item with
        {
            Width = Size.Default,
            MinWidth = 140.0f,
            Spacing = 0.0f,
            Padding = EdgeInsets.LeftRight(4.0f, 4.0f)
        };

        public static readonly ContainerStyle ItemRight = ContainerStyle.Default with
        {
            Width = 120f
        };

        public static PopupMenuStyle Style = new()
        {
            BackgroundColor = Popup.FillColor,
            BorderRadius = 6f,
            BorderWidth = 1.0f,
            BorderColor = Popup.BorderColor,
            Padding = EdgeInsets.Symmetric(4, 0),
            MinWidth = 180,
            ItemHeight = Control.Height,
            ItemPadding = EdgeInsets.LeftRight(10.0f, 10.0f),
            ItemContentPadding = Control.ContentPadding,
            ItemContentSpacing = 8,
            ItemHoverColor = Palette.Active,
            FontSize = Control.TextSize,
            TextColor = Palette.Content,
            DisabledTextColor = Palette.Disabled,
            IconSize = Control.IconSize,
            CheckWidth = Control.Height / 2,
            SeparatorHeight = 1,
            SeparatorMargin = EdgeInsets.Symmetric(2, 4),
            SeparatorColor = Palette.Active,
            TitleFontSize = Control.TextSize,
            TitleColor = Palette.SecondaryText,
            TitlePadding = EdgeInsets.LeftRight(10.0f),
            ShortcutColor = Palette.Placeholder,
            SubmenuSpacing = Control.Spacing,
        };
    }

    // :notification
    public static class Notifications
    {
        public readonly static ContainerStyle Wrapper = new()
        {
            AlignX = Align.Max,
            AlignY = Align.Max,
            Padding = EdgeInsets.BottomRight(Workspace.Padding),
        };

        public readonly static ContainerStyle Root = new()
        {
            Width = 240.0f,
            Height = Size.Fit,
            Spacing = Control.Spacing,
        };

        public readonly static ContainerStyle Notification = new()
        {
            Height = Control.Height + 8,
            Padding = EdgeInsets.Symmetric(4, 12),
            Color = Popup.FillColor,
            BorderRadius = PanelOld.BorderRadius
        };

        public readonly static ImageStyle NotificationIcon = Control.Icon;
        public readonly static LabelStyle NotificationText = Control.Text;
        public readonly static LabelStyle NotificationErrorText = Control.Text with { Color = ErrorColor };
    }

    // :confirm
    public static class Confirm
    {
        public static readonly ContainerStyle Backdrop = new()
        {
            AlignX = Align.Center,
            AlignY = Align.Center,
            Color = new Color(0, 0, 0, 0.4f),
        };

        public static readonly ContainerStyle Root = Popup.Root with
        {
            AlignX = Align.Center,
            Width = Size.Fit,
            Height = Size.Fit,
            Spacing = 24,
            Padding = EdgeInsets.Symmetric(24, 36)
        };

        public static readonly LabelStyle MessageLabel = Control.Text;

        public static readonly ContainerStyle ButtonContainer = new()
        {
            AlignX = Align.Center,
            Width = Size.Fit,
            Height = Control.Height,
            Spacing = 24
        };
    }

    // :documenteditor
    public static class DocumentEditor
    {
        public static readonly ContainerStyle Root = new()
        {
            Color = Palette.PageBG,
            AlignY = Align.Max,
            Height = Size.Fit
        };
    }

    // :inspector
    public static class Inspector
    {
        // Inspector-specific sizes (28px controls, not the global 40px)
        public const float FontSize = 11f;
        public const float HeaderFontSize = 12f;
        public const float LabelFontSize = 9f;
        public const float IconSize = 12f;
        public const float BorderRadius = 4f;
        public const float BodyPaddingV = 10f;
        public const float BodyPaddingH = 12f;
        public const float BodyGap = 6f;
        public const float HeaderGap = 6f;
        public const float LabelWidth = 80f;
        public const float SectionHeaderHeight = SmallWidget.Height;

        public static readonly ContainerStyle Root = Panel with
        {
            Width = 300.0f,
            Padding = EdgeInsets.Symmetric(BodyPaddingV, BodyPaddingH)
        };

        public static readonly ContainerStyle SectionHeader = new()
        {
            Height = SectionHeaderHeight,
            Color = Palette.Header,
            Padding = EdgeInsets.LeftRight(8),
            Spacing = HeaderGap,
        };

        public static readonly LabelStyle SectionText = new()
        {
            FontSize = HeaderFontSize,
            Color = Palette.HeaderText,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public static readonly ImageStyle ChevronIcon = new()
        {
            Color = Palette.HeaderText,
            Size = IconSize,
            AlignX = Align.Center,
            AlignY = Align.Center
        };

        public static readonly ImageStyle SectionIcon = ChevronIcon;

        public static readonly ContainerStyle SectionHeaderActive = SectionHeader with
        {
            Color = Palette.Active
        };

        public static readonly LabelStyle SectionTextActive = SectionText with
        {
            Color = Palette.Content
        };

        public static readonly ContainerStyle Section = new()
        {
            Height = Size.Fit,
        };

        public static readonly ContainerStyle Property = new()
        {
            Height = Control.Height,
        };

        public static readonly LabelStyle PropertyName = new()
        {
            FontSize = LabelFontSize,
            Color = Palette.Label,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle Content = new()
        {
            Spacing = Control.Spacing,
            Padding = EdgeInsets.Symmetric(4, Control.Spacing)
        };

        public static readonly ContainerStyle Row = new()
        {
            Height = Size.Fit,
            MinHeight = Control.Height,
            Padding = EdgeInsets.LeftRight(4),
            Spacing = BodyGap
        };

        public static readonly ContainerStyle ColorButton = new()
        {
            Size = Icon.Primary.Size,
            BorderRadius = 2,
            AlignY = Align.Center
        };

        public static readonly TextInputStyle TextBox = EditorStyle.TextInput;
        public static readonly TextInputStyle TextArea = EditorStyle.TextArea;

        public static readonly ToggleStyle Toggle = new()
        {
            Size = 24,
            IconSize = Control.IconSize,
            Spacing = 8,
            FontSize = Control.TextSize,
            BorderRadius = 4,
            BorderWidth = 0,
            Color = Palette.PageBG,
            CheckedColor = Palette.Active,
            BorderColor = Color.Transparent,
            ContentColor = Palette.Content,
            CheckColor = Palette.Content,
            Resolve = (s, f) =>
            {
                if ((f & WidgetFlags.Hovered) != 0)
                    s.Color = Palette.Active;
                return s;
            },
        };

        public static readonly ContainerStyle FieldContainer = new()
        {
            Width = 90f,
            AlignY = Align.Center
        };

        public static readonly LabelStyle Label = new()
        {
            FontSize = LabelFontSize,
            Color = Palette.Label,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle Separator = new()
        {
            Height = 1,
            Margin = EdgeInsets.Symmetric(Control.Spacing / 2, 0),
            Color = Palette.Active
        };

        public static readonly ContainerStyle EmitterTab = new()
        {
            Height = Control.Height,
            Padding = EdgeInsets.LeftRight(10),
            BorderRadius = BorderRadius
        };

        public static readonly ContainerStyle EmitterTabFill = new()
        {
            Color = Palette.PageBG,
            BorderRadius = BorderRadius
        };

        public static readonly ContainerStyle EmitterTabSelected = EmitterTabFill with
        {
            Color = Palette.Active
        };

        public static readonly ContainerStyle EmitterTabHover = EmitterTabFill with
        {
            Color = Palette.Header
        };

        public static readonly LabelStyle EmitterTabText = new()
        {
            FontSize = FontSize,
            Color = Palette.Content,
            AlignX = Align.Center,
            AlignY = Align.Center
        };
    }

    // :slider
    public static class Slider
    {
        public const float TrackHeight = 6;
        public const float ThumbSize = 14;

        public static readonly SliderStyle Style = new()
        {
            Height = Control.Height,
            TrackHeight = TrackHeight,
            ThumbSize = ThumbSize,
            TrackColor = Palette.PageBG,
            FillColor = Palette.Primary,
            ThumbColor = Palette.Content,
            Step = 0.05f,
        };
    }

    // :spriteeditor
    public static class SpriteEditor
    {
        public const float LayerColumnWidth = 200f;

        public static readonly ContainerStyle Toolbar = new()
        {
            Height = Control.Height,
        };

        public static readonly ContainerStyle LayerToolbar = new()
        {
            Width = LayerColumnWidth,
            Height = Control.Height,
        };

        public static readonly ContainerStyle LayerRow = new()
        {
            Height = SmallWidget.Height,
        };

        public static readonly ContainerStyle LayerNameContainer = new()
        {
            Width = LayerColumnWidth,
            Padding = EdgeInsets.LeftRight(4),
        };

        public static readonly ContainerStyle LayerNameContainerActive = LayerNameContainer with
        {
            Color = Palette.Primary
        };

        public static readonly Color UndefinedColor = new(0f, 0f, 0f, 0.1f);
        public const float ButtonSize = 40f;
        public const float ButtonMarginY = 6f;
        public const float ColorPickerBorderWidth = 2.5f;
        public const float ColorSize = Control.Height;
        public const float ColorPickerWidth = ColorSize * 64 + ColorPickerBorderWidth * 2;
        public const float ColorPickerHeight = ColorSize + ColorPickerBorderWidth * 2;
        public const float ColorPickerSelectionBorderWidth = 3f;
        public static readonly Color BoneOriginColor = Color.White;

        public static readonly ContainerStyle ColorPicker = new()
        {
            Padding = EdgeInsets.All(4f),
            BorderRadius = PanelOld.ContentBorderRadius
        };

        public static readonly ContainerStyle PaletteColor = new()
        {
            Width = ColorSize,
            Height = ColorSize,
            Padding = EdgeInsets.All(1.0f),
        };

        public static readonly ContainerStyle PaletteSelectedColor = new()
        {
            BorderRadius = 4f,
            Color = Palette.Primary,
            Margin = EdgeInsets.All(-1.5f),
        };

        public static readonly ContainerStyle PaletteDisplayColor = new()
        {
            BorderRadius = 4f
        };

        public static readonly ContainerStyle OpacityButtonRoot = new()
        {
            Margin = EdgeInsets.Left(Control.Spacing),
            Width = ColorSize * 2,
            Height = ColorSize * 2,
            AlignY = Align.Max
        };

        public static readonly ContainerStyle OpacityButtonIconContainer = new()
        {
            Padding = EdgeInsets.All(6)
        };

        public static readonly PopupStyle OpacityPopup = new()
        {
            AnchorX = Align.Min,
            AnchorY = Align.Min,
            PopupAlignX = Align.Min,
            PopupAlignY = Align.Max,
            Spacing = 2,
            ClampToScreen = true
        };

        public static readonly PopupStyle PalettePopup = new()
        {
            AnchorX = Align.Max,
            AnchorY = Align.Min,
            PopupAlignX = Align.Max,
            PopupAlignY = Align.Max,
            Spacing = 2,
            ClampToScreen = true
        };

        public readonly static ContainerStyle OpacityPopupRoot = Popup.Root;

        public readonly static ContainerStyle ConstraintsPopupRoot = Popup.Root;
    }

    // :colorpicker
    public static class ColorPicker
    {
        private const float Width = 300;
        private const float Padding = 8;
        public const float SVSize = Width - 4;
        public const float SliderWidth = Width - Padding * 2;
        public const float SliderHeight = 20;

        public readonly static ContainerStyle Root = Popup.Root with
        {
            Spacing = Control.Spacing * 2,
            Padding = Padding,
            Width = Width,
            Height = Size.Fit
        };

        public readonly static ContainerStyle SaturationAndValue = new()
        {
            Size = SVSize,
            Margin = EdgeInsets.LeftRight(-(Padding - 2)),
            Clip = true
        };

        public readonly static ContainerStyle Slider = new()
        {
            Height = SliderHeight,
            Clip = true,
        };

        public readonly static ImageStyle SliderImage = ImageStyle.Fill with { BorderRadius = 10 };
    }

    // :dopesheet
    public static class Dopesheet
    {
        public const float FrameWidth = 18.0f;
        public const float FrameHeight = SmallWidget.Height;
        public const float FrameSpacerWidth = 1.0f;

        public static readonly ContainerStyle FrameDot = new()
        {
            Width = 9f,
            Height = 9f,
            AlignX = Align.Center,
            AlignY = Align.Max,
            Margin = EdgeInsets.Bottom(6f),
            Color = Color.FromRgb(0x252525),
            BorderRadius = 6
        };

        public static readonly ContainerStyle SelectedFrameDot = FrameDot with { Color = Color.Black };

        public static readonly ContainerStyle HeaderContainer = new()
        {
            Height = Control.Height
        };

        public static readonly ContainerStyle TimeBlock = new()
        {
            Width = FrameWidth * 4 + (FrameSpacerWidth * 3),
            Padding = EdgeInsets.BottomLeft(0, 3)
        };

        public static readonly LabelStyle TimeText = Text.SecondarySmall with
        {
            AlignY = Align.Max
        };

        public static readonly ContainerStyle FrameContainer = new()
        {
            Height = FrameHeight,
            Color = Color.FromRgb(0x2f2f2f)
        };

        public static readonly ContainerStyle Frame = new()
        {
            Width = FrameWidth,
            Color = Color.FromRgb(0x909090)
        };

        public static readonly ContainerStyle SelectedFrame = Frame with
        {
            Color = Palette.Primary
        };

        public static readonly ContainerStyle FrameSeparator = new()
        {
            Width = 1,
            Color = Color.FromRgb(0x252525)
        };

        public static readonly ContainerStyle HoldSeparator = new()
        {
            Width = 1,
            Color = Frame.Color
        };

        public static readonly ContainerStyle SelectedHoldSeparator = HoldSeparator with
        {
            Color = SelectedFrame.Color
        };

        public static readonly ContainerStyle EmptyFrame = Frame with
        {
            Color = Color.Transparent
        };

        public static readonly ContainerStyle FourthEmptyFrame = EmptyFrame with
        {
            Color = Color.FromRgb(0x3a3a3a)
        };

        public static readonly ContainerStyle LayerSeparator = new()
        {
            Height = FrameSpacerWidth,
            Color = Color.FromRgb(0x252525)
        };
    }

    // :tool
    public static class Tool
    {
        public static readonly Color LineColor = Color.FromRgba(0xf5f04e, 0.7f);
        public static readonly Color PointColor = Color.FromRgb(0x63ffff);
        public const float PointSize = Shape.AnchorSize * 1.5f;
    }

    // :knifetool
    public static class KnifeTool
    {
        public static readonly Color AnchorColor = Color.FromRgb(0x000000);
        public static readonly Color IntersectionColor = Color.FromRgb(0x4ea64e);
        public static readonly Color InvalidSegmentColor = Color.FromRgb(0x953d49);
        public const float IntersectionAnchorScale = 1.2f;
    }

    // :renametool
    public static class RenameTool
    {
        public static readonly ContainerStyle Root = new()
        {
            AlignX = Align.Min,
            AlignY = Align.Min,
            Width = 200.0f,
            Height = Control.Height + Control.ContentPadding * 2
        };

        public readonly static ContainerStyle Content = new()
        {
            Width = Size.Fit,
            Height = Size.Fit,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Color = Popup.FillColor,
            Padding = EdgeInsets.Symmetric(Control.ContentPadding, Control.ContentPadding * 2),
            BorderRadius = Popup.Root.BorderRadius, BorderWidth = Popup.Root.BorderWidth, BorderColor = Popup.Root.BorderColor
        };

        public static readonly TextInputStyle Text = new()
        {
            Width = Size.Fit,
            FontSize = 17.0f,
            TextColor = Control.Text.Color,
            SelectionColor = Palette.TextSelection
        };
    }

    public static void Init()
    {
        ContextMenu.Style.CheckIcon = EditorAssets.Sprites.IconCheck;
        ContextMenu.Style.SubmenuIcon = EditorAssets.Sprites.IconSubmenu;
        DropDown.ArrowIcon = EditorAssets.Sprites.IconFoldoutClosed;

        Style.DropDown = DropDown;
        Style.PopupMenu = ContextMenu.Style;
    }

    public static void Shutdown()
    {
    }
}
