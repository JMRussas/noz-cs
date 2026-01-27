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

    public static readonly Color SelectionColor = Color.FromRgb(0x4772b3);

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
        public static readonly Color FillColor = Color.FromRgb(0x3f3f3f);
        public static readonly Color GridColor = Color.FromRgb(0x4e4e4e);
        public static readonly Color DocumentBoundsColor = Color.FromRgb(0x212121);
        public static readonly Color SelectedDocumentBoundsColor = Color.FromRgb(0xfd970e);
        public static readonly Color OriginColor = Color.FromRgb(0xff9f2c);
        public const float OriginSize = 0.06f;
        public const float DocumentBoundsLineWidth = 0.015f;
        public const float Padding = 16f;
        public const float NameSize = 0.24f;
        public const float NamePadding = 0.26f;
        public const float GridAlpha = 0.4f;
        public const float GridZeroAlpha = 0.5f;
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
            Padding = EdgeInsets.Symmetric(8.0f, 4.0f),
            Color = Popup.FillColor,
            Border = new BorderStyle { Radius = Control.BorderRadius, Width = 1.0f, Color = BorderColor }
        };
        public readonly static ContainerStyle Item = new()
        {
            Height = 19.0f,
            Width = Size.Fit,
            Spacing = Control.Spacing
        };
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
        public readonly static LabelStyle Text = new()
        {
            FontSize = Control.TextSize,
            Color = Color.FromRgb(0xdcdcdc),
            AlignX = Align.Min,
            AlignY = Align.Center
        };
        public readonly static LabelStyle DisabledText = Text with { Color = Color.FromRgb(0x767676) };
        public readonly static LabelStyle HoveredText = Text with { Color = Color.FromRgb(0xfdfdfd) };
        public readonly static LabelStyle SelectedText = Text with { Color = Color.White };

        public static readonly ContainerStyle IconContainer = new()
        {
            Width = Item.Height,
            Height = Item.Height,
            Padding = EdgeInsets.All(2f),
        };

        public static readonly ImageStyle Icon = new()
        {
            Color = Color.FromRgb(0x868686),
            AlignX = Align.Center,
            AlignY = Align.Center,
        };
        public static readonly ImageStyle SelectedIcon = Icon with { Color = Color.White };
        public static readonly ImageStyle HoveredIcon = Icon with { Color = Color.FromRgb(0xdedede) };
        public static readonly ImageStyle DisabledIcon = Icon with { Color = Color.FromRgb(0x5b5b5b) };
    }

    // :control
    public static class Control
    {
        public const float TextSize = 11.0f;
        public const float Height = 20.0f;
        public const float BorderRadius = 6.0f;
        public const float Spacing = 5.0f;
        public static Color TextColor => _colors.ControlTextColor;
        public static readonly Color IconColor = Color.FromRgb(0xe6e6e6);
        public static readonly Color SelectedIconColor = Color.White;
        public static Color FillColor => _colors.ControlFillColor;
        public static Color SelectedFillColor => _colors.ControlSelectedFillColor;
        public static Color PlaceholderTextColor => _colors.ControlPlaceholderTextColor;

        public static readonly LabelStyle Text = new()
        {
            FontSize = TextSize,
            Color = TextColor,
            AlignX = Align.Min,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle Root = new()
        {
        };

        public static readonly ContainerStyle Fill = new()
        {
            Color = Color.FromRgb(0x282828),
            Border = new BorderStyle { Radius = BorderRadius }
        };

        public static readonly ContainerStyle Content = new()
        {
            Padding = EdgeInsets.All(2)
        };

        public static readonly ContainerStyle HoverFill = Fill with { Color = Color.FromRgb(0x454545) };
        public static readonly ContainerStyle SelectedFill = Fill with { Color = Color.FromRgb(0x4772b3) };
        public static readonly ContainerStyle SelectedHoverFill = Fill with { Color = Color.FromRgb(0x628bca) };
        public static readonly ContainerStyle DisabledFill = Fill with { Color = Color.FromRgb(0x484848) };
    }

    // :button
    public static class Button
    {
        public static readonly ContainerStyle Root = new()
        {
            Width = Size.Fit,
            MinWidth = 80.0f,
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
            Color = Color.FromRgb(0x545454)
        };

        public static readonly ContainerStyle HoverFill = Fill with { Color = Color.FromRgb(0x656565) };
        public static readonly ContainerStyle SelectedFill = Fill with { Color = Color.FromRgb(0x4772b3) };
        public static readonly ContainerStyle SelectedHoverFill = Fill with { Color = Color.FromRgb(0x628bca) };
        public static readonly ContainerStyle DisabledFill = Fill with { Color = Color.FromRgb(0x484848) };

        public static readonly ContainerStyle TextContent = new()
        {
            Padding = EdgeInsets.Symmetric(2, 4)
        };
        public static readonly ContainerStyle IconContent = new()
        {
            Width = Control.Height,
            Height = Control.Height,
            Padding = EdgeInsets.All(3)
        };
        public static readonly ContainerStyle Content = new()
        {
            Padding = EdgeInsets.LeftRight(3)
        };
        public static readonly LabelStyle Text = new()
        {
            FontSize = EditorStyle.Control.TextSize,
            Color = EditorStyle.Control.TextColor,
            AlignX = Align.Center,
            AlignY = Align.Center
        };
        public static readonly LabelStyle DisabledText = Text with { Color = Color.FromRgb(0x929292) };
        public static readonly ImageStyle Icon = new() { Color = Control.IconColor, AlignX = Align.Center, AlignY = Align.Center };
        public static readonly ImageStyle SelectedIcon = Icon with { Color = Control.SelectedIconColor };
        public static readonly ImageStyle DisabledIcon = Icon with { Color = Color.FromRgb(0x585858) };
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
            TextColor = Control.TextColor,
            SelectionColor = SelectionColor,
            PlaceholderColor = Control.PlaceholderTextColor
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
            Command with { Color = Control.SelectedFillColor };
    }

    // :shape
    public static class Shape
    {
        public static Color SegmentColor => _colors.Shape.Segment;
        public static Color SelectedSegmentColor => _colors.Shape.SelectedSegment;
        public static Color AnchorColor => _colors.Shape.Anchor;
        public static Color SelectedAnchorColor => _colors.Shape.SelectedAnchor;
        public const float AnchorSize = 0.10f;
        public const float AnchorHitSize = AnchorSize * 2.0f;
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
        public static readonly Color SelectedBoneColor = Color.FromRgb(0xfd970e);
        public static readonly Color ParentLineColor = Color.FromRgb(0x212121);
        public const float BoneWidth = 0.14f;
        public const float BoneSize = 0.12f;
        public const float BoneOriginSize = 0.11f;
        public const float BoneOutlineWidth = 0.02f;
    }

    // :overlay
    public static class Overlay
    {
        public const float BorderRadius = 10f;
        public const float BorderWidth = 2.0f;
        public const int TextSize = 12;
        public const float ContentBorderRadius = 9f;
        public const float VerticalSpacing = 4.0f;
        public static Color FillColor => _colors.Overlay.Fill;
        public static readonly Color TextColor = Color.White;
        public static Color AccentTextColor => _colors.Overlay.AccentText;
        public static Color DisabledTextColor => _colors.Overlay.DisabledText;
        public static Color IconColor => _colors.Overlay.Icon;
        public static readonly Color ContentColor = Color.FromRgb(0x232323);
        public static readonly ContainerStyle Root = new()
        {
            Color = FillColor,
            Border = new BorderStyle { Radius = BorderRadius, Width = BorderWidth, Color = Color.FromRgb(0x3d3d3d) },
            Padding = EdgeInsets.All(BorderWidth)
        };

        public static readonly ContainerStyle Toolbar = new()
        {
            Padding = EdgeInsets.LeftRight(2 + BorderWidth),
            Spacing = Control.Spacing,
            Height = Control.Height,
            Margin = EdgeInsets.Top(VerticalSpacing)
        };

        public static readonly ContainerStyle Content = new()
        {
            Color = ContentColor,
            Spacing = Control.Spacing,
            Margin = EdgeInsets.TopBottom(VerticalSpacing),
            Padding = EdgeInsets.All(Control.Spacing)
        };

        public static readonly ContainerStyle UnpaddedContent = Content with
        {
            Padding = EdgeInsets.Zero
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

        public static readonly LabelStyle Text = Popup.Text with { Color = Color.FromRgb(0x767676) };
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
            Height = Popup.Item.Height.Value + 8,
            Padding = EdgeInsets.Symmetric(4,8),
            Color = Popup.FillColor,
            Border = new BorderStyle { Radius = Overlay.BorderRadius }
        };

        public readonly static LabelStyle NotificationText = EditorStyle.Popup.Text;
        public readonly static LabelStyle NotificationErrorText = Popup.Text with { Color = EditorStyle.ErrorColor };
    }

    // :confirm
    public static class Confirm
    {
        public static readonly ContainerStyle Root = Popup.Root with
        {
            AlignX = Align.Center,
            Width = Size.Fit,
            Height = Size.Fit,
            Spacing = 12
        };

        public static readonly LabelStyle MessageLabel = new()
        {
            FontSize = Overlay.TextSize,
            Color = Overlay.TextColor,
            AlignX = Align.Center,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle ButtonContainer = new()
        {
            AlignX = Align.Center,
            Width = Size.Fit,
            Height = Control.Height,
            Spacing = Control.Spacing
        };
    }

    // :animationeditor
    public static class AnimationEditor
    {
        public const int MinFrames = 24;
        public const float FrameWidth = 13f;
        public const float FrameHeight = 26f;
        public const float FrameSpacerWidth = 1.0f;
        public const float Padding = 8f;
        public const float BorderWidth = 1f;
        public const float FrameDotSize = 4f;
        public const float TickHeight = FrameHeight * 0.4f;
        public const float ShortTickHeight = TickHeight;

        public static readonly Color BorderColor = Color.FromGrayscale(10);
        public static readonly Color FrameColor = Color.FromGrayscale(100);
        public static readonly Color FrameDotColor = Color.FromGrayscale(20);
        public static readonly Color SelectedFrameColor = SelectionColor;
        public static readonly Color EmptyFrameColor = Color.FromGrayscale(45);
        public static readonly Color TickBackgroundColor = Color.FromGrayscale(52);
        public static readonly Color TickColor = BorderColor;
        public static readonly Color TickHoverColor = new Color(1f, 1f, 1f, 0.04f);
        public static readonly Color ShortTickColor = Color.FromGrayscale(44);
        public static readonly Color ButtonColor = FrameColor;
        public static readonly Color ButtonCheckedColor = SelectionColor;
        public static readonly Color ButtonBorderColor = BorderColor;
        public static readonly Color EventColor = Color.FromGrayscale(180);

        public static readonly ContainerStyle Root = Overlay.Root with
        {
            AlignX = Align.Center,
            AlignY = Align.Max,
            Height = Size.Fit,
            Width = Size.Fit,
            Margin = EdgeInsets.Bottom(Workspace.Padding)
        };


        public static readonly ContainerStyle Tick = new()
        {
            Width = FrameWidth,
            Height = TickHeight,
            Color = TickBackgroundColor
        };

        public static readonly ContainerStyle FrameBorder = new()
        {
            Width = BorderWidth,
            Height = FrameHeight,
            Color = TickColor
        };

        public static readonly ContainerStyle TickBorder = new()
        {
            Width = BorderWidth,
            Color = TickColor
        };

        public static readonly ContainerStyle ShortTick = new()
        {
            Width = BorderWidth,
            Height = ShortTickHeight,
            AlignY = Align.Max,
            Color = ShortTickColor
        };

        public static readonly ContainerStyle HorizontalBorder = new()
        {
            Height = BorderWidth,
            Color = TickColor
        };

        public static readonly ContainerStyle FrameBlock = new()
        {
            Width = FrameWidth * 4 + (FrameSpacerWidth * 3),
            Padding = EdgeInsets.BottomLeft(3,3)
        };

        public static readonly ContainerStyle FrameBlockSeparator = new()
        {
            Width = FrameSpacerWidth,
            Color = Color.FromRgb(0x282828)
        };

        public static readonly LabelStyle FrameBlockText = new()
        {
            Color = Color.FromRgb(0xa5a5a5),
            FontSize = Control.TextSize
        };

        public static readonly ContainerStyle FrameDot = new()
        {
            Width = FrameDotSize,
            Height = FrameDotSize,
            AlignX = Align.Center,
            AlignY = Align.Max,
            Margin = EdgeInsets.Bottom(3f),
            Color = Color.FromRgb(0x282828),
            Border = { Radius = 3 }
        };


        public static readonly ContainerStyle SelectedFrameDot = FrameDot with
        {
            Color = Color.Black
        };

        public static readonly LabelStyle FrameLabel = new()
        {
            FontSize = 10f,
            Color = Control.TextColor,
            AlignX = Align.Center,
            AlignY = Align.Center
        };

        public static readonly ContainerStyle TickContainer = new()
        {
            Color = Color.FromRgb(0x1d1d1d),
            Height = Control.Height
        };

        public static readonly ContainerStyle FrameContainer = new()
        {
            Color = Color.FromRgb(0x282828),
            Width = Size.Fit,
            Height = 20.0f
        };


        public static readonly ContainerStyle Frame = new()
        {
            Width = FrameWidth,
            Color = Color.FromRgb(0x747474)
        };

        public static readonly ContainerStyle SelectedFrame = Frame with 
        {
            Color = Color.FromRgb(0xfd970e)
        };

        public static readonly ContainerStyle EmptyFrame = Frame with
        {
            Color = Color.Transparent
        };

        public static readonly ContainerStyle FourthFrame = EmptyFrame with 
        {
            Color = Color.FromRgb(0x2b2b2b)
        };

        public static readonly ContainerStyle FrameSeparator = new()
        {
            Width = 1,
            Color = Color.FromRgb(0x262626)
        };

        public static readonly ContainerStyle LayerSeparator = new()
        {
            Height = FrameSpacerWidth,
            Color = Color.FromRgb(0x262626)
        };
    }

    // :spriteeditor
    public static class SpriteEditor
    {
        public static readonly Color UndefinedColor = new(0f, 0f, 0f, 0.1f);
        public const float ButtonSize = 40f;
        public const float ButtonMarginY = 6f;
        public const float ColorPickerBorderWidth = 2.5f;
        public const float ColorSize = 26f;
        public const float ColorPickerWidth = ColorSize * 64 + ColorPickerBorderWidth * 2;
        public const float ColorPickerHeight = ColorSize + ColorPickerBorderWidth * 2;
        public const float ColorPickerSelectionBorderWidth = 3f;

        public static readonly ContainerStyle Root = Overlay.Root with
        {
            AlignX = Align.Center,
            AlignY = Align.Max,
            Width = Size.Fit,
            Height = Size.Fit,
            Margin = EdgeInsets.Bottom(Workspace.Padding)
        };

        public static readonly ContainerStyle ColorPicker = new()
        {
            Padding = EdgeInsets.All(4f),
            Color = Overlay.ContentColor,
            Border = new BorderStyle { Radius = Overlay.ContentBorderRadius }
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

        public readonly static ContainerStyle OpacityPopupRoot = new()
        {
            Padding = Popup.Root.Padding,
            Color = Popup.Root.Color,
            Border = Popup.Root.Border
        };
    }

    // :atlaseditor
    public static class AtlasEditor
    {
        public static readonly ContainerStyle Root = Overlay.Root with
        {
            AlignX = Align.Center,
            AlignY = Align.Max,
            Width = Size.Fit,
            Height = Size.Fit,
            Margin = EdgeInsets.Bottom(20f)
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
            Height = 60.0f
        };

        public static readonly ContainerStyle TextContainer = new()
        {
            Width = Size.Fit,
            Height = Control.Height + 4f,
            AlignX = Align.Center,
            AlignY = Align.Center,
            Border = new BorderStyle { Radius = Control.BorderRadius, Width = 1f, Color = Popup.BorderColor },
            Padding = EdgeInsets.Symmetric(2f, 8f),
            Color = Popup.FillColor,
        };

        public static readonly TextBoxStyle Text = new()
        {
            FontSize = Control.TextSize,
            TextColor = Control.TextColor,
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

