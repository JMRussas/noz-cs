//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public partial class GenSpriteEditor : DocumentEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId LayerItem { get; }
        public static partial WidgetId GenerateButton { get; }
        public static partial WidgetId ConstraintDropDown { get; }
        public static partial WidgetId StyleDropDown { get; }
        public static partial WidgetId LayerSteps { get; }
        public static partial WidgetId LayerStrength { get; }
        public static partial WidgetId LayerGuidanceScale { get; }
        public static partial WidgetId LayerPrompt { get; }
        public static partial WidgetId LayerNegativePrompt { get; }
        public static partial WidgetId LayerDeleteButton { get; }
        public static partial WidgetId RefineSteps { get; }
        public static partial WidgetId RefineStrength { get; }
        public static partial WidgetId RefineGuidanceScale { get; }
        public static partial WidgetId RefinePrompt { get; }
        public static partial WidgetId RefineNegativePrompt { get; }
        public static partial WidgetId RefineDeleteButton { get; }
        public static partial WidgetId PathNormal { get; }
        public static partial WidgetId PathSubtract { get; }
        public static partial WidgetId PathClip { get; }
        public static partial WidgetId AddComponentButton { get; }
        public static partial WidgetId AddComponentPopup { get; }
        public static partial WidgetId CancelButton { get; }
    }

    private readonly Vector2[] _savedPositions = new Vector2[Shape.MaxAnchors];
    private readonly float[] _savedCurves = new float[Shape.MaxAnchors];
    private bool _hasPathSelection;

    public new GenSpriteDocument Document => (GenSpriteDocument)base.Document;

    public override bool ShowInspector => true;

    private Shape CurrentShape => Document.ActiveLayer.Shape;

    public GenSpriteEditor(GenSpriteDocument doc) : base(doc)
    {
        var deleteCommand = new Command { Name = "Delete", Handler = DeleteSelected, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete };
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab };
        var moveCommand = new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var rotateCommand = new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR };
        var scaleCommand = new Command { Name = "Scale", Handler = OnScale, Key = InputCode.KeyS };
        var copyCommand = new Command { Name = "Copy", Handler = CopySelected, Key = InputCode.KeyC, Ctrl = true };
        var pasteCommand = new Command { Name = "Paste", Handler = PasteSelected, Key = InputCode.KeyV, Ctrl = true };

        Commands =
        [
            deleteCommand,
            exitEditCommand,
            moveCommand,
            rotateCommand,
            scaleCommand,
            copyCommand,
            pasteCommand,
            new Command { Name = "Curve", Handler = BeginCurveTool, Key = InputCode.KeyC },
            new Command { Name = "Select All", Handler = SelectAll, Key = InputCode.KeyA },
            new Command { Name = "Insert Anchor", Handler = InsertAnchorAtHover, Key = InputCode.KeyV },
            new Command { Name = "Pen Tool", Handler = BeginPenTool, Key = InputCode.KeyP },
            new Command { Name = "Knife Tool", Handler = BeginKnifeTool, Key = InputCode.KeyK },
            new Command { Name = "Rectangle Tool", Handler = BeginRectangleTool, Key = InputCode.KeyR, Ctrl = true },
            new Command { Name = "Circle Tool", Handler = BeginCircleTool, Key = InputCode.KeyO, Ctrl = true },
            new Command { Name = "Duplicate", Handler = DuplicateSelected, Key = InputCode.KeyD, Ctrl = true },
            new Command { Name = "Generate", Handler = () => Document.GenerateAsync(), Key = InputCode.KeyG, Ctrl = true },
        ];
    }

    public override void OnUndoRedo()
    {
        Document.UpdateBounds();
    }

    public override void Update()
    {
        UpdateInput();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);
            Graphics.SetSortGroup(5);
            Document.DrawOrigin();
            Graphics.SetSortGroup(4);
            DrawAllLayerWireframes();
        }

        UpdateMesh();
        DrawMaskMesh();
        DrawGeneratedImage();
    }

    public override void LateUpdate()
    {
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
            HandleDragStart();
        else if (Input.WasButtonReleased(InputCode.MouseLeft))
            HandleLeftClick();
    }

    public override void UpdateUI() { }

    public override void InspectorUI()
    {
        if (_hasPathSelection)
            PathInspectorUI();
        else
            GenSpriteInspectorUI();

        LayersInspectorUI();
        RefineInspectorUI();

        AddComponentUI();

        UI.Flex();

        var genImage = Document.Generation;
        if (genImage.IsGenerating)
            ProgressUI(genImage);
        else
            GenerateButtonUI(genImage);
    }

    public override void Dispose()
    {
        ClearSelection();
        // TODO: migrate to UI.PopupMenu
        EditorUI.ClosePopup();
        base.Dispose();
    }

    #region Inspector

    private void ConstraintUI()
    {
        var sizes = EditorApplication.Config.SpriteSizes;
        var constraintLabel = "256x256";
        for (int i = 0; i < sizes.Length; i++)
            if (Document.ConstrainedSize == sizes[i].Size)
            {
                constraintLabel = sizes[i].Label;
                break;
            }

        var items = sizes.Select(s =>
            PopupMenuItem.Item(s.Label, () => SetConstraint(s.Size))).ToArray();

        UI.DropDown(WidgetIds.ConstraintDropDown, items, constraintLabel, EditorAssets.Sprites.IconConstraint);
    }

    private void StyleUI()
    {
        var items = new List<PopupMenuItem>
        {
            PopupMenuItem.Item("None", () => SetStyle(null))
        };
        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is GenStyleDocument styleDoc)
                items.Add(PopupMenuItem.Item(styleDoc.Name, () => SetStyle(styleDoc)));
        }

        UI.DropDown(WidgetIds.StyleDropDown, items.ToArray(), Document.StyleName ?? "None");
    }

    private void GenSpriteInspectorUI()
    {
        if (_hasPathSelection)
            return;

        using var _ = Inspector.BeginSection("GENSPRITE");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            ConstraintUI();

        using (Inspector.BeginRow())
        using (UI.BeginFlex())
            StyleUI();
    }

    private void SetStyle(GenStyleDocument? style)
    {
        Undo.Record(Document);
        Document.StyleName = style?.Name;
        Document.Style = style;
        Document.SaveMetadata();
        Document.IncrementVersion();
    }

    private void SetConstraint(Vector2Int size)
    {
        Undo.Record(Document);
        Document.ConstrainedSize = size;
        Document.UpdateBounds();
        Document.SaveMetadata();
        Document.IncrementVersion();
    }

    private void AddComponentUI()
    {
        using (UI.BeginContainer(new ContainerStyle { Padding = EdgeInsets.Symmetric(10, 16), AlignX = Align.Center }))
        {
            UI.SetChecked(EditorUI.IsPopupOpen(WidgetIds.AddComponentButton));
            if (UI.Button(WidgetIds.AddComponentButton, () =>
            {
                UI.Text("+ Add Component", EditorStyle.Control.Text);
            }, EditorStyle.Button.Secondary))
                // TODO: migrate to UI.PopupMenu
                EditorUI.TogglePopup(WidgetIds.AddComponentButton);
        }

        var hasRefine = Document.Refine != null;

        // TODO: migrate to UI.PopupMenu
        EditorUI.Popup(WidgetIds.AddComponentButton, () =>
        {
            // TODO: migrate to UI.PopupMenu
            if (EditorUI.PopupItem(EditorAssets.Sprites.IconLayer, "Layer"))
            {
                // TODO: migrate to UI.PopupMenu
                EditorUI.ClosePopup();
                Undo.Record(Document);
                Document.AddLayer();
                ClearSelection();
                Document.IncrementVersion();
            }

            // TODO: migrate to UI.PopupMenu
            if (EditorUI.PopupItem(EditorAssets.Sprites.IconAi, "Refine", disabled: hasRefine))
            {
                // TODO: migrate to UI.PopupMenu
                EditorUI.ClosePopup();
                Undo.Record(Document);
                Document.Refine = new GenerationConfig();
                Document.IncrementVersion();
            }
        });
    }

    private void ProgressUI(GenerationImage genImage)
    {
        using (UI.BeginColumn(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
            Spacing = 10,
        }))
        {
            UI.Text("PROGRESS", EditorStyle.Inspector.SectionText with
            {
                FontSize = EditorStyle.Inspector.LabelFontSize,
                Color = EditorStyle.Palette.Label,
            });

            var progressText = genImage.GenerationState switch
            {
                GenerationState.Queued when genImage.QueuePosition > 0 =>
                    $"Queued (position {genImage.QueuePosition})",
                GenerationState.Queued => "Queued...",
                GenerationState.Running when genImage.TotalSteps > 0 =>
                    $"Generating {genImage.CurrentStep}/{genImage.TotalSteps}",
                GenerationState.Running => "Processing...",
                _ => "Starting..."
            };

            using (UI.BeginRow(new ContainerStyle { Spacing = 8 }))
            {
                UI.Text(progressText, EditorStyle.Text.Primary with { FontSize = EditorStyle.Inspector.HeaderFontSize });
                UI.Flex();
                if (UI.Button(WidgetIds.CancelButton, EditorAssets.Sprites.IconClose, EditorStyle.Button.SmallIconOnly))
                    genImage.CancelGeneration();
            }

            using (UI.BeginContainer(new ContainerStyle
            {
                Width = Size.Percent(1),
                Height = 4f,
                Color = EditorStyle.Palette.Active,
                BorderRadius = 2f
            }))
            {
                UI.Container(new ContainerStyle
                {
                    Width = Size.Percent(genImage.GenerationProgress),
                    Height = 4f,
                    Color = EditorStyle.Palette.Primary,
                    BorderRadius = 2f
                });
            }
        }
    }

    private void GenerateButtonUI(GenerationImage genImage)
    {
        if (genImage.GenerationError != null)
            UI.Text(genImage.GenerationError, EditorStyle.Text.Secondary with { Color = EditorStyle.ErrorColor });

        using (UI.BeginContainer(new ContainerStyle
        {
            Padding = EdgeInsets.Symmetric(12, 16),
        }))
        {
            if (UI.Button(WidgetIds.GenerateButton, "Generate", EditorAssets.Sprites.IconAi, EditorStyle.Button.Primary with { Width = Size.Percent(1), MinWidth = 0, Height = 36 }))
                Document.GenerateAsync();
        }
    }

    private void LayersInspectorUI()
    {
        for (int i = Document.Layers.Count - 1; i >= 0; i--)
        {
            var layer = Document.Layers[i];
            var isActive = Document.ActiveLayerIndex == i;
            var layerIndex = i;

            var prompt = layer.Generation.Prompt;
            var title = string.IsNullOrEmpty(prompt) ? layer.Name
                : prompt.Length > 28 ? prompt[..28] + "..."
                : prompt;

            void LayerHeaderContent()
            {
                if (Document.Layers.Count > 1)
                {
                    if (UI.Button(WidgetIds.LayerDeleteButton + layerIndex, EditorAssets.Sprites.IconDelete, EditorStyle.Button.SmallIconOnly))
                    {
                        Undo.Record(Document);
                        Document.RemoveLayer(layerIndex);
                        ClearSelection();
                        Document.IncrementVersion();
                    }
                }
            }

            using (Inspector.BeginSection(title, icon: EditorAssets.Sprites.IconLayer, isActive: isActive, content: LayerHeaderContent, collapsed: !isActive))
            {
                if (Inspector.WasHeaderPressed && !isActive)
                {
                    Document.ActiveLayerIndex = layerIndex;
                    ClearSelection();
                }

                if (!Inspector.IsSectionCollapsed && isActive)
                {
                    var gen = layer.Generation;

                    GenerationParamsUI(
                        WidgetIds.LayerSteps + i,
                        WidgetIds.LayerStrength + i,
                        WidgetIds.LayerGuidanceScale + i,
                        gen);

                    using (Inspector.BeginRow())
                    using (UI.BeginFlex())
                        gen.Prompt = UI.TextInput(WidgetIds.LayerPrompt + i, gen.Prompt, EditorStyle.TextArea, "Prompt", Document);

                    using (Inspector.BeginRow())
                    using (UI.BeginFlex())
                        gen.NegativePrompt = UI.TextInput(WidgetIds.LayerNegativePrompt + i, gen.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document);
                }
            }
        }
    }

    private void RefineInspectorUI()
    {
        if (Document.Refine == null)
            return;

        void RefineHeaderContent()
        {
            if (UI.Button(WidgetIds.RefineDeleteButton, EditorAssets.Sprites.IconDelete, EditorStyle.Button.SmallIconOnly))
            {
                Undo.Record(Document);
                Document.Refine = null;
                Document.IncrementVersion();
            }
        }

        using (Inspector.BeginSection("Refine", icon: EditorAssets.Sprites.IconAi, content: RefineHeaderContent))
        {
            if (!Inspector.IsSectionCollapsed && Document.Refine != null)
            {
                var refine = Document.Refine;

                GenerationParamsUI(
                    WidgetIds.RefineSteps,
                    WidgetIds.RefineStrength,
                    WidgetIds.RefineGuidanceScale,
                    refine);

                using (Inspector.BeginRow())
                using (UI.BeginFlex())
                    refine.Prompt = UI.TextInput(WidgetIds.RefinePrompt, refine.Prompt, EditorStyle.TextArea, "Refine Prompt", Document);

                using (Inspector.BeginRow())
                using (UI.BeginFlex())
                    refine.NegativePrompt = UI.TextInput(WidgetIds.RefineNegativePrompt, refine.NegativePrompt, EditorStyle.TextArea, "Negative Prompt", Document);
            }
        }
    }

    private void PathInspectorUI()
    {
        using (Inspector.BeginSection("PATH"))
        {
            if (!Inspector.IsSectionCollapsed)
            {
                using (Inspector.BeginRow())
                {
                    var shape = CurrentShape;
                    var currentOp = GetSelectedPathOperation(shape);

                    PathToggle(WidgetIds.PathNormal, EditorAssets.Sprites.IconFill, "Fill", currentOp == PathOperation.Normal, PathOperation.Normal);
                    PathToggle(WidgetIds.PathSubtract, EditorAssets.Sprites.IconSubtract, "Sub", currentOp == PathOperation.Subtract, PathOperation.Subtract);
                    PathToggle(WidgetIds.PathClip, EditorAssets.Sprites.IconClip, "Clip", currentOp == PathOperation.Clip, PathOperation.Clip);
                }
            }
        }
    }

    private void PathToggle(WidgetId id, Sprite icon, string label, bool isChecked, PathOperation op)
    {
        var hovered = UI.IsHovered(id);
        var textColor = isChecked ? EditorStyle.Palette.Content : EditorStyle.Palette.Label;

        using (UI.BeginContainer(id, new ContainerStyle
        {
            Width = Size.Percent(1),
            Height = 32,
            BorderRadius = 4,
            Color = isChecked ? EditorStyle.Palette.Active : (hovered ? EditorStyle.Palette.Header : EditorStyle.Palette.Header),
            AlignX = Align.Center,
            AlignY = Align.Center,
            Spacing = 6,
        }))
        {
            UI.Image(icon, new ImageStyle
            {
                Size = 14,
                Color = textColor,
                Align = Align.Center,
            });
            UI.Text(label, new LabelStyle
            {
                FontSize = EditorStyle.Inspector.FontSize,
                Color = textColor,
                AlignY = Align.Center,
            });

            if (UI.WasPressed())
                SetPathOperation(op);
        }
    }

    private void GenerationParamsUI(WidgetId stepsId, WidgetId strengthId, WidgetId guidanceId, GenerationConfig gen)
    {
        using (UI.BeginRow(new ContainerStyle { Spacing = 4 }))
        {
            using (UI.BeginFlex())
            {
                var steps = gen.Steps;
                if (UI.NumberInput(stepsId, ref steps, EditorStyle.TextInput, min: 1, max: 100, icon: EditorAssets.Sprites.IconSort))
                {
                    Undo.Record(Document);
                    gen.Steps = steps;
                }
            }

            using (UI.BeginFlex())
            {
                var strength = gen.Strength;
                if (UI.NumberInput(strengthId, ref strength, EditorStyle.TextInput, min: 0f, max: 1f, step: 0.01f, format: "0.00", icon: EditorAssets.Sprites.IconOpacity))
                {
                    Undo.Record(Document);
                    gen.Strength = strength;
                }
            }

            using (UI.BeginFlex())
            {
                var guidance = gen.GuidanceScale;
                if (UI.NumberInput(guidanceId, ref guidance, EditorStyle.TextInput, min: 0f, max: 30f, step: 0.1f, format: "0.0", icon: EditorAssets.Sprites.IconConstraint))
                {
                    Undo.Record(Document);
                    gen.GuidanceScale = guidance;
                }
            }
        }
    }

    private static PathOperation GetSelectedPathOperation(Shape shape)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                var anchor = shape.GetAnchor((ushort)(path.AnchorStart + a));
                if (anchor.IsSelected)
                    return path.Operation;
            }
        }
        return PathOperation.Normal;
    }

    private void SetPathOperation(PathOperation operation)
    {
        var shape = CurrentShape;
        Undo.Record(Document);
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            var hasSelected = false;
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                if (shape.GetAnchor((ushort)(path.AnchorStart + a)).IsSelected)
                {
                    hasSelected = true;
                    break;
                }
            }
            if (hasSelected)
                shape.SetPathOperation(p, operation);
        }
        Document.IncrementVersion();
    }

    #endregion


    #region Input & Selection

    private void UpdateInput()
    {
        if (Input.WasButtonPressed(InputCode.KeyDelete))
            DeleteSelected();
    }

    private void HandleLeftClick()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var localMousePos = Vector2.Transform(Workspace.MouseWorldPosition, invTransform);
        var shift = Input.IsShiftDown(InputScope.All);

        // Hit test all layers from top to bottom
        for (int layerIdx = Document.Layers.Count - 1; layerIdx >= 0; layerIdx--)
        {
            var layer = Document.Layers[layerIdx];
            var shape = layer.Shape;
            var result = shape.HitTest(localMousePos);

            if (result.AnchorIndex != ushort.MaxValue)
            {
                if (layerIdx != Document.ActiveLayerIndex)
                {
                    ClearSelection();
                    Document.ActiveLayerIndex = layerIdx;
                }
                SelectAnchor(result.AnchorIndex, shift);
                return;
            }

            if (result.SegmentIndex != ushort.MaxValue)
            {
                if (layerIdx != Document.ActiveLayerIndex)
                {
                    ClearSelection();
                    Document.ActiveLayerIndex = layerIdx;
                }
                SelectSegment(result.SegmentIndex, shift);
                return;
            }

            if (result.PathIndex != ushort.MaxValue)
            {
                if (layerIdx != Document.ActiveLayerIndex)
                {
                    ClearSelection();
                    Document.ActiveLayerIndex = layerIdx;
                }
                SelectPath(result.PathIndex, shift);
                return;
            }
        }

        if (!shift)
            ClearSelection();
    }

    private void HandleDragStart()
    {
        Workspace.BeginTool(new BoxSelectTool(CommitBoxSelectAnchors));
    }

    private void CommitBoxSelectAnchors(Rect bounds)
    {
        var shape = CurrentShape;
        var shift = Input.IsShiftDown(InputScope.All);

        if (!shift)
            shape.ClearAnchorSelection();

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var minLocal = Vector2.Transform(bounds.Min, invTransform);
        var maxLocal = Vector2.Transform(bounds.Max, invTransform);
        var localRect = Rect.FromMinMax(minLocal, maxLocal);
        shape.SelectAnchors(localRect);

        UpdateSelection();
    }

    private void SelectAnchor(ushort anchorIndex, bool toggle)
    {
        var shape = CurrentShape;
        if (toggle)
        {
            var anchor = shape.GetAnchor(anchorIndex);
            shape.SetAnchorSelected(anchorIndex, !anchor.IsSelected);
        }
        else
        {
            shape.ClearSelection();
            shape.SetAnchorSelected(anchorIndex, true);
        }
        UpdateSelection();
    }

    private void SelectSegment(ushort anchorIndex, bool toggle)
    {
        var shape = CurrentShape;
        var pathIdx = FindPathForAnchor(shape, anchorIndex);
        if (pathIdx == ushort.MaxValue) return;

        var path = shape.GetPath(pathIdx);
        var nextAnchor = (ushort)(path.AnchorStart + ((anchorIndex - path.AnchorStart + 1) % path.AnchorCount));

        if (toggle)
        {
            var bothSelected = shape.GetAnchor(anchorIndex).IsSelected &&
                               shape.GetAnchor(nextAnchor).IsSelected;
            shape.SetAnchorSelected(anchorIndex, !bothSelected);
            shape.SetAnchorSelected(nextAnchor, !bothSelected);
        }
        else
        {
            shape.ClearSelection();
            shape.SetAnchorSelected(anchorIndex, true);
            shape.SetAnchorSelected(nextAnchor, true);
        }
        UpdateSelection();
    }

    private void SelectPath(ushort pathIndex, bool toggle)
    {
        var shape = CurrentShape;
        var path = shape.GetPath(pathIndex);

        if (toggle)
        {
            var allSelected = true;
            for (ushort a = 0; a < path.AnchorCount; a++)
            {
                if (!shape.GetAnchor((ushort)(path.AnchorStart + a)).IsSelected)
                {
                    allSelected = false;
                    break;
                }
            }
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), !allSelected);
        }
        else
        {
            shape.ClearSelection();
            for (ushort a = 0; a < path.AnchorCount; a++)
                shape.SetAnchorSelected((ushort)(path.AnchorStart + a), true);
        }
        UpdateSelection();
    }

    private void SelectAll()
    {
        var shape = CurrentShape;
        for (ushort i = 0; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected(i, true);
        UpdateSelection();
    }

    private void UpdateSelection()
    {
        _hasPathSelection = CurrentShape.HasSelection();
    }

    private void ClearSelection()
    {
        CurrentShape.ClearSelection();
        _hasPathSelection = false;
    }

    private void DeleteSelected()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection()) return;

        Undo.Record(Document);
        shape.DeleteAnchors();
        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.IncrementVersion();
        Document.UpdateBounds();
        _hasPathSelection = false;
    }

    #endregion

    #region Tools

    private void BeginPenTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new PenTool(Document, shape, Color32.White, PathOperation.Normal));
    }

    private void BeginKnifeTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new KnifeTool(Document, shape, commit: () =>
        {
            shape.UpdateSamples();
            shape.UpdateBounds();
        }));
    }

    private void BeginRectangleTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new ShapeTool(Document, shape, Color32.White, ShapeType.Rectangle, PathOperation.Normal));
    }

    private void BeginCircleTool()
    {
        var shape = CurrentShape;
        Workspace.BeginTool(new ShapeTool(Document, shape, Color32.White, ShapeType.Circle, PathOperation.Normal));
    }

    private void BeginMoveTool()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection()) return;

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                shape.TranslateAnchors(delta, _savedPositions, Input.IsCtrlDown(InputScope.All));
                shape.UpdateSamples();
                shape.UpdateBounds();
                _meshVersion = -1;
            },
            commit: _ =>
            {
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void BeginRotateTool()
    {
        var shape = CurrentShape;
        var localPivot = shape.GetSelectedAnchorsCentroid();
        if (!localPivot.HasValue) return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);
        Matrix3x2.Invert(Document.Transform, out var invTransform);

        Undo.Record(Document);
        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedPositions[i] = shape.GetAnchor(i).Position;

        Workspace.BeginTool(new RotateTool(
            worldPivot,
            localPivot.Value,
            worldOrigin,
            Vector2.Zero,
            invTransform,
            update: angle =>
            {
                var pivot = Input.IsShiftDown() ? Vector2.Zero : localPivot.Value;
                shape.RotateAnchors(pivot, angle, _savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
            },
            commit: _ =>
            {
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void OnScale()
    {
        var shape = CurrentShape;
        var localPivot = shape.GetSelectedAnchorsCentroid();
        if (!localPivot.HasValue) return;

        var worldPivot = Vector2.Transform(localPivot.Value, Document.Transform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);

        Undo.Record(Document);
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            _savedPositions[i] = shape.GetAnchor(i).Position;
            _savedCurves[i] = shape.GetAnchor(i).Curve;
        }

        Workspace.BeginTool(new ScaleTool(
            worldPivot,
            worldOrigin,
            update: scale =>
            {
                var pivot = Input.IsShiftDown(InputScope.All) ? Vector2.Zero : localPivot.Value;
                shape.ScaleAnchors(pivot, scale, _savedPositions, _savedCurves);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
            },
            commit: _ =>
            {
                shape.UpdateSamples();
                shape.UpdateBounds();
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () =>
            {
                shape.RestoreAnchorPositions(_savedPositions);
                shape.RestoreAnchorCurves(_savedCurves);
                shape.UpdateSamples();
                shape.UpdateBounds();
                Undo.Cancel();
            }
        ));
    }

    private void BeginCurveTool()
    {
        var shape = CurrentShape;

        var hasSelectedSegment = false;
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            if (shape.IsSegmentSelected(i))
            {
                hasSelectedSegment = true;
                break;
            }
        }
        if (!hasSelectedSegment) return;

        for (ushort i = 0; i < shape.AnchorCount; i++)
            _savedCurves[i] = shape.GetAnchor(i).Curve;

        Undo.Record(Document);

        Workspace.BeginTool(new CurveTool(
            shape,
            Document.Transform,
            _savedCurves,
            update: () => Document.IncrementVersion(),
            commit: () =>
            {
                Document.IncrementVersion();
                Document.UpdateBounds();
            },
            cancel: () => Undo.Cancel()
        ));
    }

    private void InsertAnchorAtHover()
    {
        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var hit = CurrentShape.HitTest(
            Vector2.Transform(Workspace.MouseWorldPosition, invTransform));

        if (hit.SegmentIndex == ushort.MaxValue) return;

        Undo.Record(Document);
        CurrentShape.SplitSegment(hit.SegmentIndex);
        Document.IncrementVersion();
        Document.UpdateBounds();
    }

    private void DuplicateSelected()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection()) return;

        Undo.Record(Document);

        Span<ushort> pathsToDuplicate = stackalloc ushort[Shape.MaxPaths];
        var pathCount = 0;

        for (ushort p = 0; p < shape.PathCount; p++)
        {
            ref readonly var path = ref shape.GetPath(p);
            var hasSelected = false;
            for (ushort a = 0; a < path.AnchorCount; a++)
                if (shape.GetAnchor((ushort)(path.AnchorStart + a)).IsSelected) { hasSelected = true; break; }
            if (hasSelected)
                pathsToDuplicate[pathCount++] = p;
        }

        if (pathCount == 0) return;

        shape.ClearAnchorSelection();
        var firstNewAnchor = shape.AnchorCount;

        for (var i = 0; i < pathCount; i++)
        {
            var srcPath = shape.GetPath(pathsToDuplicate[i]);
            var newPathIndex = shape.AddPath(
                fillColor: srcPath.FillColor,
                strokeColor: srcPath.StrokeColor,
                strokeWidth: srcPath.StrokeWidth,
                operation: srcPath.Operation);
            if (newPathIndex == ushort.MaxValue) break;

            for (ushort a = 0; a < srcPath.AnchorCount; a++)
            {
                var srcAnchor = shape.GetAnchor((ushort)(srcPath.AnchorStart + a));
                shape.AddAnchor(newPathIndex, srcAnchor.Position, srcAnchor.Curve);
            }
        }

        for (var i = firstNewAnchor; i < shape.AnchorCount; i++)
            shape.SetAnchorSelected((ushort)i, true);

        shape.UpdateSamples();
        shape.UpdateBounds();
        Document.IncrementVersion();
        Document.UpdateBounds();

        BeginMoveTool();
    }

    private void CopySelected()
    {
        var shape = CurrentShape;
        if (!shape.HasSelection()) return;

        var data = new PathClipboardData(shape);
        if (data.Paths.Length == 0) return;

        Clipboard.Copy(data);
    }

    private void PasteSelected()
    {
        var clipboardData = Clipboard.Get<PathClipboardData>();
        if (clipboardData == null) return;

        Undo.Record(Document);
        var shape = CurrentShape;
        shape.ClearSelection();
        clipboardData.PasteInto(shape);

        Document.IncrementVersion();
        Document.UpdateBounds();
        UpdateSelection();
    }

    #endregion

    #region Drawing

    private void DrawAllLayerWireframes()
    {
        var layers = Document.Layers;

        // Draw non-active layers dimmed
        for (int layerIdx = 0; layerIdx < layers.Count; layerIdx++)
        {
            if (layerIdx == Document.ActiveLayerIndex) continue;
            DrawSegments(layers[layerIdx].Shape, dimmed: true);
        }

        // Draw active layer on top
        var activeShape = CurrentShape;
        DrawSegments(activeShape, dimmed: false);
        DrawAnchors(activeShape);
    }

    private static void DrawSegments(Shape shape, bool dimmed)
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            var lineColor = dimmed
                ? EditorStyle.Workspace.LineColor.WithAlpha(0.3f)
                : EditorStyle.Workspace.LineColor;

            Gizmos.SetColor(lineColor);
            for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
            {
                if (!shape.IsSegmentSelected(anchorIndex))
                    DrawSegment(shape, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 1);
            }

            if (!dimmed)
            {
                Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
                for (ushort anchorIndex = 0; anchorIndex < shape.AnchorCount; anchorIndex++)
                {
                    if (shape.IsSegmentSelected(anchorIndex))
                        DrawSegment(shape, anchorIndex, EditorStyle.Shape.SegmentLineWidth, 2);
                }
            }
        }
    }

    private static void DrawSegment(Shape shape, ushort segmentIndex, float width, ushort order = 0)
    {
        var samples = shape.GetSegmentSamples(segmentIndex);
        ref readonly var anchor = ref shape.GetAnchor(segmentIndex);

        var prev = anchor.Position;
        foreach (var sample in samples)
        {
            Gizmos.DrawLine(prev, sample, width, order: order);
            prev = sample;
        }

        ref readonly var nextAnchor = ref shape.GetNextAnchor(segmentIndex);
        Gizmos.DrawLine(prev, nextAnchor.Position, width, order: order);
    }

    private static void DrawAnchors(Shape shape)
    {
        using var _ = Gizmos.PushState(EditorLayer.DocumentEditor);
        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (!anchor.IsSelected)
            {
                Gizmos.SetColor(EditorStyle.Workspace.LineColor);
                Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 4);
            }
        }

        for (ushort i = 0; i < shape.AnchorCount; i++)
        {
            ref readonly var anchor = ref shape.GetAnchor(i);
            if (anchor.IsSelected)
            {
                Gizmos.SetColor(EditorStyle.Workspace.SelectionColor);
                Gizmos.DrawRect(anchor.Position, EditorStyle.Shape.AnchorSize, order: 5);
            }
        }
    }

    private static ushort FindPathForAnchor(Shape shape, ushort anchorIndex)
    {
        for (ushort p = 0; p < shape.PathCount; p++)
        {
            var path = shape.GetPath(p);
            if (anchorIndex >= path.AnchorStart && anchorIndex < path.AnchorStart + path.AnchorCount)
                return p;
        }
        return ushort.MaxValue;
    }

    #endregion
}
