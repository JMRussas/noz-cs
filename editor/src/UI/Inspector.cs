//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal static partial class Inspector
{
    public struct AutoSection : IDisposable
    {
        readonly void IDisposable.Dispose() => EndSection();
    }

    private struct SectionState
    {
        public byte Collapsed;
    }

    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId Section { get; }
    }

    private static WidgetId _nextSectionId;
    private static bool _sectionCollapsed;
    private static bool _sectionActive;
    private static bool _wasHeaderPressed;

    public static bool IsSectionCollapsed => _sectionCollapsed;
    public static bool WasHeaderPressed => _wasHeaderPressed;

    public static void UpdateUI()
    {
        if (!(Workspace.ActiveEditor?.ShowInspector ?? false))
            return;

        _nextSectionId = ElementId.Section;

        using (UI.BeginColumn(ElementId.Root, EditorStyle.Inspector.Root))
            Workspace.ActiveEditor.InspectorUI();
    }

    public static AutoSection BeginSection(
        string name,
        Sprite? icon = null,
        Action? content = null,
        bool isActive = false,
        bool collapsed = false)
    {
        var sectionId = _nextSectionId++;
        _sectionActive = isActive;

        // Outer section wrapper
        ElementTree.BeginColumn();
        var borderColor = isActive ? EditorStyle.Palette.Primary : Color.Transparent;
        ElementTree.BeginBorder(1, borderColor);
        ElementTree.BeginPadding(EdgeInsets.All(1));
        ElementTree.BeginColumn();

        // Header (self-contained tree)
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<SectionState>(sectionId);
        var flags = ElementTree.GetWidgetFlags();
        var hovered = flags.HasFlag(WidgetFlags.Hovered);

        _wasHeaderPressed = flags.HasFlag(WidgetFlags.Pressed);

        if (_wasHeaderPressed)
            state.Collapsed = (byte)(state.Collapsed != 0 ? 0 : 1);

        var iconColor = isActive ? EditorStyle.Palette.Content : EditorStyle.Palette.HeaderText;
        var headerBg = hovered ? EditorStyle.Palette.Secondary : EditorStyle.Palette.Header;
        var chevron = (state.Collapsed != 0 || collapsed)
            ? EditorAssets.Sprites.IconFoldoutClosed
            : EditorAssets.Sprites.IconFoldoutOpen;

        ElementTree.BeginSize(Size.Default, EditorStyle.Inspector.SectionHeaderHeight);
        ElementTree.BeginFill(headerBg);
        ElementTree.BeginPadding(EdgeInsets.LeftRight(8));
        ElementTree.BeginRow(EditorStyle.Inspector.HeaderGap);

        ElementTree.Image(chevron, EditorStyle.Inspector.IconSize, ImageStretch.Uniform, iconColor, 1.0f, new Align2(Align.Center, Align.Center));

        if (icon != null)
            ElementTree.Image(icon, EditorStyle.Inspector.IconSize, ImageStretch.Uniform, iconColor, 1.0f, new Align2(Align.Center, Align.Center));

        ElementTree.Text(name, UI.DefaultFont, EditorStyle.Inspector.HeaderFontSize, iconColor, new Align2(Align.Min, Align.Center));

        ElementTree.Flex();

        content?.Invoke();

        ElementTree.EndTree();

        _sectionCollapsed = state.Collapsed != 0 || collapsed;

        // Body (only if not collapsed)
        if (!_sectionCollapsed)
        {
            ElementTree.BeginFill(EditorStyle.Palette.Panel);
            ElementTree.BeginPadding(EdgeInsets.Symmetric(EditorStyle.Inspector.BodyPaddingV, EditorStyle.Inspector.BodyPaddingH));
            ElementTree.BeginColumn(EditorStyle.Inspector.BodyGap);
        }

        return new AutoSection();
    }

    public static UI.AutoRow BeginRow()
    {
        return UI.BeginRow(EditorStyle.Inspector.Row);
    }

    public static void EndSection()
    {
        if (!_sectionCollapsed)
        {
            ElementTree.EndColumn();
            ElementTree.EndPadding();
            ElementTree.EndFill();
        }

        _sectionCollapsed = false;

        ElementTree.EndColumn();
        ElementTree.EndPadding();
        ElementTree.EndBorder();

        ElementTree.EndColumn();
        _sectionActive = false;
    }
}
