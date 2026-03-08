//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal partial class VfxEditor : DocumentEditor
{
    [ElementId("Root")]
    [ElementId("ToolbarRoot")]
    [ElementId("PlayButton")]
    [ElementId("LoopButton")]
    [ElementId("AddEmitterButton")]
    [ElementId("InspectorRoot")]
    [ElementId("InspectorScroll")]
    [ElementId("EmitterTab", 32)]
    [ElementId("RemoveEmitterButton")]
    [ElementId("Field", 256)]
    private static partial class ElementId { }

    private int _nextFieldId;

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

        if (EditorUI.ToggleButton(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, isChecked: Document.IsPlaying))
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


    private int NextFieldId(int count = 1)
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
            if (EditorUI.RangeField(NextFieldId(2), "Duration", ref duration))
            {
                Document.Duration = duration;
                Document.ApplyChanges();
            }

            var loop = Document.Loop;
            if (EditorUI.ToggleField(NextFieldId(), "Loop", ref loop))
            {
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
                if (EditorUI.Button(ElementId.AddEmitterButton, EditorAssets.Sprites.IconDuplicate))
                    Document.AddEmitter($"emitter{Document.EmitterCount}");

                UI.Flex();

                // Remove button (only if selected)
                if (Document.EmitterCount > 0)
                {
                    if (EditorUI.Button(ElementId.RemoveEmitterButton, EditorAssets.Sprites.IconDelete))
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

        EditorUI.SectionHeader("Emitter");

        if (EditorUI.IntRangeField(NextFieldId(2), "Rate", ref e.Rate)) changed = true;
        if (EditorUI.IntRangeField(NextFieldId(2), "Burst", ref e.Burst)) changed = true;
        if (EditorUI.RangeField(NextFieldId(2), "Duration", ref e.Duration)) changed = true;
        if (EditorUI.RangeField(NextFieldId(2), "Angle", ref e.Angle)) changed = true;
        if (EditorUI.Vec2RangeField(NextFieldId(4), "Spawn", ref e.Spawn)) changed = true;
        if (EditorUI.Vec2RangeField(NextFieldId(4), "Direction", ref e.Direction)) changed = true;

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

        EditorUI.SectionHeader("Particle");

        if (EditorUI.RangeField(NextFieldId(2), "Duration", ref p.Duration)) changed = true;
        if (EditorUI.Vec2RangeField(NextFieldId(4), "Gravity", ref p.Gravity)) changed = true;
        if (EditorUI.RangeField(NextFieldId(2), "Drag", ref p.Drag)) changed = true;

        EditorUI.SectionHeader("Curves");

        if (EditorUI.FloatCurveField(NextFieldId(5), "Size", ref p.Size)) changed = true;
        if (EditorUI.FloatCurveField(NextFieldId(5), "Speed", ref p.Speed)) changed = true;
        if (EditorUI.FloatCurveField(NextFieldId(5), "Opacity", ref p.Opacity)) changed = true;
        if (EditorUI.FloatCurveField(NextFieldId(5), "Rotation", ref p.Rotation)) changed = true;
        if (EditorUI.ColorCurveField(NextFieldId(5), "Color", ref p.Color)) changed = true;

        if (changed)
            Document.ApplyChanges();
    }

    private void TogglePlayback()
    {
        Document.TogglePlay();
    }
}
