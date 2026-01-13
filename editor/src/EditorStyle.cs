//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz.editor;

public class Style
{
    // General
    public Color BackgroundColor;
    public Color SelectionColor;

    // Workspace
    public Color WorkspaceColor;
    public Color GridColor;
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
}

public static class EditorStyle
{
    private static Style _current = null!;

    public static Style Current => _current;

    // Legacy colors (deprecated but still used)
    public static readonly Color VertexSelected = Color.FromRgb(0xFF7900);
    public static readonly Color Vertex = Color.Black;
    public static readonly Color Edge = Color.Black;
    public static readonly Color EdgeSelected = Color.FromRgb(0xFD970B);
    public static readonly Color Origin = Color.FromRgb(0xFF9F2C);
    public static readonly Color Selected = Color.White;
    public static readonly Color Center = new(1f, 1f, 1f, 0.5f);
    public static readonly Color BoneSelected = EdgeSelected;

    // Color32 versions for rendering
    public static readonly Color32 SelectionColor32 = new(0xFF, 0x79, 0x00, 0xFF);
    public static readonly Color32 EdgeColor = new(0x00, 0x00, 0x00, 0xFF);

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
    public const float WorkspacePadding = 16f;

    // Overlay
    public const int OverlayTextSize = 14;
    public const float OverlayContentBorderRadius = 9f;
    public const float OverlayPadding = 12f;
    public const float OverlayBorderRadius = 16f;

    // Toggle Button
    public const float ToggleButtonHeight = ButtonHeight;
    public const float ToggleButtonPadding = 6f;
    public const float ToggleButtonBorderRadius = 8f;

    // Context Menu
    public const int ContextMenuMinWidth = 100;
    public const int ContextMenuTextSize = 12;
    public const float ContextMenuSeparatorHeight = 2f;
    public const float ContextMenuSeparatorSpacing = 12f;
    public const float ContextMenuItemHeight = 20f;

    // Color Picker
    public const float ColorPickerBorderWidth = 2.5f;
    public const float ColorPickerColorSize = 26f;
    public const float ColorPickerWidth = ColorPickerColorSize * 64 + ColorPickerBorderWidth * 2;
    public const float ColorPickerHeight = ColorPickerColorSize + ColorPickerBorderWidth * 2;
    public const float ColorPickerSelectionBorderWidth = 3f;
    public static readonly Color ColorPickerSelectionBorderColor = VertexSelected;

    // Style accessors
    public static Color BackgroundColor => _current.BackgroundColor;
    public static Color SelectionColor => _current.SelectionColor;
    public static Color ButtonColor => _current.ButtonColor;
    public static Color ButtonHoverColor => _current.ButtonHoverColor;
    public static Color ButtonTextColor => _current.ButtonTextColor;
    public static Color ButtonCheckedColor => _current.ButtonCheckedColor;
    public static Color ButtonCheckedTextColor => _current.ButtonCheckedTextColor;
    public static Color ButtonDisabledColor => _current.ButtonDisabledColor;
    public static Color ButtonDisabledTextColor => _current.ButtonDisabledTextColor;
    public static Color WorkspaceColor => _current.WorkspaceColor;
    public static Color GridColor => _current.GridColor;
    public static Color OverlayBackgroundColor => _current.OverlayBackgroundColor;
    public static Color OverlayTextColor => _current.OverlayTextColor;
    public static Color OverlayAccentTextColor => _current.OverlayAccentTextColor;
    public static Color OverlayDisabledTextColor => _current.OverlayDisabledTextColor;
    public static Color OverlayIconColor => _current.OverlayIconColor;
    public static Color OverlayContentColor => _current.OverlayContentColor;
    public static Color ContextMenuTitleColor => _current.ContextMenuTitleColor;
    public static Color ContextMenuSeparatorColor => _current.ContextMenuSeparatorColor;

    public static void Init()
    {
        _current = CreateDarkStyle();
    }

    public static void Shutdown()
    {
    }

    private static Style CreateDarkStyle()
    {
        return new Style
        {
            BackgroundColor = Color.FromRgb(0x383838),
            SelectionColor = Color.FromRgb(0x3a79bb),
            ButtonColor = Color.FromRgb(0x585858),
            ButtonHoverColor = Color.FromRgb(0x676767),
            ButtonTextColor = Color.FromRgb(0xe3e3e3),
            ButtonCheckedColor = Color.FromRgb(0x557496),
            ButtonCheckedTextColor = Color.FromRgb(0xf0f0f0),
            ButtonDisabledColor = Color.FromRgb(0x2a2a2a),
            ButtonDisabledTextColor = Color.FromRgb(0x636363),
            WorkspaceColor = Color.FromRgb(0x464646),
            GridColor = Color.FromRgb(0x686868),
            OverlayBackgroundColor = Color.FromRgb(0x0e0e0e),
            OverlayTextColor = Color.FromRgb(0x979797),
            OverlayAccentTextColor = Color.FromRgb(0xd2d2d2),
            OverlayDisabledTextColor = Color.FromRgb(0x4a4a4a),
            OverlayIconColor = Color.FromRgb(0x585858),
            OverlayContentColor = Color.FromRgb(0x2a2a2a),
            ContextMenuSeparatorColor = Color.FromRgb(0x2a2a2a),
            ContextMenuTitleColor = Color.FromRgb(0x636363)
        };
    }
}
