//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public static partial class UI
{
    private static void BeginContainerImpl(int id, in ContainerStyle style, int axis)
    {
        ElementTree.BeginTree();

        var flags = ElementFlags.None;
        if (id != 0)
        {
            ElementTree.BeginWidget(id);
            flags = ElementTree.CurrentWidgetFlags;
        }

        var resolved = style.Resolve != null ? style.Resolve(style, flags) : style;

        if (!style.Margin.IsZero)
            ElementTree.BeginMargin(style.Margin);

        if (resolved.BorderWidth > 0)
            ElementTree.BeginBorder(resolved.BorderWidth, resolved.BorderColor, style.BorderRadius);

        ElementTree.BeginSize(style.Size);

        if (!resolved.Color.IsTransparent)
            ElementTree.BeginFill(resolved.Color, style.BorderRadius);

        if (style.Clip)
            ElementTree.BeginClip(style.BorderRadius);

        if (!style.Padding.IsZero)
            ElementTree.BeginPadding(style.Padding);

        if (style.Align.X != Align.Min || style.Align.Y != Align.Min)
            ElementTree.BeginAlign(style.Align);
        
        if (axis == 0)
            ElementTree.BeginRow(style.Spacing);
        else if (axis == 1)
            ElementTree.BeginColumn(style.Spacing);
    }

    private static void EndContainerImpl() 
    {
        ElementTree.EndTree();
    }

    #region Container

    public static AutoContainer BeginContainer(int id = default) =>
        BeginContainer(id, ContainerStyle.Default);

    public static AutoContainer BeginContainer(int id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, -1);
        return new AutoContainer();
    }

    public static AutoContainer BeginContainer(in ContainerStyle style) =>
        BeginContainer(0, style);

    public static void EndContainer() => EndContainerImpl();

    public static void Container(int id = 0)
    {
        BeginContainer(id: id);
        EndContainer();
    }

    public static void Container(int id, ContainerStyle style)
    {
        BeginContainer(id, style);
        EndContainer();
    }

    public static void Container(ContainerStyle style) =>
        Container(0, style);

    #endregion

    #region Column

    public static AutoColumn BeginColumn(int id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, 1);
        return new AutoColumn();
    }

    public static AutoColumn BeginColumn(int id) =>
        BeginColumn(id, ContainerStyle.Default);

    public static AutoColumn BeginColumn(in ContainerStyle style) =>
        BeginColumn(0, style);

    public static AutoColumn BeginColumn() =>
        BeginColumn(0, ContainerStyle.Default);

    public static void EndColumn() => EndContainerImpl();

    #endregion

    #region Row

    public static AutoRow BeginRow(int id, in ContainerStyle style)
    {
        BeginContainerImpl(id, style, 0);
        return new AutoRow();
    }

    public static AutoRow BeginRow(int id) =>
        BeginRow(id, ContainerStyle.Default);

    public static AutoRow BeginRow(in ContainerStyle style) =>
        BeginRow(0, style);

    public static AutoRow BeginRow() =>
        BeginRow(0, ContainerStyle.Default);

    public static void EndRow() => EndContainerImpl();

    #endregion
}
