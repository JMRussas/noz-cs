//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class VfxEditor : DocumentEditor
{
    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId ToolbarRoot { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId LoopButton { get; }
        public static partial WidgetId AddEmitterButton { get; }
        public static partial WidgetId InspectorRoot { get; }
        public static partial WidgetId InspectorScroll { get; }
        public static partial WidgetId EmitterTab { get; }
        public static partial WidgetId RemoveEmitterButton { get; }
        public static partial WidgetId Field { get; }
    }

    private WidgetId _nextFieldId;

    public new VfxDocument Document => (VfxDocument)base.Document;

    public VfxEditor(VfxDocument document) : base(document)
    {
        Commands =
        [
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab },
        ];
    }

    public override void Update()
    {
        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(Document.Transform);
            Document.DrawOrigin();
            Document.DrawBounds(selected: false);
        }

        Graphics.SetTransform(Document.Transform);
        Document.Draw();
    }

    public override void UpdateUI()
    {
        // Toolbar (bottom-center overlay)
        using (UI.BeginColumn(ElementId.ToolbarRoot, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();
        }

        // Inspector panel (right side)
        //InspectorUI();
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.Toolbar.Root);

        UI.Flex();

        UI.SetChecked(Document.IsPlaying);
        if (UI.Button(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, EditorStyle.Button.ToggleIcon))
            TogglePlayback();

        UI.Flex();
    }

#if false
    private void InspectorUI()
    {
        _nextFieldId = ElementId.Field;

        using (UI.BeginColumn(ElementId.InspectorRoot, EditorStyle.Inspector.Root))
        {
            // VFX-level properties
            VfxPropertiesUI();

            // Emitter tabs
            EmitterListUI();

            // Properties for selected emitter (Flex fills remaining column height)
            using (UI.BeginFlex())
            {
                if (Document.EmitterCount > 0 && Document.SelectedEmitterIndex >= 0)
                {
                    using (UI.BeginScrollable(ElementId.InspectorScroll, new ScrollableStyle
                    {
                        Scrollbar = ScrollbarVisibility.Auto,
                        ScrollbarWidth = 6,
                        ScrollbarThumbColor = Color.FromRgb(0x555555),
                        ScrollbarBorderRadius = 3
                    }))
                    {
                        // Height=Fit so content Column grows beyond viewport, enabling scroll
                        using (UI.BeginColumn(EditorStyle.Inspector.Content with { Height = Size.Fit }))
                        {
                            EmitterPropertiesUI();
                            ParticlePropertiesUI();
                        }
                    }
                }
            }
        }
    }
