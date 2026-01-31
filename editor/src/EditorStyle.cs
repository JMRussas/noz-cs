//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;


public static class EditorStyle
{
    #region Canvas
    public static class CanvasId
    {
        public const byte CommandPalette = 1;
        public const byte ContextMenu = 2;
        public const byte Confirm = 3;
        public const byte DocumentEditor = 4;
        public const byte Workspace = 5;
    }
    #endregion

    public static readonly Color SelectionColor = Color.FromRgb(0x54a3f6);

    // Toggle Button
    public const float ToggleButtonHeight = ButtonHeight;
    public const float ToggleButtonPadding = 6f;
    public const float ToggleButtonBorderRadius = 8f;

    private static EditorColors _colors = null!;

    public static EditorColors Current => _colors;

    // Legacy colors (deprecated but still used)
    public static readonly Color Origin = Color.FromRgb(0xFF9F2C);
    public static readonly Color Selected = Color.White;
    public static readonly Color Center = new(1f, 1f, 1f, 0.5f);

    // Color32 versions for rendering
    public static readonly Color EdgeColor = new(0x00, 0x00, 0x00);

    // UI Colors
    public static readonly Color UIBackground = Color.FromRgb(0x262525);
    public static readonly Color UIBorder = Color.FromRgb(0x2c323c);
    public static readonly Color UIText = Color.FromRgb(0xdcdfe4);
    public static readonly Color UIErrorText = Color.FromRgb(0xdf6b6d);
    public static readonly Color UIButtonHover = Color.FromRgb(0x76a8ff);
    public static readonly Color UIButton = new(0.9f, 0.9f, 0.9f);
    public static readonly Color UIButtonText = UIBackground;

    // Text
    public static readonly Color TextColor = Color.FromRgb(0xb4b4aa);
    public const int TextFontSize = 14;

    // Icon
    public static readonly Color IconColor = Color.FromRgb(0xb4b4aa);

    // Error
    public static readonly Color ErrorColor = Color.FromRgb(0xdf6b6d);

    // Mesh
    public const float MeshEdgeWidth = 0.02f;
    public const float MeshVertexSize = 0.12f;
    public const float MeshWeightOutlineSize = 0.20f;
    public const float MeshWeightSize = 0.19f;


    // Button
    public const float ButtonPadding = 8f;
    public const float ButtonHeight = 32f;
    public const float ButtonBorderRadius = 8f;

    // :workspace
    public static class Workspace
    {
        public static readonly Color SelectionColor = Color.Cyan;
        public static readonly Color FillColor = Color.FromRgb(0x464646);
        public static readonly Color GridColor = Color.FromRgb(0x686868);
        public static readonly Color BoundsColor = Color.FromRgb(0x212121);
        public static readonly Color OriginColor = Color.FromRgb(0xff9f2c);
        public const float OriginSize = 0.1f;
        public const float DocumentBoundsLineWidth = 0.015f;
        public const float Padding = 16f;
        public static readonly Color NameColor = Color.White;
        public const float NameSize = 0.36f;
        public const float NamePadding = 0.26f;
        public const float NameOutline = 0.1f;
        public static readonly Color NameOutlineColor = Color.Black;
        public const float GridAlpha = 0.4f;
        public const float GridZeroAlpha = 0.5f;
    }

    // :control
    public static class Control
    {
        public const float TextSize = 16.0f;
        public const float Height = 40.0f;
        public const float BorderRadius = 10.0f;
        public const float Spacing = 5.0f;
        public const float ContentPadding = 4.0f;
        public const float ContentHeight = Height - ContentPadding * 2;

        public static readonly ContainerStyle Root = new()
        {
            Height = Height
        };

        public static readonly ContainerStyle Fill = new()
        {
            Color = Color.FromRgb(0x1d1d1d),
            Border = new BorderStyle { Radius = BorderRadius }
        };

        public static readonly ContainerStyle Content = new()
        {
            Padding = EdgeInsets.All(ContentPadding),
            Spacing = Spacing
        };

        public static readonly ContainerStyle ContentNoPadding = Content with { Padding = EdgeInsets.Zero };

        public static readonly ContainerStyle HoverFill = Fill with { Color = Color.FromRgb(0x3f3f3f) };
        public static readonly ContainerStyle SelectedFill = Fill with { Color = Color.FromRgb(0x545454) };
        public static readonly ContainerStyle SelectedHoverFill = Fill with { Color = Color.FromRgb(0x545454) };
        public static readonly ContainerStyle DisabledFill = Fill with { Color = Color.Transparent };

