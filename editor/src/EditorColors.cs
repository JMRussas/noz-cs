//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class EditorColors
{
    // General
    public Color BackgroundColor;
    public Color SelectionColor;
    public Color SelectionTextColor;

    // Workspace
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


    // Box Select
    public Color BoxSelectLineColor;
    public Color BoxSelectFillColor;

    // Control
    public Color ControlTextColor;
    public Color ControlFillColor;
    public Color ControlSelectedFillColor;
    public Color ControlPlaceholderTextColor;
    public Color ControlIconColor;

    // List
    public Color ListItemSelectedFillColor;
    public Color ListItemSelectedTextColor;
    public Color ListItemTextColor;
    public Color ListHeaderTextColor;

    public struct WorkspaceColors
    {
        public Color Fill;
        public Color Grid;
    }

    public struct PopupColors
    {
        public Color Fill;
        public Color Text;
        public Color Spacer;
    }

    public struct SpriteEditorColors
    {
    }

    public struct ShapeColors
    {
        public Color Anchor;
        public Color SelectedAnchor;
        public Color Segment;
        public Color SelectedSegment;
    }

    public WorkspaceColors Workspace;
    public SpriteEditorColors SpriteEditor;
    public ShapeColors Shape;
    public PopupColors Popup;

    private static readonly Color selectionColor = Color.FromRgb(0x0099ff);
    public static EditorColors Dark => new()
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


        OverlayBackgroundColor = Color.FromRgb(0x111111),
        OverlayTextColor = Color.FromRgb(0x979797),
        OverlayAccentTextColor = Color.FromRgb(0xd2d2d2),
        OverlayDisabledTextColor = Color.FromRgb(0x4a4a4a),
        OverlayIconColor = Color.FromRgb(0x585858),
        OverlayContentColor = Color.FromRgb(0x2a2a2a),
        ContextMenuSeparatorColor = Color.FromRgb(0x2a2a2a),
        ContextMenuTitleColor = Color.FromRgb(0x636363),


        BoxSelectLineColor = selectionColor,
        BoxSelectFillColor = selectionColor.WithAlpha(0.15f),

        ControlTextColor = Color.FromRgb(0xeeeeee),
        ControlIconColor = Color.FromRgb(0x999999),
        ControlFillColor = Color.FromRgb(0x2b2b2b),
        ControlSelectedFillColor = Color.FromRgb(0x555555),
        ControlPlaceholderTextColor = Color.FromRgb(0x666666),

        ListItemSelectedFillColor = Color.FromRgb(0x2b2b2b),
        ListItemSelectedTextColor = Color.FromRgb(0xf4f4f4),
        ListItemTextColor = Color.FromRgb(0x999999),
        ListHeaderTextColor = Color.FromRgb(0x666666),

        Workspace = new()
        {
            Fill = Color.FromRgb(0x3f3f3f),
            Grid = Color.FromRgb(0x4e4e4e),
        },

        Popup = new()
        {
            Fill = Color.FromRgb(0x2b2b2b),
            Text = Color.FromRgb(0xFFFFFF),
            Spacer = Color.FromRgb(0x363636),
        },

        Shape = new ()
        {
            Anchor = Color.Black,
            SelectedAnchor = Color.FromRgb(0xff7900),
            Segment = Color.FromRgb(0x1d1d1d),
            SelectedSegment = Color.FromRgb(0xfd970e)
        },

        SpriteEditor = new()
        {
        }
    };
}
