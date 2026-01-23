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

    public struct OverlayColors
    {
        public Color Fill;
        public Color Text;
        public Color AccentText;
        public Color DisabledText;
        public Color Icon;
        public Color Content;
    }

    public WorkspaceColors Workspace;
    public SpriteEditorColors SpriteEditor;
    public ShapeColors Shape;
    public PopupColors Popup;
    public OverlayColors Overlay;

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
            Fill = Color.FromRgb(0x181818),
            Text = Color.FromRgb(0xdbdbdb),
            Spacer = Color.FromRgb(0x2f2f2f),
        },

        Shape = new ()
        {
            Anchor = Color.Black,
            SelectedAnchor = Color.FromRgb(0xff7900),
            Segment = Color.FromRgb(0x1d1d1d),
            SelectedSegment = Color.FromRgb(0xfd970e)
        },

        Overlay = new()
        {
            Fill = Color.FromRgb(0x111111),
            Text = Color.FromRgb(0x979797),
            AccentText = Color.FromRgb(0xd2d2d2),
            DisabledText = Color.FromRgb(0x4a4a4a),
            Icon = Color.FromRgb(0x585858),
            Content = Color.FromRgb(0x2a2a2a),
        },

        SpriteEditor = new()
        {
        }
    };
}
