#if DEBUG

using System.Numerics;
using System.Reflection;
using System.Text;

namespace NoZ;

public static unsafe partial class UI
{
    private static Dictionary<int, string>? _debugIdNames;

    public static void DebugInit()
    {
        _debugIdNames = new Dictionary<int, string>();

        foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
        {
            try
            {
                foreach (var type in assembly.GetTypes())
                {
                    var nested = type.GetNestedType("ElementId",
                        BindingFlags.NonPublic | BindingFlags.Public);
                    if (nested == null) continue;

                    foreach (var field in nested.GetFields(BindingFlags.Public | BindingFlags.Static))
                    {
                        if (field.IsLiteral && field.FieldType == typeof(int))
                        {
                            var id = (int)field.GetRawConstantValue()!;
                            _debugIdNames[id] = $"{type.Name}.{field.Name}";
                        }
                    }
                }
            }
            catch (ReflectionTypeLoadException) { }
        }
    }

    public static string DebugGetElementName(int elementId)
    {
        if (_debugIdNames != null && _debugIdNames.TryGetValue(elementId, out var name))
            return name;
        return "";
    }

    public static int? DebugFindElementByName(string name)
    {
        if (_debugIdNames == null) return null;
        foreach (var kvp in _debugIdNames)
        {
            if (kvp.Value.Equals(name, StringComparison.OrdinalIgnoreCase))
                return kvp.Key;
        }
        return null;
    }