        public readonly static LabelStyle Text = new()
        {
            FontSize = TextSize,
            Color = Color.FromRgb(0xebebeb),
            AlignX = Align.Min,
            AlignY = Align.Center
        };
        public readonly static LabelStyle DisabledText = Text with { Color = Color.FromRgb(0x3e3e3e) };
        public readonly static LabelStyle HoveredText = Text with { Color = Color.FromRgb(0xffffff) };
        public readonly static LabelStyle SelectedText = Text with { Color = Color.FromRgb(0xffffff) };

        public readonly static LabelStyle PlaceholderText = Text with { Color = Color.FromRgb(0x575757) };
        public readonly static LabelStyle PlaceholderHoverText = Text with { Color = Color.FromRgb(0x888888) };
        public readonly static LabelStyle PlaceholderSelectedText = Text with { Color = Color.FromRgb(0xcccccc) };

        public static readonly ContainerStyle IconContainer = new()
        {
            Width = ContentHeight,
            Height = ContentHeight,
            Padding = EdgeInsets.All(2f),
        };

        public static readonly ImageStyle Icon = new()
        {
            Color = Color.FromRgb(0xebebeb),
            AlignX = Align.Center,
            AlignY = Align.Center,
        };

        public static readonly ImageStyle SelectedIcon = Icon with { Color = Color.FromRgb(0xffffff) };
        public static readonly ImageStyle DisabledIcon = Icon with { Color = Color.FromRgb(0x3e3e3e) };
        public static readonly ImageStyle HoveredIcon = Icon with { Color = Color.FromRgb(0xffffff) };
    }

    // :panel
    public static class Panel
    {
        public const float BorderRadius = 14f;
        public const float BorderWidth = 2.0f;
        public const int TextSize = 12;
        public const float ContentBorderRadius = 9f;
        public const float VerticalSpacing = 4.0f;

        public static readonly EdgeInsets BottomMargin = EdgeInsets.Bottom(-BorderRadius / 2);

        public static readonly ContainerStyle Root = new()
        {
            Color = Color.FromRgb(0x323232),
            Border = new BorderStyle { Radius = BorderRadius, Width = BorderWidth, Color = Color.Black10Pct },
            Padding = EdgeInsets.All(BorderWidth)
        };

        public static readonly ContainerStyle Content = new()
        {
            //Color = Color.FromRgb(0x232323),
            Spacing = Control.Spacing,
            Margin = EdgeInsets.TopBottom(VerticalSpacing),
            Padding = EdgeInsets.All(Control.Spacing)
        };

        public static readonly ContainerStyle UnpaddedContent = Content with
        {
            Padding = EdgeInsets.Zero
        };
    }

    // :popup
    public static class Popup
    {
        public static readonly Color FillColor = Color.FromRgb(0x181818);
        public static readonly Color BorderColor = Color.FromRgb(0x272727);
        public readonly static ContainerStyle Root = new()
        {
            AlignX = Align.Center,
            AlignY = Align.Center,
            Padding = EdgeInsets.All(Control.Spacing),
            Color = FillColor,
            Border = new BorderStyle { Radius = 14f, Width = 1.0f, Color = BorderColor }
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
            Color = Color.FromRgb(0x2f2f2f)
        };
        public readonly static LabelStyle Title = new()
        {
            FontSize = Control.TextSize,
            AlignX = Align.Min,
            AlignY = Align.Center,
            Color = Color.FromRgb(0x999999),
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
        public static readonly ContainerStyle Root = new()
        {
            Width = Size.Fit,
            MinWidth = 100.0f,
            Height = Control.Height
        };

        public static readonly ContainerStyle RootWithIcon = new()
        {
            Width = Control.Height,
            Height = Control.Height
        };

        public static readonly ContainerStyle RootWithContent = new()
        {
            Width = Size.Fit,
            Height = Control.Height
        };

        public static readonly ContainerStyle Fill = new()
        {
            Border = new BorderStyle { Radius = Control.BorderRadius },
            Color = Color.FromRgb(0x262626)
        };

        public static readonly ContainerStyle HoverFill = Fill with { Color = Color.FromRgb(0x3f3f3f) };
        public static readonly ContainerStyle SelectedFill = Fill with { Color = Color.FromRgb(0x545454) };
        public static readonly ContainerStyle SelectedHoverFill = Fill with { Color = Color.FromRgb(0x545454) };
        public static readonly ContainerStyle DisabledFill = Fill with { Color = Color.Transparent };

        public static readonly ContainerStyle TextContent = Control.Content;
        public static readonly ContainerStyle IconContent = new()
        {
            Width = Control.Height,
            Height = Control.Height,
            Padding = EdgeInsets.All(7)
        };
        public static readonly ContainerStyle Content = new()
        {
            Padding = EdgeInsets.LeftRight(3)
        };
    }

