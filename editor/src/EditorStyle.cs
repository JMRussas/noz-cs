//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class Style
{
    // General
    public Color BackgroundColor;
    public Color SelectionColor;
    public Color SelectionTextColor;

    // Workspace
    public Color WorkspaceColor;
    public Color WorkspaceGridColor;
    public Color OverlayBackgroundColor;
    public Color OverlayTextColor;
    public Color OverlayAccentTextColor;
    public Color OverlayDisabledTextColor;
    public Color OverlayIconColor;
    public Color OverlayContentColor;

    // Button
    public Color ButtonColor;
    public Color ButtonHoverColor;
    public Color ButtonTextColor;
    public Color ButtonCheckedColor;
    public Color ButtonCheckedTextColor;
    public Color ButtonDisabledColor;
    public Color ButtonDisabledTextColor;

    // Context Menu
    public Color ContextMenuTitleColor;
    public Color ContextMenuSeparatorColor;
    
    // Shape
    public Color ShapeAnchorColor;
    public Color ShapeAnchorOutlineColor;
    public Color ShapeSelectionColor;
    public Color ShapeHoverColor;
    public Color ShapeSegmentColor;
    
    // Box Select
    public Color BoxSelectLineColor;
    public Color BoxSelectFillColor;

    // Control
    public Color ControlTextColor;
    public Color ControlBackgroundColor;
    public Color ControlIconColor;

    // List
    public Color ListItemSelectedFillColor;
    public Color ListItemSelectedTextColor;
    public Color ListItemTextColor;
    public Color ListHeaderTextColor;
}

public static class EditorStyle
{
    #region Canvas
    public static class CanvasId
    {
        public const byte CommandPalette = 1;
    }
    #endregion
    
    private static Style _current = null!;

    public static Style Current => _current;

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

    public const float UIBorderWidth = 2f;

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

    // Skeleton
    public const float SkeletonBoneWidth = 0.02f;
    public const float SkeletonBoneRadius = 0.06f;
    public static readonly Color SkeletonBoneColor = Color.Black;
    public const float SkeletonParentDash = 0.1f;

    // Button
    public const float ButtonPadding = 8f;
    public const float ButtonHeight = 32f;
    public const float ButtonBorderRadius = 8f;

    // Workspace

    public static class Control
    {
        public static Color TextColor => _current.ControlTextColor;
        public static Color IconColor => _current.ControlIconColor;
        public static Color FillColor => _current.ControlBackgroundColor;
    }

    public static class List
    {
        public const float ItemHeight = 28f;
        public const float ItemPadding = 8f;
        public const float ItemTextSize = 14.0f;
        public static Color ItemSelectedFillColor => _current.ListItemSelectedFillColor;
        public static Color ItemSelectedTextColor => _current.ListItemSelectedTextColor;
        public static Color ItemTextColor => _current.ListItemTextColor;
        public static Color HeaderTextColor => _current.ListHeaderTextColor;
    }

    public static class Workspace
    {
        public static Color Color => _current.WorkspaceColor;
        public const float Padding = 16f;
        public const float BoundsLineWidth = 0.03f;
        public const float NameSize = 0.24f;
        public const float NamePadding = 0.04f;
        public static Color GridColor => _current.WorkspaceGridColor;
        public const float GridAlpha = 0.05f;
        public const float GridZeroAlpha = 0.4f;
    }

    public static class BoxSelect 
    {
        public const float LineWidth = 0.02f;
        public static Color LineColor => _current.BoxSelectLineColor;
        public static Color FillColor => _current.BoxSelectFillColor;
    }

    public static class CommandPalette
    {
        private const float IconSize = 24.0f;

        public static readonly ContainerStyle RootContainer = new()
        {
            Width = 600.0f,
            Height = 200.0f,
            Align = Align.Center,
            Padding = EdgeInsets.All(8.0f),
            Color = EditorStyle.Overlay.FillColor,
            Border = new BorderStyle { Radius = 10.0f }
        };

        public static readonly ContainerStyle SearchContainer = new()
        {
            Margin = EdgeInsets.Left(List.ItemHeight)
        };

        public static readonly TextBoxStyle SearchTextBox = new()
        { 
            Height = 24.0f,
            FontSize = 18.0f,
            BackgroundColor = Overlay.FillColor,
            TextColor = List.ItemSelectedTextColor,
            PlaceholderColor = List.HeaderTextColor,
        };

        public static readonly ContainerStyle ListColumn = new()
        {
            Spacing = 4.0f
        };

        public static readonly ContainerStyle CommandContainer = new()
        {
            Height = List.ItemHeight,
            Color = Color.Transparent,
            Border = new BorderStyle { Radius = 8 }
        };

        public static readonly ContainerStyle CommandIconContainer = new()
        {
            Width = List.ItemHeight,
            Height = List.ItemHeight
        };

        public static readonly ContainerStyle SelectedCommandContainer = 
            CommandContainer.WithColor(List.ItemSelectedFillColor);

        public static readonly LabelStyle CommandText = new()
        {
            FontSize = List.ItemTextSize,
            Color = EditorStyle.List.ItemTextColor,
            Align = Align.CenterLeft
        };

        public static readonly LabelStyle SelectedCommandText =
            CommandText.WithColor(List.ItemSelectedTextColor);
    }

    public static class Shape
    {
        public static Color AnchorColor => _current.ShapeAnchorColor;
        public static Color AnchorOutlineColor => _current.ShapeAnchorOutlineColor;
        public static Color SegmentColor => _current.ShapeSegmentColor;
        public const float AnchorSize = 0.18f;
        public const float AnchorSelectedSize = AnchorSize * 1.3f; 
        public const float SegmentWidth = 0.02f;
        public const float SegmentHoverWidth = SegmentWidth * 2.0f;
    }