    public static string DebugDumpTree()
    {
        if (_debugIdNames == null) DebugInit();

        var sb = new StringBuilder();

        sb.AppendLine($"Frame: {_frame}  Screen: {(int)ScreenSize.X}x{(int)ScreenSize.Y}");
        sb.AppendLine("───────────────────────────────");

        if (_elementCount == 0)
        {
            sb.AppendLine("(no elements)");
            return sb.ToString();
        }

        for (int i = 0; i < _elementCount; i++)
        {
            ref var e = ref _elements[i];
            var depth = GetDepth(in e);
            var indent = new string(' ', depth * 2);

            sb.Append($"{indent}[{i}] {e.Type}");

            if (e.Id > 0)
            {
                var name = DebugGetElementName(e.Id);
                if (name.Length > 0)
                    sb.Append($" \"{name}\"");
            }

            var rect = e.Rect;
            if (rect.Width > 0 || rect.Height > 0)
                sb.Append($" {(int)rect.X},{(int)rect.Y} {(int)rect.Width}x{(int)rect.Height}");

            DebugAppendElementData(sb, ref e);

            if (e.Id > 0)
                DebugAppendStateFlags(sb, e.Id);

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static string DebugDumpSubtree(int elementId)
    {
        var sb = new StringBuilder();

        int rootIndex = -1;
        for (int i = 0; i < _elementCount; i++)
        {
            ref var e = ref _elements[i];
            if (e.Id == elementId)
            {
                rootIndex = i;
                break;
            }
        }

        if (rootIndex < 0)
        {
            sb.AppendLine($"Element {elementId} not found");
            return sb.ToString();
        }

        ref var root = ref _elements[rootIndex];
        var rootDepth = GetDepth(in root);

        for (int i = rootIndex; i < _elementCount; i++)
        {
            ref var e = ref _elements[i];
            var depth = GetDepth(in e);

            if (i > rootIndex && depth <= rootDepth)
                break;

            var relativeDepth = depth - rootDepth;
            var indent = new string(' ', relativeDepth * 2);

            sb.Append($"{indent}[{i}] {e.Type}");

            if (e.Id > 0)
            {
                var name = DebugGetElementName(e.Id);
                if (name.Length > 0)
                    sb.Append($" \"{name}\"");
            }

            var rect = e.Rect;
            if (rect.Width > 0 || rect.Height > 0)
                sb.Append($" {(int)rect.X},{(int)rect.Y} {(int)rect.Width}x{(int)rect.Height}");

            DebugAppendElementData(sb, ref e);

            if (e.Id > 0)
                DebugAppendStateFlags(sb, e.Id);

            sb.AppendLine();
        }

        return sb.ToString();
    }

    public static List<(int Index, int Id, string Name, string Type)> DebugFindElements(
        string? typeName = null, string? textContent = null)
    {
        var results = new List<(int, int, string, string)>();

        for (int i = 0; i < _elementCount; i++)
        {
            ref var e = ref _elements[i];

            if (typeName != null && !e.Type.ToString().Equals(typeName, StringComparison.OrdinalIgnoreCase))
                continue;

            if (textContent != null)
            {
                var text = DebugGetElementText(ref e);
                if (text == null || !text.Contains(textContent, StringComparison.OrdinalIgnoreCase))
                    continue;
            }

            var name = e.Id > 0 ? DebugGetElementName(e.Id) : "";
            results.Add((i, e.Id, name, e.Type.ToString()));
        }

        return results;
    }

    public static string? DebugGetTextByElementId(int elementId)
    {
        for (int i = 0; i < _elementCount; i++)
        {
            ref var e = ref _elements[i];
            if (e.Id == elementId)
                return DebugGetElementText(ref e);
        }
        return null;
    }

    public static bool DebugElementExistsInTree(int elementId)
    {
        for (int i = 0; i < _elementCount; i++)
        {
            if (_elements[i].Id == elementId)
                return true;
        }
        return false;
    }

    private static string? DebugGetElementText(ref Element e)
    {
        switch (e.Type)
        {
            case ElementType.Label:
                if (e.Data.Label.Text.Length > 0)
                    return e.Data.Label.Text.AsReadOnlySpan().ToString();
                break;

            case ElementType.Widget:
                if (e.Id > 0)
                {
                    var text = GetElementText(e.Id);
                    if (text.Length > 0)
                        return text.ToString();
                }
                break;
        }
        return null;
    }

    private static void DebugAppendElementData(StringBuilder sb, ref Element e)
    {
        switch (e.Type)
        {
            case ElementType.Container:
            case ElementType.Column:
            case ElementType.Row:
                ref var c = ref e.Data.Container;
                if (!c.Color.IsTransparent)
                    sb.Append($" bg:{DebugColorToHex(c.Color)}");
                if (!c.BorderRadius.IsZero)
                    sb.Append($" radius={c.BorderRadius.TopLeft:0}");
                if (!c.Padding.IsZero)
                {
                    if (c.Padding.T == c.Padding.B && c.Padding.L == c.Padding.R && c.Padding.T == c.Padding.L)
                        sb.Append($" pad={c.Padding.T:0}");
                    else
                        sb.Append($" pad={c.Padding.T:0},{c.Padding.R:0},{c.Padding.B:0},{c.Padding.L:0}");
                }
                if (c.Spacing > 0)
                    sb.Append($" spacing={c.Spacing:0}");
                if (c.Align.X == Align.Center && c.Align.Y == Align.Center)
                    sb.Append(" align=center");
                else if (c.Align.X != Align.Min || c.Align.Y != Align.Min)
                    sb.Append($" align={c.Align.X},{c.Align.Y}");
                if (!c.Margin.IsZero)
                    sb.Append($" margin={c.Margin.T:0},{c.Margin.R:0},{c.Margin.B:0},{c.Margin.L:0}");
                break;

            case ElementType.Label:
                ref var l = ref e.Data.Label;
                if (l.Text.Length > 0)
                {
                    var text = l.Text.AsReadOnlySpan().ToString();
                    if (text.Length > 50)
                        text = text[..47] + "...";
                    sb.Append($" \"{text}\"");
                }
                sb.Append($" size={l.FontSize:0}");
                if (l.Align.X == Align.Center)
                    sb.Append(" align=center");
                if (l.Color != Color.White)
                    sb.Append($" color={DebugColorToHex(l.Color)}");
                break;

            case ElementType.Widget:
                if (e.Id > 0)
                {
                    var text = GetElementText(e.Id);
                    if (text.Length > 0)
                    {
                        var str = text.Length > 50 ? text[..47].ToString() + "..." : text.ToString();
                        sb.Append($" text=\"{str}\"");
                    }
                }
                break;

            case ElementType.Scrollable:
                ref var s = ref e.Data.Scrollable;
                sb.Append($" scroll={s.Offset:0}/{s.ContentHeight:0}");
                break;

            case ElementType.Spacer:
                ref var sp = ref e.Data.Spacer;
                sb.Append($" {sp.Size.X:0}x{sp.Size.Y:0}");
                break;

            case ElementType.Image:
                if (e.Asset != null)
                    sb.Append($" asset={e.Asset}");
                break;

            case ElementType.Flex:
                ref var f = ref e.Data.Flex;
                if (f.Flex != 1f)
                    sb.Append($" flex={f.Flex}");
                break;

            case ElementType.Opacity:
                sb.Append($" opacity={e.Data.Opacity.Value:0.##}");
                break;

            case ElementType.Grid:
                ref var g = ref e.Data.Grid;
                sb.Append($" cols={g.Columns} cell={g.CellWidth:0}x{g.CellHeight:0}");
                break;

            case ElementType.Popup:
                ref var p = ref e.Data.Popup;
                if (p.AutoClose)
                    sb.Append(" autoclose");
                if (p.Interactive)
                    sb.Append(" interactive");
                break;
        }
    }

    private static void DebugAppendStateFlags(StringBuilder sb, int elementId)
    {
        ref var es = ref _elementStates[elementId];
        if (es.IsHovered) sb.Append(" [hovered]");
        if (es.IsPressed) sb.Append(" [pressed]");
        if (es.HasFocus) sb.Append(" [focused]");
        if (es.IsDown) sb.Append(" [down]");
        if (es.IsDragging) sb.Append(" [dragging]");
    }

    public static Vector2? DebugGetElementScreenCenter(int elementId)
    {
        if (Camera == null) return null;

        ref var es = ref _elementStates[elementId];
        if (es.Rect.Width <= 0 && es.Rect.Height <= 0)
            return null;

        var worldCenter = Vector2.Transform(
            es.Rect.Position + es.Rect.Size * 0.5f,
            es.LocalToWorld);

        return Camera.WorldToScreen(worldCenter);
    }

    private static string DebugColorToHex(Color c)
    {
        var r = (int)(c.R * 255);
        var g = (int)(c.G * 255);
        var b = (int)(c.B * 255);
        return $"#{r:X2}{g:X2}{b:X2}";
    }
}

#endif