    public static class List
    {
        public const float ItemHeight = 32f;
        public const float ItemPadding = 8f;
        public const float ItemTextSize = 14.0f;
        public static Color ItemSelectedFillColor => _colors.ListItemSelectedFillColor;
        public static Color ItemSelectedTextColor => _colors.ListItemSelectedTextColor;
        public static Color ItemTextColor => _colors.ListItemTextColor;
        public static Color HeaderTextColor => _colors.ListHeaderTextColor;
    }



    public static class BoxSelect
    {
        public const float LineWidth = 0.02f;
        public static Color LineColor => _colors.BoxSelectLineColor;
        public static Color FillColor => _colors.BoxSelectFillColor;
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

        public static readonly TextBoxStyle SearchTextBox = new()
        {
            Height = Popup.Item.Height,
            FontSize = Control.TextSize,
            TextColor = Control.Text.Color,
            SelectionColor = SelectionColor,
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
        public static Color SegmentColor => _colors.Shape.Segment;
        public static Color SelectedSegmentColor => _colors.Shape.SelectedSegment;
        public static Color AnchorColor => _colors.Shape.Anchor;
        public static Color SelectedAnchorColor => _colors.Shape.SelectedAnchor;
        public const float AnchorSize = 0.10f;
        public const float AnchorHitSize = AnchorSize * 4.0f;
        public const float SegmentLineWidth = 0.015f;
        public const float SegmentHitSize = SegmentLineWidth * 8.0f;

        public static Color ControlPointColor => _colors.Shape.SelectedAnchor;
        public static Color ControlPointLineColor => _colors.Shape.SelectedSegment;
        public const float ControlPointSize = 0.06f;
        public const float ControlPointLineWidth = 0.01f;
    }

    // :skeleton
    public static class Skeleton
    {
        public static readonly Color BoneColor = Color.White;
        public static readonly Color BoneOriginColor = Color.Black;
        public static readonly Color BoneOutlineColor = Color.Black10Pct;
        public static readonly Color SelectedBoneColor = Workspace.SelectionColor;
        public static readonly Color ParentLineColor = Color.FromRgb(0x212121);
        public const float BoneSize = 0.18f;
        public const float BoneOriginSize = 0.15f;
        public const float BoneOutlineWidth = 0.02f;
        public const float BoneHitThreshold = 0.05f;
        public const float BoneHitThresholdSqr = BoneHitThreshold * BoneHitThreshold;
    }

    // :toolbar
    public static class Toolbar
    {
        public static readonly ContainerStyle Root = new()
        {
            Padding = EdgeInsets.Symmetric(4, 2 + Panel.BorderWidth),
            Spacing = Control.Spacing,
            Height = Size.Fit,
            Margin = EdgeInsets.Top(Panel.VerticalSpacing)
        };

        public static readonly ContainerStyle Spacer = new()
        {
            Width = 1,
            Color = Color.FromRgb(0x545454),
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
            Border = new BorderStyle { Radius = 4f }
        };

        public static readonly LabelStyle Text = Control.Text with { Color = Color.FromRgb(0x767676) };
    }

    // :contextmenu
    public static class ContextMenu
    {
        public const int TextSize = 12;
        public const float SeparatorHeight = 2f;
        public const float SeparatorSpacing = 12f;
        public static Color TitleColor => _colors.ContextMenuTitleColor;
        public static Color SeparatorColor => _colors.ContextMenuSeparatorColor;
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
    }

    // :notification
    public static class Notifications
    {
        public readonly static ContainerStyle Root = new()
        {
            AlignX = Align.Max,
            AlignY = Align.Max,
            Width = 240.0f,
            Height = Size.Fit,
            Margin = EdgeInsets.BottomRight(EditorStyle.Workspace.Padding),
            Spacing = Control.Spacing,
        };

        public readonly static ContainerStyle Notification = new()
        {
            Height = Control.Height + 8,
            Padding = EdgeInsets.Symmetric(4, 12),
            Color = Popup.FillColor,
            Border = new BorderStyle { Radius = Panel.BorderRadius }
        };

        public readonly static LabelStyle NotificationText = Control.Text;
        public readonly static LabelStyle NotificationErrorText = Control.Text with { Color = EditorStyle.ErrorColor };
    }