    // Overlay
    public static class Overlay
    {
        public const float Padding = 12f;
        public const float BorderRadius = 16f;
        public const int TextSize = 14;
        public const float ContentBorderRadius = 9f;
        public static Color FillColor => _current.OverlayBackgroundColor;
        public static Color TextColor => _current.OverlayTextColor;
        public static Color AccentTextColor => _current.OverlayAccentTextColor;
        public static Color DisabledTextColor => _current.OverlayDisabledTextColor;
        public static Color IconColor => _current.OverlayIconColor;
        public static Color ContentColor => _current.OverlayContentColor;
    }

    public static class Shortcut
    {
        public const float Size = 24.0f;
        public const float TextSize = 10;

        public static readonly ContainerStyle ListContainer = new()
        {
            Spacing = 4.0f,
            Align = Align.CenterLeft
        };

        public static readonly ContainerStyle RootContainer = new()
        {
            MinWidth = 24.0f,
            Height = 24.0f,
            Align = Align.Center,
            Padding = EdgeInsets.LeftRight(8),
            Color = Control.TextColor,
            Border = new BorderStyle { Radius = 8.0f }
        };

        public static readonly LabelStyle TextStyle = new()
        {
            FontSize = 10.0f,
            Color = Control.FillColor,
            Align = Align.Center
        };
    }

    // Toggle Button
    public const float ToggleButtonHeight = ButtonHeight;
    public const float ToggleButtonPadding = 6f;
    public const float ToggleButtonBorderRadius = 8f;

    public static class ContextMenu
    {
        public const int MinWidth = 100;
        public const int TextSize = 12;
        public const float SeparatorHeight = 2f;
        public const float SeparatorSpacing = 12f;
        public const float ItemHeight = 20f;
        public static Color TitleColor => _current.ContextMenuTitleColor;
        public static Color SeparatorColor => _current.ContextMenuSeparatorColor;
    }

    // Color Picker
    public const float ColorPickerBorderWidth = 2.5f;
    public const float ColorPickerColorSize = 26f;
    public const float ColorPickerWidth = ColorPickerColorSize * 64 + ColorPickerBorderWidth * 2;
    public const float ColorPickerHeight = ColorPickerColorSize + ColorPickerBorderWidth * 2;
    public const float ColorPickerSelectionBorderWidth = 3f;

    // Style accessors
    public static Color SelectionColor => _current.SelectionColor;
    public static Color SelectionTextColor => _current.SelectionTextColor;
    public static Color ButtonColor => _current.ButtonColor;
    public static Color ButtonHoverColor => _current.ButtonHoverColor;
    public static Color ButtonTextColor => _current.ButtonTextColor;
    public static Color ButtonCheckedColor => _current.ButtonCheckedColor;
    public static Color ButtonCheckedTextColor => _current.ButtonCheckedTextColor;
    public static Color ButtonDisabledColor => _current.ButtonDisabledColor;
    public static Color ButtonDisabledTextColor => _current.ButtonDisabledTextColor;

    public static void Init()
    {
        _current = CreateDarkStyle();
    }

    public static void Shutdown()
    {
    }

    private static Style CreateDarkStyle()
    {
        var selectionColor = Color.FromRgb(0x0099ff);

        return new Style
        {
            BackgroundColor = Color.FromRgb(0x383838),
            SelectionColor = selectionColor,
            SelectionTextColor = Color.FromRgb(0xf0f0f0),
            ButtonColor = Color.FromRgb(0x585858),
            ButtonHoverColor = Color.FromRgb(0x676767),
            ButtonTextColor = Color.FromRgb(0xe3e3e3),
            ButtonCheckedColor = Color.FromRgb(0x557496),
            ButtonCheckedTextColor = Color.FromRgb(0xf0f0f0),
            ButtonDisabledColor = Color.FromRgb(0x2a2a2a),
            ButtonDisabledTextColor = Color.FromRgb(0x636363),
            
            WorkspaceColor = Color.FromRgb(0x1d1d1d),
            WorkspaceGridColor = new Color(128, 127, 128),

            OverlayBackgroundColor = Color.FromRgb(0x111111),
            OverlayTextColor = Color.FromRgb(0x979797),
            OverlayAccentTextColor = Color.FromRgb(0xd2d2d2),
            OverlayDisabledTextColor = Color.FromRgb(0x4a4a4a),
            OverlayIconColor = Color.FromRgb(0x585858),
            OverlayContentColor = Color.FromRgb(0x2a2a2a),
            ContextMenuSeparatorColor = Color.FromRgb(0x2a2a2a),
            ContextMenuTitleColor = Color.FromRgb(0x636363),

            ShapeAnchorColor = Color.White,
            ShapeAnchorOutlineColor = Color.FromRgb(0x0099ff),
            ShapeSelectionColor = selectionColor,
            ShapeHoverColor = selectionColor,
            ShapeSegmentColor = Color.FromRgb(0x111111).WithAlpha(0.5f),

            BoxSelectLineColor = selectionColor,
            BoxSelectFillColor = selectionColor.WithAlpha(0.15f),

            ControlTextColor = Color.FromRgb(0xeeeeee),
            ControlIconColor = Color.FromRgb(0x999999),
            ControlBackgroundColor = Color.FromRgb(0x2b2b2b),

            ListItemSelectedFillColor = Color.FromRgb(0x2b2b2b),
            ListItemSelectedTextColor = Color.FromRgb(0xf4f4f4),
            ListItemTextColor = Color.FromRgb(0x999999),
            ListHeaderTextColor = Color.FromRgb(0x666666)
        };
    }
}

