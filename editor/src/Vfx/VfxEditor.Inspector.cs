//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class VfxEditor
{
    private static partial class FieldId
    {
        public static partial WidgetId VfxDuration { get; }
        public static partial WidgetId VfxLoop { get; }

        public static partial WidgetId EmitterRate { get; }
        public static partial WidgetId EmitterBurst { get; }
        public static partial WidgetId EmitterDuration { get; }
        public static partial WidgetId EmitterWorldSpace { get; }
        public static partial WidgetId EmitterParticle { get; }
        public static partial WidgetId EmitterSpawnShape { get; }
        public static partial WidgetId EmitterSpawnOffset { get; }
        public static partial WidgetId EmitterSpawnRadius { get; }
        public static partial WidgetId EmitterSpawnInnerRadius { get; }
        public static partial WidgetId EmitterSpawnSize { get; }
        public static partial WidgetId EmitterSpawnInnerSize { get; }
        public static partial WidgetId EmitterSpawnRotation { get; }
        public static partial WidgetId EmitterDirection { get; }  // +0..+2
        public static partial WidgetId EmitterSpread { get; }     // +0..+2
        public static partial WidgetId EmitterRadial { get; }

        public static partial WidgetId ParticleDuration { get; }
        public static partial WidgetId ParticleSize { get; }
        public static partial WidgetId ParticleSpeed { get; }
        public static partial WidgetId ParticleColor { get; }
        public static partial WidgetId ParticleOpacity { get; }
        public static partial WidgetId ParticleGravity { get; }
        public static partial WidgetId ParticleRotation { get; }
        public static partial WidgetId ParticleRotationSpeed { get; }
        public static partial WidgetId ParticleAlignToDirection { get; }
        public static partial WidgetId ParticleFrameMode { get; }

        // Addable section buttons
        public static partial WidgetId AddSize { get; }
        public static partial WidgetId AddSpeed { get; }
        public static partial WidgetId AddColor { get; }
        public static partial WidgetId AddOpacity { get; }
        public static partial WidgetId AddGravity { get; }
        public static partial WidgetId AddRotation { get; }
        public static partial WidgetId AddRotationSpeed { get; }
        public static partial WidgetId RemoveSize { get; }
        public static partial WidgetId RemoveSpeed { get; }
        public static partial WidgetId RemoveColor { get; }
        public static partial WidgetId RemoveOpacity { get; }
        public static partial WidgetId RemoveGravity { get; }
        public static partial WidgetId RemoveRotation { get; }
        public static partial WidgetId RemoveRotationSpeed { get; }
        public static partial WidgetId SpriteDropDown { get; }
        public static partial WidgetId ParticleSort { get; }

        // Addable section persistent state
        public static partial WidgetId SectionSize { get; }
        public static partial WidgetId SectionSpeed { get; }
        public static partial WidgetId SectionColor { get; }
        public static partial WidgetId SectionOpacity { get; }
        public static partial WidgetId SectionGravity { get; }
        public static partial WidgetId SectionRotation { get; }
        public static partial WidgetId SectionRotationSpeed { get; }
    }

    private struct AddableSectionState
    {
        public bool Initialized;
        public bool IsActive;
    }

    private struct CurveFieldState
    {
        public bool Initialized;
        public bool HasCurve;
        public bool HasRandomStart;
        public bool HasRandomEnd;
    }

    private static ref CurveFieldState BeginFieldState(WidgetId id, bool dataCurve, bool dataRandomStart, bool dataRandomEnd = false)
    {
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<CurveFieldState>(id, interactive: false);
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.HasCurve = dataCurve;
            state.HasRandomStart = dataRandomStart;
            state.HasRandomEnd = dataRandomEnd;
        }
        ElementTree.EndTree();
        return ref state;
    }

    public override void InspectorUI()
    {
        if (Document.HasMultiSelection)
        {
            UI.Text("Multiple items selected", EditorStyle.Text.Secondary);
            return;
        }

        var selectedType = Document.SingleSelectedType;
        var selectedIndex = Document.SingleSelectedIndex;

        switch (selectedType)
        {
            case VfxSelectionType.Vfx:
                VfxInspectorUI();
                break;

            case VfxSelectionType.Emitter:
                if (selectedIndex >= 0 && selectedIndex < Document.Emitters.Count)
                    EmitterInspectorUI(Document.Emitters[selectedIndex], selectedIndex);
                break;

            case VfxSelectionType.Particle:
                if (selectedIndex >= 0 && selectedIndex < Document.Particles.Count)
                    ParticleInspectorUI(Document.Particles[selectedIndex]);
                break;
        }
    }

    private void VfxInspectorUI()
    {
        using (Inspector.BeginSection("VFX"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var duration = Document.Duration;
            if (RangeField(FieldId.VfxDuration, "Duration", ref duration))
            {
                Document.Duration = duration;
                Document.ApplyChanges();
            }

            var loop = Document.Loop;
            using (Inspector.BeginProperty("Loop"))
            {
                if (UI.Toggle(FieldId.VfxLoop, loop, EditorStyle.Inspector.Toggle))
                {
                    Undo.Record(Document);
                    Document.Loop = !loop;
                    Document.ApplyChanges();
                }
            }
        }
    }

    private void EmitterInspectorUI(VfxDocEmitter emitter, int index)
    {
        using (Inspector.BeginSection("EMITTER"))
        {
            if (Inspector.IsSectionCollapsed) return;

            var changed = false;
            var rate = emitter.Rate;
            var burst = emitter.Burst;
            var duration = emitter.Duration;
            var worldSpace = emitter.WorldSpace;

            if (FloatCurveField(FieldId.EmitterRate, "Rate", ref rate)) changed = true;
            if (IntRangeField(FieldId.EmitterBurst, "Burst", ref burst)) changed = true;
            if (RangeField(FieldId.EmitterDuration, "Duration", ref duration)) changed = true;

            using (Inspector.BeginProperty("WorldSpace"))
            {
                worldSpace = UI.Toggle(FieldId.EmitterWorldSpace, worldSpace, EditorStyle.Inspector.Toggle);
                changed = changed || UI.WasChanged();
            }

            // Particle dropdown
            using (Inspector.BeginProperty("Particle"))
            {
                var currentName = emitter.ParticleRef;
                UI.DropDown(FieldId.EmitterParticle, () =>
                {
                    var items = new List<PopupMenuItem>();
                    foreach (var p in Document.Particles)
                    {
                        var name = p.Name;
                        items.Add(PopupMenuItem.Item(name, () =>
                        {
                            Undo.Record(Document);
                            emitter.ParticleRef = name;
                            Document.ApplyChanges();
                        }));
                    }
                    return [.. items];
                }, text: currentName);
            }

            if (changed)
            {
                emitter.Rate = rate;
                emitter.Burst = burst;
                emitter.Duration = duration;
                emitter.WorldSpace = worldSpace;
                Document.ApplyChanges();
            }
        }

        using (Inspector.BeginSection("SPAWN"))
        {
            if (!Inspector.IsSectionCollapsed)
                SpawnDefFields(emitter, index);
        }

        using (Inspector.BeginSection("DIRECTION"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                var direction = emitter.Direction;
                var spread = emitter.Spread;
                var radial = emitter.Radial;

                using (Inspector.BeginProperty("Direction"))
                    direction = FloatInput(FieldId.EmitterDirection + index * 4, direction);
                using (Inspector.BeginProperty("Spread"))
                    spread = FloatInput(FieldId.EmitterSpread + index * 4, spread);
                using (Inspector.BeginProperty("Radial"))
                    radial = FloatInput(FieldId.EmitterRadial + index * 4, radial);
                if (direction != emitter.Direction || spread != emitter.Spread || radial != emitter.Radial)
                {
                    emitter.Direction = direction;
                    emitter.Spread = spread;
                    emitter.Radial = radial;
                    Document.ApplyChanges();
                }
            }
        }
    }

    // --- Spawn Shape Fields ---

    private static readonly (string Name, VfxSpawnShape Shape)[] SpawnShapeOptions =
    [
        ("Point", VfxSpawnShape.Point),
        ("Circle", VfxSpawnShape.Circle),
        ("Box", VfxSpawnShape.Box),
    ];

    private static bool _spawnShapeChanged;
    private static VfxSpawnShape _spawnShapeNewValue;
    private static WidgetId _spawnShapeChangedId;

    private void SpawnDefFields(VfxDocEmitter emitter, int index)
    {
        var spawn = emitter.Spawn;
        var changed = false;

        // Shape dropdown
        using (Inspector.BeginProperty("Shape"))
        {
            var currentName = spawn.Shape.ToString();
            if (_spawnShapeChanged && _spawnShapeChangedId == FieldId.EmitterSpawnShape)
            {
                _spawnShapeChanged = false;
                spawn = new VfxSpawnDef { Shape = _spawnShapeNewValue };
                if (_spawnShapeNewValue == VfxSpawnShape.Circle)
                    spawn.Circle.Radius = 1f;
                else if (_spawnShapeNewValue == VfxSpawnShape.Box)
                    spawn.Box.Size = new Vector2(1f, 1f);
                changed = true;
            }

            UI.DropDown(FieldId.EmitterSpawnShape, () =>
            {
                var items = new PopupMenuItem[SpawnShapeOptions.Length];
                for (var i = 0; i < SpawnShapeOptions.Length; i++)
                {
                    var opt = SpawnShapeOptions[i];
                    items[i] = PopupMenuItem.Item(opt.Name, () =>
                    {
                        _spawnShapeChanged = true;
                        _spawnShapeChangedId = FieldId.EmitterSpawnShape;
                        _spawnShapeNewValue = opt.Shape;
                    });
                }
                return items;
            }, text: currentName);
        }

        // Offset (all shapes)
        using (Inspector.BeginProperty("Offset"))
        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            spawn.Offset.X = FloatInput(FieldId.EmitterSpawnOffset + index * 4, spawn.Offset.X);
            spawn.Offset.Y = FloatInput(FieldId.EmitterSpawnOffset + 2 + index * 4, spawn.Offset.Y);
        }

        // Shape-specific fields
        switch (spawn.Shape)
        {
            case VfxSpawnShape.Circle:
                using (Inspector.BeginProperty("Radius"))
                    spawn.Circle.Radius = FloatInput(FieldId.EmitterSpawnRadius + index * 4, spawn.Circle.Radius);
                using (Inspector.BeginProperty("Inner Radius"))
                    spawn.Circle.InnerRadius = FloatInput(FieldId.EmitterSpawnInnerRadius + index * 4, spawn.Circle.InnerRadius);
                break;

            case VfxSpawnShape.Box:
                using (Inspector.BeginProperty("Size"))
                using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
                {
                    spawn.Box.Size.X = FloatInput(FieldId.EmitterSpawnSize + index * 4, spawn.Box.Size.X);
                    spawn.Box.Size.Y = FloatInput(FieldId.EmitterSpawnSize + 2 + index * 4, spawn.Box.Size.Y);
                }
                using (Inspector.BeginProperty("Inner Size"))
                using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
                {
                    spawn.Box.InnerSize.X = FloatInput(FieldId.EmitterSpawnInnerSize + index * 4, spawn.Box.InnerSize.X);
                    spawn.Box.InnerSize.Y = FloatInput(FieldId.EmitterSpawnInnerSize + 2 + index * 4, spawn.Box.InnerSize.Y);
                }
                using (Inspector.BeginProperty("Rotation"))
                    spawn.Box.Rotation = FloatInput(FieldId.EmitterSpawnRotation + index * 4, spawn.Box.Rotation);
                break;
        }

        var spawnChanged = spawn.Offset != emitter.Spawn.Offset
            || spawn.Shape != emitter.Spawn.Shape
            || spawn.Circle.Radius != emitter.Spawn.Circle.Radius
            || spawn.Circle.InnerRadius != emitter.Spawn.Circle.InnerRadius
            || spawn.Box.Size != emitter.Spawn.Box.Size
            || spawn.Box.InnerSize != emitter.Spawn.Box.InnerSize
            || spawn.Box.Rotation != emitter.Spawn.Box.Rotation;
        if (changed || spawnChanged)
        {
            emitter.Spawn = spawn;
            Document.ApplyChanges();
        }
    }

    // --- Particle Inspector ---

    private static readonly (string Name, VfxFrameMode Mode)[] FrameModeOptions =
    [
        ("Time", VfxFrameMode.Time),
        ("Random", VfxFrameMode.Random),
    ];

    private static bool _frameModeChanged;
    private static VfxFrameMode _frameModeNewValue;

    private void ParticleInspectorUI(VfxDocParticle particle)
    {
        using (Inspector.BeginSection("PARTICLE"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                var duration = particle.Duration;
                if (RangeField(FieldId.ParticleDuration, "Duration", ref duration))
                {
                    particle.Duration = duration;
                    Document.ApplyChanges();
                }

                using (Inspector.BeginProperty("Sprite"))
                {
                    var newRef = EditorUI.SpriteField(FieldId.SpriteDropDown, particle.SpriteRef);
                    if (newRef.Value != particle.SpriteRef.Value)
                    {
                        Undo.Record(Document);
                        particle.SpriteRef = newRef;
                        Document.ApplyChanges();
                    }
                }

                using (Inspector.BeginProperty("Frame"))
                {
                    if (_frameModeChanged)
                    {
                        _frameModeChanged = false;
                        Undo.Record(Document);
                        particle.FrameMode = _frameModeNewValue;
                        Document.ApplyChanges();
                    }

                    UI.DropDown(FieldId.ParticleFrameMode, () =>
                    {
                        var items = new PopupMenuItem[FrameModeOptions.Length];
                        for (var i = 0; i < FrameModeOptions.Length; i++)
                        {
                            var opt = FrameModeOptions[i];
                            items[i] = PopupMenuItem.Item(opt.Name, () =>
                            {
                                _frameModeChanged = true;
                                _frameModeNewValue = opt.Mode;
                            });
                        }
                        return items;
                    }, text: particle.FrameMode.ToString());
                }

                using (Inspector.BeginProperty("Sort"))
                {
                    var sortVal = (int)particle.Sort;
                    if (IntInput(FieldId.ParticleSort, ref sortVal))
                    {
                        Undo.Record(Document);
                        particle.Sort = (ushort)Math.Clamp(sortVal, 0, ushort.MaxValue);
                        Document.ApplyChanges();
                    }
                }
            }
        }

        // Addable particle groups
        if (AddableSection("SIZE", particle.Size != VfxDocFloatCurve.One, FieldId.SectionSize, FieldId.AddSize, FieldId.RemoveSize,
            () => { particle.Size = new VfxDocFloatCurve { Start = new VfxRange(0.5f, 0.5f), End = new VfxRange(0f, 0.1f), CurveType = VfxCurveType.Quadratic, EaseType = VfxEaseType.Out, WindowEnd = 1f }; },
            () => { particle.Size = VfxDocFloatCurve.One; }))
        {
            var size = particle.Size;
            if (FloatCurveField(FieldId.ParticleSize, "Size", ref size))
            {
                particle.Size = size;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("SPEED", particle.Speed != VfxDocFloatCurve.Zero, FieldId.SectionSpeed, FieldId.AddSpeed, FieldId.RemoveSpeed,
            () => { particle.Speed = new VfxDocFloatCurve { Start = new VfxRange(10, 20), End = new VfxRange(0, 5), CurveType = VfxCurveType.Linear, EaseType = VfxEaseType.In, WindowEnd = 1f }; },
            () => { particle.Speed = VfxDocFloatCurve.Zero; }))
        {
            var speed = particle.Speed;
            if (FloatCurveField(FieldId.ParticleSpeed, "Speed", ref speed))
            {
                particle.Speed = speed;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("COLOR", particle.Color != VfxDocColorCurve.White, FieldId.SectionColor, FieldId.AddColor, FieldId.RemoveColor,
            () => { particle.Color = new VfxDocColorCurve { Start = new VfxColorRange(Color.White, Color.White), End = new VfxColorRange(Color.Yellow, Color.Yellow), CurveType = VfxCurveType.Linear, EaseType = VfxEaseType.In, WindowEnd = 1f }; },
            () => { particle.Color = VfxDocColorCurve.White; }))
        {
            var color = particle.Color;
            if (ColorCurveField(FieldId.ParticleColor, "Color", ref color))
            {
                Undo.Record(Document);
                particle.Color = color;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("OPACITY", particle.Opacity != VfxDocFloatCurve.One, FieldId.SectionOpacity, FieldId.AddOpacity, FieldId.RemoveOpacity,
            () => { particle.Opacity = new VfxDocFloatCurve { Start = VfxRange.One, End = VfxRange.Zero, CurveType = VfxCurveType.Quadratic, EaseType = VfxEaseType.Out, WindowEnd = 1f }; },
            () => { particle.Opacity = VfxDocFloatCurve.One; }))
        {
            var opacity = particle.Opacity;
            if (FloatCurveField(FieldId.ParticleOpacity, "Opacity", ref opacity))
            {
                particle.Opacity = opacity;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

        if (AddableSection("GRAVITY", particle.Gravity != VfxDocFloatCurve.Zero, FieldId.SectionGravity, FieldId.AddGravity, FieldId.RemoveGravity,
            () => { particle.Gravity = new VfxDocFloatCurve { Start = new VfxRange(10, 10), End = new VfxRange(10, 10), WindowEnd = 1f }; },
            () => { particle.Gravity = VfxDocFloatCurve.Zero; }))
        {
            var gravity = particle.Gravity;
            if (FloatCurveField(FieldId.ParticleGravity, "Gravity", ref gravity))
            {
                particle.Gravity = gravity;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }


        if (AddableSection("ROTATION", particle.Rotation != VfxRange.Zero, FieldId.SectionRotation, FieldId.AddRotation, FieldId.RemoveRotation,
            () => { particle.Rotation = new VfxRange(0, 360); },
            () => { particle.Rotation = VfxRange.Zero; }))
        {
            var rotation = particle.Rotation;
            if (RangeField(FieldId.ParticleRotation, "Rotation", ref rotation))
            {
                particle.Rotation = rotation;
                Document.ApplyChanges();
            }

            using (Inspector.BeginProperty("Align to Direction"))
            {
                var alignToDirection = UI.Toggle(FieldId.ParticleAlignToDirection, particle.AlignToDirection, EditorStyle.Inspector.Toggle);
                if (alignToDirection != particle.AlignToDirection)
                {
                    particle.AlignToDirection = alignToDirection;
                    Document.ApplyChanges();
                }
            }
            EndAddableSection();
        }

        if (AddableSection("ROTATION SPEED", particle.RotationSpeed != VfxDocFloatCurve.Zero, FieldId.SectionRotationSpeed, FieldId.AddRotationSpeed, FieldId.RemoveRotationSpeed,
            () => { particle.RotationSpeed = new VfxDocFloatCurve { Start = new VfxRange(-180, 180), End = new VfxRange(-180, 180), WindowEnd = 1f }; },
            () => { particle.RotationSpeed = VfxDocFloatCurve.Zero; }))
        {
            var rotSpeed = particle.RotationSpeed;
            if (FloatCurveField(FieldId.ParticleRotationSpeed, "Speed", ref rotSpeed))
            {
                particle.RotationSpeed = rotSpeed;
                Document.ApplyChanges();
            }
            EndAddableSection();
        }

    }

    // --- Addable Section Helper ---

    private Inspector.AutoSection _addableSectionHandle;

    private static bool _sectionToggled;
    private static WidgetId _sectionToggledId;
    private static bool _sectionToggledValue;

    private bool AddableSection(string name, bool dataIsActive, WidgetId stateId, WidgetId addId, WidgetId removeId, Action onAdd, Action onRemove)
    {
        // Persistent state: initialized from data once, then only changed by explicit button clicks
        ElementTree.BeginTree();
        ref var state = ref ElementTree.BeginWidget<AddableSectionState>(stateId, interactive: false);
        if (!state.Initialized)
        {
            state.Initialized = true;
            state.IsActive = dataIsActive;
        }
        // Apply deferred toggle from previous frame's button press
        if (_sectionToggled && _sectionToggledId == stateId)
        {
            _sectionToggled = false;
            state.IsActive = _sectionToggledValue;
        }
        ElementTree.EndTree();

        if (!state.IsActive)
        {
            using (Inspector.BeginSection(name, content: () =>
            {
                ElementTree.BeginAlign(Align.Min, Align.Center);
                if (UI.Button(addId, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
                {
                    Undo.Record(Document);
                    onAdd();
                    _sectionToggled = true;
                    _sectionToggledId = stateId;
                    _sectionToggledValue = true;
                    Document.ApplyChanges();
                }
                ElementTree.EndAlign();
            }, empty: true)) { }
            return false;
        }

        _addableSectionHandle = Inspector.BeginSection(name, content: () =>
        {
            ElementTree.BeginAlign(Align.Min, Align.Center);
            if (UI.Button(removeId, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
            {
                Undo.Record(Document);
                onRemove();
                _sectionToggled = true;
                _sectionToggledId = stateId;
                _sectionToggledValue = false;
                Document.ApplyChanges();
            }
            ElementTree.EndAlign();
        });

        if (Inspector.IsSectionCollapsed)
        {
            ((IDisposable)_addableSectionHandle).Dispose();
            return false;
        }

        return true;
    }

    private void EndAddableSection()
    {
        ((IDisposable)_addableSectionHandle).Dispose();
    }

    // --- Field Helpers ---

    private static readonly ContainerStyle ValueRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = EditorStyle.Control.Height };
    private static readonly ContainerStyle CurveRowStyle = new() { Spacing = 4, Height = Size.Fit, MinHeight = 22 };

    // Range field: [label] [min] [⇄] [max]  — no curve row
    // baseId+0,+1: min float (scrub+text), +2: randomToggle, +3,+4: max float (scrub+text), +5: state
    private bool RangeField(WidgetId baseId, string label, ref VfxRange value)
    {
        var changed = false;
        ref var state = ref BeginFieldState(baseId + 5,
            dataCurve: false,
            dataRandomStart: value.Min != value.Max);
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            var min = FloatInput(baseId, value.Min);
            if (min != value.Min) { value.Min = min; changed = true; if (!state.HasRandomStart) value.Max = min; }
            if (RandomToggleButton(baseId + 2, ref state.HasRandomStart, out var pressed)) changed = true;
            if (pressed && !state.HasRandomStart) value.Max = value.Min;
            if (state.HasRandomStart)
            {
                var max = FloatInput(baseId + 3, value.Max);
                if (max != value.Max) { value.Max = max; changed = true; }
            }
            else
                using (UI.BeginFlex()) { } // reserve space
        }
        return changed;
    }

    // Int range field: [label] [min] [⇄] [max]  — no curve row
    // baseId+0: min, +1: randomToggle, +2: max, +3: state
    private static bool IntRangeField(WidgetId baseId, string label, ref VfxIntRange value)
    {
        var changed = false;
        ref var state = ref BeginFieldState(baseId + 3,
            dataCurve: false,
            dataRandomStart: value.Min != value.Max);
        using (Inspector.BeginProperty(label))
        using (UI.BeginRow(ValueRowStyle))
        {
            if (IntInput(baseId, ref value.Min)) { changed = true; if (!state.HasRandomStart) value.Max = value.Min; }
            if (RandomToggleButton(baseId + 1, ref state.HasRandomStart, out var pressed)) changed = true;
            if (pressed && !state.HasRandomStart) value.Max = value.Min;
            if (state.HasRandomStart)
            {
                if (IntInput(baseId + 2, ref value.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }
        return changed;
    }

    // Vec2 range field: two rows of min/max (no random toggle — too complex)
    // baseId+0,+1: minX, +2,+3: minY, +4,+5: maxX, +6,+7: maxY
    private bool Vec2RangeField(WidgetId baseId, string label, ref VfxVec2Range value)
    {
        var changed = false;
        using (Inspector.BeginProperty(label))
        using (UI.BeginColumn(new ContainerStyle { Spacing = 2 }))
        {
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                var mx = FloatInput(baseId, value.Min.X);
                var my = FloatInput(baseId + 2, value.Min.Y);
                if (mx != value.Min.X) { value.Min.X = mx; changed = true; }
                if (my != value.Min.Y) { value.Min.Y = my; changed = true; }
            }
            using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
            {
                var mx = FloatInput(baseId + 4, value.Max.X);
                var my = FloatInput(baseId + 6, value.Max.Y);
                if (mx != value.Max.X) { value.Max.X = mx; changed = true; }
                if (my != value.Max.Y) { value.Max.Y = my; changed = true; }
            }
        }
        return changed;
    }

    // Float curve field — Start row, optional End row, then a clickable curve thumbnail
    // that opens the curve editor popup (ease in/out + window). End row appears when ease is set.
    // Each FloatInput consumes two adjacent widget IDs (scrub handle + text input).
    // baseId+0,+1: startMin, +2: startRandom, +3,+4: startMax,
    // +5,+6: endMin, +7: endRandom, +8,+9: endMax,
    // +10: curveThumbnail, +16: state
    private bool FloatCurveField(WidgetId baseId, string label, ref VfxDocFloatCurve curve)
    {
        var changed = false;
        var hasEase = curve.EaseType != VfxEaseType.None;
        ref var state = ref BeginFieldState(baseId + 16,
            dataCurve: hasEase,
            dataRandomStart: curve.Start.Min != curve.Start.Max,
            dataRandomEnd: curve.End.Min != curve.End.Max);
        var startLabel = hasEase ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            var smin = FloatInput(baseId, curve.Start.Min);
            if (smin != curve.Start.Min) { curve.Start.Min = smin; changed = true; if (!state.HasRandomStart) curve.Start.Max = smin; }
            if (RandomToggleButton(baseId + 2, ref state.HasRandomStart, out var startPressed)) changed = true;
            if (startPressed && !state.HasRandomStart) curve.Start.Max = curve.Start.Min;
            if (state.HasRandomStart)
            {
                var smax = FloatInput(baseId + 3, curve.Start.Max);
                if (smax != curve.Start.Max) { curve.Start.Max = smax; changed = true; }
            }
            else
                using (UI.BeginFlex()) { }
        }

        // End row — only when an ease is set
        if (hasEase)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                var emin = FloatInput(baseId + 5, curve.End.Min);
                if (emin != curve.End.Min) { curve.End.Min = emin; changed = true; if (!state.HasRandomEnd) curve.End.Max = emin; }
                if (RandomToggleButton(baseId + 7, ref state.HasRandomEnd, out var endPressed)) changed = true;
                if (endPressed && !state.HasRandomEnd) curve.End.Max = curve.End.Min;
                if (state.HasRandomEnd)
                {
                    var emax = FloatInput(baseId + 8, curve.End.Max);
                    if (emax != curve.End.Max) { curve.End.Max = emax; changed = true; }
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }
        else
        {
            // Keep End == Start while inactive so the curve is a no-op.
            curve.End = curve.Start;
        }

        // Curve thumbnail row — opens the curve editor popup
        using (Inspector.BeginProperty("Curve"))
        using (UI.BeginRow(CurveRowStyle))
        {
            if (CurveEditorPopup.Draw(baseId + 10, ref curve))
            {
                changed = true;
                UI.HandleChange(Document);
            }
        }

        return changed;
    }

    // Color curve field — same layout as float curves. ColorInput uses 1 ID; FloatInput uses 2.
    // baseId+0: startMin, +1: startRandom, +2: startMax, +3: endMin, +4: endRandom, +5: endMax,
    // +6: curveThumbnail, +12: state
    private bool ColorCurveField(WidgetId baseId, string label, ref VfxDocColorCurve curve)
    {
        var changed = false;
        var hasEase = curve.EaseType != VfxEaseType.None;
        ref var state = ref BeginFieldState(baseId + 12,
            dataCurve: hasEase,
            dataRandomStart: curve.Start.Min != curve.Start.Max,
            dataRandomEnd: curve.End.Min != curve.End.Max);
        var startLabel = hasEase ? "Start" : "Value";

        // Start/Value row
        using (Inspector.BeginProperty(startLabel))
        using (UI.BeginRow(ValueRowStyle))
        {
            using (UI.BeginFlex())
                if (ColorInput(baseId, ref curve.Start.Min)) { changed = true; if (!state.HasRandomStart) curve.Start.Max = curve.Start.Min; }
            if (RandomToggleButton(baseId + 1, ref state.HasRandomStart, out var startPressed)) changed = true;
            if (startPressed && !state.HasRandomStart) curve.Start.Max = curve.Start.Min;
            if (state.HasRandomStart)
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 2, ref curve.Start.Max)) changed = true;
            }
            else
                using (UI.BeginFlex()) { }
        }

        if (hasEase)
        {
            using (Inspector.BeginProperty("End"))
            using (UI.BeginRow(ValueRowStyle))
            {
                using (UI.BeginFlex())
                    if (ColorInput(baseId + 3, ref curve.End.Min)) { changed = true; if (!state.HasRandomEnd) curve.End.Max = curve.End.Min; }
                if (RandomToggleButton(baseId + 4, ref state.HasRandomEnd, out var endPressed)) changed = true;
                if (endPressed && !state.HasRandomEnd) curve.End.Max = curve.End.Min;
                if (state.HasRandomEnd)
                {
                    using (UI.BeginFlex())
                        if (ColorInput(baseId + 5, ref curve.End.Max)) changed = true;
                }
                else
                    using (UI.BeginFlex()) { }
            }
        }
        else
        {
            curve.End = curve.Start;
        }

        // Curve thumbnail row — opens the curve editor popup
        using (Inspector.BeginProperty("Curve"))
        using (UI.BeginRow(CurveRowStyle))
        {
            if (CurveEditorPopup.Draw(baseId + 6, ref curve))
            {
                changed = true;
                UI.HandleChange(Document);
            }
        }

        return changed;
    }

    // --- Random Toggle ---

    private static readonly ButtonStyle RandomButtonStyle = new()
    {
        Width = 21,
        Height = 21,
        Background = EditorStyle.Palette.Canvas,
        ContentColor = EditorStyle.Palette.SecondaryText,
        IconSize = 11,
        BorderRadius = EditorStyle.Control.BorderRadius,
        Resolve = (s, f) =>
        {
            if ((f & WidgetFlags.Hovered) != 0) s.Background = EditorStyle.Palette.Active;
            return s;
        },
    };

    private static readonly ButtonStyle RandomButtonActiveStyle = RandomButtonStyle with
    {
        Background = EditorStyle.Palette.Active,
        ContentColor = EditorStyle.Palette.Content,
    };

    // Random toggle button that reads/writes expanded state from parent CurveFieldState.
    // Returns true if pressed (state was toggled).
    private static bool RandomToggleButton(WidgetId id, ref bool isExpanded, out bool pressed)
    {
        var flags = WidgetFlags.None | (isExpanded ? WidgetFlags.Checked : WidgetFlags.None);

        ElementTree.BeginTree();
        ElementTree.BeginWidget(id);
        ElementTree.SetWidgetFlag(id, WidgetFlags.Checked, isExpanded);
        flags |= ElementTree.GetWidgetFlags();
        var style = EditorStyle.Button.ToggleIcon.Resolve!(EditorStyle.Button.ToggleIcon, flags);

        ElementTree.BeginSize(new Size2(style.Width, style.Height));
        ElementTree.BeginFill(style.Background, style.BorderRadius);
        ElementTree.BeginAlign(Align.Center);
        ElementTree.Image(EditorAssets.Sprites.IconRandomRange, style.IconSize, ImageStretch.Uniform, style.ContentColor);
        ElementTree.EndTree();

        pressed = flags.HasFlag(WidgetFlags.Pressed);
        if (pressed)
            isExpanded = !isExpanded;

        return pressed;
    }

    private float FloatInput(WidgetId id, float value)
    {
        float result;
        using (UI.BeginFlex())
            result = EditorUI.FloatInput(id, value, EditorStyle.Inspector.TextBox, step: 0.1f, fineStep: 0.01f);
        UI.HandleChange(Document);
        return result;
    }

    private static bool IntInput(WidgetId id, ref int value)
    {
        var text = value.ToString();
        bool changed = false;
        using (UI.BeginFlex())
        {
            var result = UI.TextInput(id, text, EditorStyle.Inspector.TextBox, "0");
            if (result != text && int.TryParse(result, out var parsed))
            {
                value = parsed;
                changed = true;
            }
        }
        return changed;
    }

    private static bool ColorInput(WidgetId id, ref Color color)
    {
        color = EditorUI.ColorButton(id, color, new ColorButtonStyle() with { FillWidth = true, ShowHDR = true });
        return UI.WasChanged();
    }

    private static bool ApproximatelyEqual(VfxColorRange a, VfxColorRange b)
    {
        return ApproximatelyEqual(a.Min, b.Min) && ApproximatelyEqual(a.Max, b.Max);
    }

    private static bool ApproximatelyEqual(Color a, Color b)
    {
        return MathEx.Approximately(a.R, b.R) &&
               MathEx.Approximately(a.G, b.G) &&
               MathEx.Approximately(a.B, b.B) &&
               MathEx.Approximately(a.A, b.A);
    }

}