#endif


    private WidgetId NextFieldId(int count = 1)
    {
        var id = _nextFieldId;
        _nextFieldId += count;
        return id;
    }

    private void VfxPropertiesUI()
    {
        using (UI.BeginColumn(EditorStyle.Inspector.Content))
        {
            var duration = Document.Duration;
            if (RangeField(NextFieldId(2), "Duration", ref duration))
            {
                Document.Duration = duration;
                Document.ApplyChanges();
            }

            var loop = Document.Loop;
            UI.SetChecked(loop);
            if (UI.Toggle(NextFieldId(), "Loop", loop, EditorStyle.Inspector.Toggle, EditorAssets.Sprites.IconCheck))
            {
                loop = !loop;
                Document.Loop = loop;
                Document.ApplyChanges();
            }
        }

        UI.Container(EditorStyle.Inspector.Separator);
    }

    private void EmitterListUI()
    {
        using (UI.BeginColumn(EditorStyle.Inspector.Content))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4, Height = Size.Fit }))
            {
                for (var i = 0; i < Document.EmitterCount; i++)
                {
                    var selected = i == Document.SelectedEmitterIndex;
                    var tabId = ElementId.EmitterTab + i;

                    using (UI.BeginContainer(tabId, EditorStyle.Inspector.EmitterTab))
                    {
                        if (selected)
                            UI.Container(EditorStyle.Inspector.EmitterTabSelected);
                        else if (UI.IsHovered())
                            UI.Container(EditorStyle.Inspector.EmitterTabHover);
                        else
                            UI.Container(EditorStyle.Inspector.EmitterTabFill);

                        using (UI.BeginContainer(new ContainerStyle { Padding = EdgeInsets.LeftRight(8), AlignY = Align.Center }))
                            UI.Text(Document.GetEmitterName(i), EditorStyle.Inspector.EmitterTabText);

                        if (UI.WasPressed())
                            Document.SelectedEmitterIndex = i;
                    }
                }

                // Add button
                if (UI.Button(ElementId.AddEmitterButton, EditorAssets.Sprites.IconDuplicate, EditorStyle.Button.IconOnly))
                    Document.AddEmitter($"emitter{Document.EmitterCount}");

                UI.Flex();

                // Remove button (only if selected)
                if (Document.EmitterCount > 0)
                {
                    if (UI.Button(ElementId.RemoveEmitterButton, EditorAssets.Sprites.IconDelete, EditorStyle.Button.IconOnly))
                        Document.RemoveEmitter(Document.SelectedEmitterIndex);
                }
            }
        }

        UI.Container(EditorStyle.Inspector.Separator);
    }

    private void EmitterPropertiesUI()
    {
        var index = Document.SelectedEmitterIndex;
        if (index < 0 || index >= Document.EmitterCount)
            return;

        ref var e = ref Document.GetEmitterDef(index);
        var changed = false;

        UI.Spacer(EditorStyle.Control.Spacing / 2);
        using (UI.BeginRow(EditorStyle.Inspector.SectionHeader)) UI.Text("Emitter", EditorStyle.Inspector.SectionText);
        UI.Container(EditorStyle.Inspector.Separator);

        if (IntRangeField(NextFieldId(2), "Rate", ref e.Rate)) changed = true;
        if (IntRangeField(NextFieldId(2), "Burst", ref e.Burst)) changed = true;
        if (RangeField(NextFieldId(2), "Duration", ref e.Duration)) changed = true;
        if (RangeField(NextFieldId(2), "Angle", ref e.Angle)) changed = true;
        if (Vec2RangeField(NextFieldId(4), "Spawn", ref e.Spawn)) changed = true;
        if (Vec2RangeField(NextFieldId(4), "Direction", ref e.Direction)) changed = true;

        if (changed)
            Document.ApplyChanges();
    }

    private void ParticlePropertiesUI()
    {
        var index = Document.SelectedEmitterIndex;
        if (index < 0 || index >= Document.EmitterCount)
            return;

        ref var p = ref Document.GetEmitterDef(index).Particle;
        var changed = false;

        UI.Spacer(EditorStyle.Control.Spacing / 2);
        using (UI.BeginRow(EditorStyle.Inspector.SectionHeader)) UI.Text("Particle", EditorStyle.Inspector.SectionText);
        UI.Container(EditorStyle.Inspector.Separator);

        if (RangeField(NextFieldId(2), "Duration", ref p.Duration)) changed = true;
        if (Vec2RangeField(NextFieldId(4), "Gravity", ref p.Gravity)) changed = true;
        if (RangeField(NextFieldId(2), "Drag", ref p.Drag)) changed = true;

        UI.Spacer(EditorStyle.Control.Spacing / 2);
        using (UI.BeginRow(EditorStyle.Inspector.SectionHeader)) UI.Text("Curves", EditorStyle.Inspector.SectionText);
        UI.Container(EditorStyle.Inspector.Separator);

        if (FloatCurveField(NextFieldId(5), "Size", ref p.Size)) changed = true;
        if (FloatCurveField(NextFieldId(5), "Speed", ref p.Speed)) changed = true;
        if (FloatCurveField(NextFieldId(5), "Opacity", ref p.Opacity)) changed = true;
        if (FloatCurveField(NextFieldId(5), "Rotation", ref p.Rotation)) changed = true;
        if (ColorCurveField(NextFieldId(5), "Color", ref p.Color)) changed = true;

        if (changed)
            Document.ApplyChanges();
    }

    private void TogglePlayback()
    {
        Document.TogglePlay();
    }

    // --- VFX Inspector Fields (moved from EditorUI.Inspector.cs) ---

    private static void InspectorLabel(string label)
    {
        using (UI.BeginContainer(new ContainerStyle
        {
            Width = EditorStyle.Inspector.LabelWidth,
            AlignY = Align.Center
        }))
            UI.Text(label, EditorStyle.Inspector.Label);
    }

    private static float _fieldFloat;
    private static float _fieldFloat2;
    private static int _fieldInt;
    private static int _fieldInt2;

    private static bool FloatField(WidgetId id, ref float value, string? placeholder = null)
    {
        var changed = false;
        Span<char> buf = stackalloc char[32];
        value.TryFormat(buf, out var written, "G");

        using (UI.BeginContainer(EditorStyle.Inspector.FieldContainer))
        {
            //if (UI.TextInput(id, buf[..written], EditorStyle.TextInput, placeholder ?? "0"))
            //{
            //    if (float.TryParse(UI.GetElementText(id), out var parsed))
            //    {
            //        value = parsed;
            //        changed = true;
            //    }
            //}
        }
        return changed;
    }

    private static bool IntField(WidgetId id, ref int value, string? placeholder = null)
    {
        var changed = false;
        Span<char> buf = stackalloc char[16];
        value.TryFormat(buf, out var written);

        using (UI.BeginContainer(EditorStyle.Inspector.FieldContainer))
        {
            //if (UI.TextInput(id, buf[..written], EditorStyle.TextInput, placeholder ?? "0"))
            //{
            //    if (int.TryParse(UI.GetElementText(id), out var parsed))
            //    {
            //        value = parsed;
            //        changed = true;
            //    }
            //}
        }
        return changed;
    }

    private static bool RangeField(WidgetId id, string label, ref VfxRange value)
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

    private static bool IntRangeField(WidgetId id, string label, ref VfxIntRange value)
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

    private static bool Vec2RangeField(WidgetId id, string label, ref VfxVec2Range value)
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

    private static bool FloatCurveField(WidgetId id, string label, ref VfxFloatCurve curve)
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

    private static bool ColorCurveField(WidgetId id, string label, ref VfxColorCurve curve)
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

    private static bool ColorField(WidgetId id, ref Color color)
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
                    //if (UI.TextInput(id, (ReadOnlySpan<char>)hex, EditorStyle.TextInput, "#fff"))
                    //{
                    //    var text = UI.GetElementText(id).ToString();
                    //    if (TryParseHexColor(text, out var parsed))
                    //    {
                    //        color = parsed;
                    //        changed = true;
                    //    }
                    //}
                }
            }
        }

        return changed;
    }

    private static readonly string[] CurveTypeNames =
        ["Linear", "EaseIn", "EaseOut", "EaseInOut", "Quadratic", "Cubic", "Sine"];

    private static VfxCurveType _curveTypeValue;

    private static bool CurveTypeButton(WidgetId id, ref VfxCurveType curveType)
    {
        var oldType = curveType;
        _curveTypeValue = curveType;
        var typeName = CurveTypeNames[(int)curveType];

        void Content()
        {
            EditorUI.ControlText(typeName);
        }

        if (EditorUI.Control(id, Content, selected: EditorUI.IsPopupOpen(id), padding: true))
            EditorUI.TogglePopup(id);

        if (EditorUI.IsPopupOpen(id))
        {
            static void PopupContent()
            {
                for (int i = 0; i < CurveTypeNames.Length; i++)
                {
                    var type = (VfxCurveType)i;
                    if (EditorUI.PopupItem(CurveTypeNames[i], selected: _curveTypeValue == type))
                    {
                        _curveTypeValue = type;
                        EditorUI.ClosePopup();
                    }
                }
            }
            EditorUI.Popup(id, PopupContent);
        }

        curveType = _curveTypeValue;
        return curveType != oldType;
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
