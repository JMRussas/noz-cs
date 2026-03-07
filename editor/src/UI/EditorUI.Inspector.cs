//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal static partial class EditorUI
{
    private static float _fieldFloat;
    private static float _fieldFloat2;
    private static int _fieldInt;
    private static int _fieldInt2;

    public static void SectionHeader(string text)
    {
        UI.Spacer(EditorStyle.Inspector.SectionGap / 2);
        using (UI.BeginRow(EditorStyle.Inspector.SectionHeader))
            UI.Label(text, EditorStyle.Inspector.SectionText);
        UI.Container(EditorStyle.Inspector.Separator);
    }

    public static bool FloatField(int id, ref float value, string? placeholder = null)
    {
        var changed = false;
        Span<char> buf = stackalloc char[32];
        value.TryFormat(buf, out var written, "G");

        using (UI.BeginContainer(EditorStyle.Inspector.FieldContainer))
        {
            if (UI.TextBox(id, buf[..written], EditorStyle.Inspector.TextBox, placeholder ?? "0"))
            {
                if (float.TryParse(UI.GetElementText(id), out var parsed))
                {
                    value = parsed;
                    changed = true;
                }
            }
        }
        return changed;
    }

    public static bool IntField(int id, ref int value, string? placeholder = null)
    {
        var changed = false;
        Span<char> buf = stackalloc char[16];
        value.TryFormat(buf, out var written);

        using (UI.BeginContainer(EditorStyle.Inspector.FieldContainer))
        {
            if (UI.TextBox(id, buf[..written], EditorStyle.Inspector.TextBox, placeholder ?? "0"))
            {
                if (int.TryParse(UI.GetElementText(id), out var parsed))
                {
                    value = parsed;
                    changed = true;
                }
            }
        }
        return changed;
    }

    private static void InspectorLabel(string label)
    {
        using (UI.BeginContainer(new ContainerStyle
        {
            Width = EditorStyle.Inspector.LabelWidth,
            //Height = Size.Fit,
            AlignY = Align.Center
        }))
            UI.Label(label, EditorStyle.Inspector.Label);
    }

    public static bool RangeField(int id, string label, ref VfxRange value)
    {
        _fieldFloat = value.Min;
        _fieldFloat2 = value.Max;
        var changed = false;

        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel(label);
            if (FloatField(id, ref _fieldFloat)) changed = true;
            if (FloatField(id + 1, ref _fieldFloat2)) changed = true;
        }

        if (changed)
            value = new VfxRange(_fieldFloat, _fieldFloat2);

        return changed;
    }

    public static bool IntRangeField(int id, string label, ref VfxIntRange value)
    {
        _fieldInt = value.Min;
        _fieldInt2 = value.Max;
        var changed = false;

        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel(label);
            if (IntField(id, ref _fieldInt)) changed = true;
            if (IntField(id + 1, ref _fieldInt2)) changed = true;
        }

        if (changed)
            value = new VfxIntRange(_fieldInt, _fieldInt2);

        return changed;
    }

    public static bool Vec2RangeField(int id, string label, ref VfxVec2Range value)
    {
        var changed = false;
        var minX = value.Min.X;
        var minY = value.Min.Y;
        var maxX = value.Max.X;
        var maxY = value.Max.Y;

        // Row 1: label + min X, min Y
        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel(label);
            if (FloatField(id, ref minX)) changed = true;
            if (FloatField(id + 1, ref minY)) changed = true;
        }

        // Row 2: empty label + max X, max Y
        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel("");
            if (FloatField(id + 2, ref maxX)) changed = true;
            if (FloatField(id + 3, ref maxY)) changed = true;
        }

        if (changed)
            value = new VfxVec2Range(new Vector2(minX, minY), new Vector2(maxX, maxY));

        return changed;
    }

    public static bool FloatCurveField(int id, string label, ref VfxFloatCurve curve)
    {
        var changed = false;

        // Start range
        var startRange = curve.Start;
        if (RangeField(id, label, ref startRange))
        {
            curve.Start = startRange;
            changed = true;
        }

        // End range + curve type
        var endRange = curve.End;
        var curveType = curve.Type;

        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel("");
            if (FloatField(id + 2, ref endRange.Min)) changed = true;
            if (FloatField(id + 3, ref endRange.Max)) changed = true;
            if (CurveTypeButton(id + 4, ref curveType)) changed = true;
        }

        if (changed)
        {
            curve.End = endRange;
            curve.Type = curveType;
        }

        return changed;
    }

    public static bool ColorCurveField(int id, string label, ref VfxColorCurve curve)
    {
        var changed = false;

        // Start color range
        var startMin = curve.Start.Min;
        var startMax = curve.Start.Max;

        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel(label);
            if (ColorField(id, ref startMin)) changed = true;
            if (ColorField(id + 1, ref startMax)) changed = true;
        }

        // End color range + curve type
        var endMin = curve.End.Min;
        var endMax = curve.End.Max;
        var curveType = curve.Type;

        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel("");
            if (ColorField(id + 2, ref endMin)) changed = true;
            if (ColorField(id + 3, ref endMax)) changed = true;
            if (CurveTypeButton(id + 4, ref curveType)) changed = true;
        }

        if (changed)
        {
            curve.Start = new VfxColorRange(startMin, startMax);
            curve.End = new VfxColorRange(endMin, endMax);
            curve.Type = curveType;
        }

        return changed;
    }

    public static bool ColorField(int id, ref Color color)
    {
        var changed = false;
        var hex = ColorToHex(color);

        using (UI.BeginContainer(EditorStyle.Inspector.FieldContainer))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                // Color swatch
                using (UI.BeginContainer(new ContainerStyle
                {
                    Width = 16, Height = 16,
                    Color = color,
                    BorderRadius = 3, BorderWidth = 1, BorderColor = Color.FromRgb(0x555555),
                    AlignY = Align.Center
                })) { }

                // Text input (in a container so TextBox fills width via Percent mode)
                using (UI.BeginContainer(ContainerStyle.Default))
                {
                    if (UI.TextBox(id, (ReadOnlySpan<char>)hex, EditorStyle.Inspector.TextBox, "#fff"))
                    {
                        var text = UI.GetElementText(id).ToString();
                        if (TryParseHexColor(text, out var parsed))
                        {
                            color = parsed;
                            changed = true;
                        }
                    }
                }
            }
        }

        return changed;
    }

    private static readonly string[] CurveTypeNames =
        ["Linear", "EaseIn", "EaseOut", "EaseInOut", "Quadratic", "Cubic", "Sine"];

    private static VfxCurveType _curveTypeValue;

    public static bool CurveTypeButton(int id, ref VfxCurveType curveType)
    {
        var oldType = curveType;
        _curveTypeValue = curveType;
        var typeName = CurveTypeNames[(int)curveType];

        void Content()
        {
            ControlText(typeName);
        }

        if (Control(id, Content, selected: IsPopupOpen(id), padding: true))
            TogglePopup(id);

        if (IsPopupOpen(id))
        {
            static void PopupContent()
            {
                for (int i = 0; i < CurveTypeNames.Length; i++)
                {
                    var type = (VfxCurveType)i;
                    if (PopupItem(CurveTypeNames[i], selected: _curveTypeValue == type))
                    {
                        _curveTypeValue = type;
                        ClosePopup();
                    }
                }
            }
            Popup(id, PopupContent);
        }

        curveType = _curveTypeValue;
        return curveType != oldType;
    }

    public static bool ToggleField(int id, string label, ref bool value)
    {
        var changed = false;

        using (UI.BeginRow(EditorStyle.Inspector.Row))
        {
            InspectorLabel(label);

            using (UI.BeginContainer(id, new ContainerStyle
            {
                Width = 24, Height = 24,
                AlignY = Align.Center,
                BorderRadius = 4
            }))
            {
                if (UI.IsHovered())
                    UI.Container(EditorStyle.Control.HoverFill with { BorderRadius = 4 });
                else
                    UI.Container(EditorStyle.Control.Fill with { BorderRadius = 4 });

                if (value)
                    UI.Image(EditorAssets.Sprites.IconCheck, EditorStyle.Control.Icon);

                if (UI.WasPressed())
                {
                    value = !value;
                    changed = true;
                }
            }
        }

        return changed;
    }

    private static string ColorToHex(Color c)
    {
        var r = (byte)(Math.Clamp(c.R, 0, 1) * 255);
        var g = (byte)(Math.Clamp(c.G, 0, 1) * 255);
        var b = (byte)(Math.Clamp(c.B, 0, 1) * 255);
        var a = (byte)(Math.Clamp(c.A, 0, 1) * 255);
        return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private static bool TryParseHexColor(string text, out Color color)
    {
        color = Color.White;
        text = text.Trim();
        if (text.StartsWith('#'))
            text = text[1..];

        if (text.Length == 6)
        {
            if (byte.TryParse(text[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b))
            {
                color = new Color(r / 255f, g / 255f, b / 255f, 1f);
                return true;
            }
        }
        else if (text.Length == 8)
        {
            if (byte.TryParse(text[0..2], System.Globalization.NumberStyles.HexNumber, null, out var r) &&
                byte.TryParse(text[2..4], System.Globalization.NumberStyles.HexNumber, null, out var g) &&
                byte.TryParse(text[4..6], System.Globalization.NumberStyles.HexNumber, null, out var b) &&
                byte.TryParse(text[6..8], System.Globalization.NumberStyles.HexNumber, null, out var a))
            {
                color = new Color(r / 255f, g / 255f, b / 255f, a / 255f);
                return true;
            }
        }

        return false;
    }
}