    // :confirm
    public static class Confirm
    {
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
        public static readonly ContainerStyle Root = Panel.Root with
        {
            AlignX = Align.Center,
            AlignY = Align.Max,
            Height = Size.Fit,
            Width = Size.Fit,
            MinWidth = 100,
            Margin = Panel.BottomMargin,
            Padding = EdgeInsets.Bottom(Control.Spacing * 3)
        };
    }

    // :animationeditor
    public static class AnimationEditor
    {
    }

    // :skeletoneditor
    public static class SkeletonEditor
    {
    }


    // :spriteeditor
    public static class SpriteEditor
    {
        public static readonly Color UndefinedColor = new(0f, 0f, 0f, 0.1f);
        public const float ButtonSize = 40f;
        public const float ButtonMarginY = 6f;
        public const float ColorPickerBorderWidth = 2.5f;
        public const float ColorSize = Control.Height;
        public const float ColorPickerWidth = ColorSize * 64 + ColorPickerBorderWidth * 2;
        public const float ColorPickerHeight = ColorSize + ColorPickerBorderWidth * 2;
        public const float ColorPickerSelectionBorderWidth = 3f;
        public static readonly Color BoneOriginColor = Color.White;
        public static readonly Color SelectedOriginColor = Shape.SelectedAnchorColor;

        public static readonly ContainerStyle ColorPicker = new()
        {
            Padding = EdgeInsets.All(4f),
            //Color = Panel.ContentColor,
            Border = new BorderStyle { Radius = Panel.ContentBorderRadius }
        };

        public static readonly ContainerStyle Palette = new()
        {

        };

        public static readonly ContainerStyle PaletteColor = new()
        {
            Width = ColorSize,
            Height = ColorSize,
            Padding = EdgeInsets.All(1.0f),
        };

        public static readonly ContainerStyle PaletteSelectedColor = new()
        {
            Border = new BorderStyle { Radius = 8f },
            Color = SelectionColor,
            Margin = EdgeInsets.All(-1.5f),
        };

        public static readonly ContainerStyle PaletteDisplayColor = new()
        {
            Border = new BorderStyle { Radius = 6f }
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

    // :atlaseditor
    public static class AtlasEditor
    {
    }

    // :dopesheet
    public static class Dopesheet
    {
        public const float FrameWidth = 18.0f;
        public const float FrameHeight = 27.0f;
        public const float FrameSpacerWidth = 1.0f;

        public static readonly ContainerStyle FrameDot = new()
        {
            Width = 9f,
            Height = 9f,
            AlignX = Align.Center,
            AlignY = Align.Max,
            Margin = EdgeInsets.Bottom(6f),
            Color = Color.FromRgb(0x252525),
            Border = { Radius = 6 }
        };

        public static readonly ContainerStyle SelectedFrameDot = FrameDot with { Color = Color.Black };

        public static readonly ContainerStyle HeaderContainer = new()
        {
            Height = FrameHeight
        };

        public static readonly ContainerStyle TimeBlock = new()
        {
            Width = FrameWidth * 4 + (FrameSpacerWidth * 3),
            Padding = EdgeInsets.BottomLeft(3, 3)
        };

        public static readonly LabelStyle TimeText = new()
        {
            Color = Color.FromRgb(0x949494),
            FontSize = 15.0f
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
            Color = SelectionColor
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
            AlignX = Align.Center,
            AlignY = Align.Center,
            Color = Popup.FillColor,
            Padding = EdgeInsets.Symmetric(Control.ContentPadding, Control.ContentPadding * 2),
            Border = Popup.Root.Border
        };

        public static readonly TextBoxStyle Text = new()
        {
            FontSize = 17.0f,
            TextColor = Control.Text.Color,
            SelectionColor = SelectionColor,
            Height = Control.Height
        };
    }

    // Style accessors
    public static Color SelectionTextColor => _colors.SelectionTextColor;
    public static Color ButtonColor => _colors.ButtonColor;
    public static Color ButtonHoverColor => _colors.ButtonHoverColor;
    public static Color ButtonTextColor => _colors.ButtonTextColor;
    public static Color ButtonCheckedColor => _colors.ButtonCheckedColor;
    public static Color ButtonCheckedTextColor => _colors.ButtonCheckedTextColor;
    public static Color ButtonDisabledColor => _colors.ButtonDisabledColor;
    public static Color ButtonDisabledTextColor => _colors.ButtonDisabledTextColor;

    public static void Init()
    {
        _colors = EditorColors.Dark;
    }

    public static void Shutdown()
    {
    }
}

