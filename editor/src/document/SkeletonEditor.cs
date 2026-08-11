//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class SkeletonEditor : DocumentEditor
{
    private const int SortGroupSkin = 0;
    private const int SortGroupBones = 1;
    private const int SortGroupSelectedBones = 2;

    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId PreviewButton { get; }
        public static partial WidgetId ConnectedButton { get; }
        public static partial WidgetId BoneColor { get; }
        public static partial WidgetId StatesAdd { get; }
        public static partial WidgetId StateRow { get; }
        public static partial WidgetId StateName { get; }
        public static partial WidgetId StateValue { get; }
        public static partial WidgetId StateDelete { get; }
    }

    private struct SavedBone
    {
        public Vector2 HeadWorld;
        public Vector2 TailWorld;
        public float Length;
        public float Radius;
    }

    public new SkeletonDocument Document => (SkeletonDocument)base.Document;

    private readonly SavedBone[] _savedBones = new SavedBone[Skeleton.MaxBones];
    //private PopupMenuItem[] _contextMenuItems = null!;
    private bool _showPreview = true;

    private readonly Command[] _commands;

    public SkeletonEditor(SkeletonDocument document) : base(document)
    {
        var exitEditCommand     = new Command("Exit Edit Mode",    Workspace.EndEdit,    [InputCode.KeyTab]);
        var deleteCommand       = new Command("Delete",            HandleDelete,         [InputCode.KeyDelete, InputCode.KeyX], icon:EditorAssets.Sprites.IconDelete);
        var renameCommand       = new Command("Rename",            HandleRename,         [InputCode.KeyF2]);
        var selectAllCommand    = new Command("Select All",        OnSelectAll,          [new(InputCode.KeyA, ctrl:true)]);

        _commands =
        [
            exitEditCommand,
            deleteCommand,
            renameCommand,
            selectAllCommand,
            new Command("Create Bone", BeginCreateBone, [InputCode.KeyV])
        ];

        Commands = _commands;
        ClearSelection();
        SetMode(new BoneMode());
    }

    public override void Dispose()
    {        
        ClearSelection();
        base.Dispose();
    }

    public override void OnUndoRedo()
    {
        base.OnUndoRedo();
    }

    public override void Update()
    {
        DrawSkeleton();

        if (_showPreview)
            using (Graphics.PushState())
            {
                Graphics.SetSortGroup(SortGroupSkin);
                Document.DrawSprites();
            }

        DrawBoneNames();
        Mode?.Draw();
    }

    public override void LateUpdate()
    {
        Mode?.Update();
    }


    private void ToggleConnected()
    {
        Undo.Record(Document);

        Document.CurrentConnected = !Document.CurrentConnected;

        for (var i = 0; i < Document.BoneCount; i++)
        {
            var bone = Document.Bones[i];
            if (!bone.IsHeadSelected || bone.ParentIndex < 0)
                continue;

            bone.IsConnected = Document.CurrentConnected;

            if (Document.CurrentConnected)
            {
                // Snap head to parent's tail - tail stays in place
                var parent = Document.Bones[bone.ParentIndex];
                bone.HeadWorld = parent.TailWorld;
            }
        }

        Document.IncrementVersion();
    }

    public override bool ShowInspector => true;

    public override void InspectorUI()
    {
        InspectorBoneSection();
        InspectorStatesSection();
    }

    private void InspectorBoneSection()
    {
        var selectedBone = -1;
        for (var i = 0; i < Document.BoneCount; i++)
        {
            if (Document.Bones[i].IsSelected)
            {
                selectedBone = i;
                break;
            }
        }

        if (selectedBone < 0) return;

        var bone = Document.Bones[selectedBone];

        using (Inspector.BeginSection("Bone"))
        {
            if (Inspector.IsSectionCollapsed) return;

            using (Inspector.BeginProperty("Name"))
                UI.Text(bone.Name);

            using (Inspector.BeginProperty("Color"))
            {
                var color = EditorUI.ColorButton(ElementId.BoneColor, bone.Color);
                if (UI.WasChangeStarted()) Undo.Record(Document);
                if (UI.WasChanged()) bone.Color = color;
                if (UI.WasChangeCancelled()) Undo.Cancel();
            }
        }
    }

    private void InspectorStatesSection()
    {
        using (Inspector.BeginSection("States", content: StatesAddButton))
        {
            if (Inspector.IsSectionCollapsed) return;

            for (var i = 0; i < Document.StateCount; i++)
                DrawStateRow(i);
        }
    }

    private void StatesAddButton()
    {
        ElementTree.BeginAlign(Align.Min, Align.Center);
        if (UI.Button(ElementId.StatesAdd, EditorAssets.Sprites.IconAdd, EditorStyle.Inspector.SectionButton))
            AddState();
        ElementTree.EndAlign();
    }

    private void DrawStateRow(int i)
    {
        var state = Document.States[i];
        var rowId = ElementId.StateRow + i;

        using (UI.BeginRow(rowId, EditorStyle.Inspector.Row))
        {
            var hovered = UI.IsHovered(rowId);

            using (UI.BeginFlex(0.5f))
            {
                var newName = UI.TextInput(ElementId.StateName + i, state.Name, EditorStyle.Inspector.TextBox);
                if (UI.WasChangeStarted()) Undo.Record(Document);
                if (UI.WasChanged() && newName != state.Name)
                {
                    var oldName = state.Name;
                    state.Name = newName;
                    Document.NotifyStateRenamed(i, oldName, newName);
                    Document.IncrementVersion();
                }
                if (UI.WasChangeCancelled()) Undo.Cancel();
            }

            using (UI.BeginFlex(0.5f))
            {
                var valText = state.InitialValue.ToString();
                var newText = UI.TextInput(ElementId.StateValue + i, valText, EditorStyle.Inspector.TextBox, "0");
                if (UI.WasChangeStarted()) Undo.Record(Document);
                if (UI.WasChanged() && newText != valText && int.TryParse(newText, out var parsed))
                {
                    state.InitialValue = parsed;
                    Document.IncrementVersion();
                }
                if (UI.WasChangeCancelled()) Undo.Cancel();
            }

            if (hovered)
            {
                if (UI.Button(ElementId.StateDelete + i, EditorAssets.Sprites.IconDelete, EditorStyle.Inspector.SectionButton))
                    RemoveState(i);
            }
            else
                UI.Spacer(EditorStyle.Icon.Size);
        }
    }

    private void AddState()
    {
        if (Document.StateCount >= Skeleton.MaxStates) return;

        Undo.Record(Document);
        var idx = Document.StateCount++;
        var s = Document.States[idx];
        s.Index = idx;
        s.Name = Document.GetUniqueStateName();
        s.InitialValue = 0;
        Document.NotifyStateAdded(idx);
        Document.IncrementVersion();
    }

    private void RemoveState(int idx)
    {
        if (idx < 0 || idx >= Document.StateCount) return;

        Undo.Record(Document);
        var name = Document.States[idx].Name;
        Document.RemoveState(idx);
        Document.NotifyStateRemoved(idx, name);
        Document.IncrementVersion();
    }

    public override void UpdateOverlayUI()
    {
        using (FloatingToolbar.Begin())
        {
            if (FloatingToolbar.Button(ElementId.ConnectedButton, EditorAssets.Sprites.IconConnected, isSelected: Document.CurrentConnected))
                ToggleConnected();

            FloatingToolbar.Divider();

            if (FloatingToolbar.Button(ElementId.PreviewButton, EditorAssets.Sprites.IconPreview, isSelected: _showPreview))
                _showPreview = !_showPreview;
        }

        // Mode UI uses screen-space positions, so draw it at the root overlay level
        // instead of relative to the scene viewport's layout origin.
        Mode?.DrawUI();
    }

    private bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].IsSelected;

    internal void SelectHeadJoint(int boneIndex, bool selected)
    {
        var bone = Document.Bones[boneIndex];
        if (bone.IsHeadSelected == selected)
            return;

        bone.IsHeadSelected = selected;
        Document.SelectedHeadCount += selected ? 1 : -1;
    }

    internal void SelectTailJoint(int boneIndex, bool selected)
    {
        var bone = Document.Bones[boneIndex];
        if (bone.IsTailSelected == selected)
            return;

        bone.IsTailSelected = selected;
        Document.SelectedTailCount += selected ? 1 : -1;
    }

    internal void SelectBone(int boneIndex, bool selected)
    {
        SelectHeadJoint(boneIndex, selected);
        SelectTailJoint(boneIndex, selected);
    }

    private int GetFirstSelectedBoneIndex()
    {
        for (var i = 0; i < Document.BoneCount; i++)
            if (IsBoneSelected(i))
                return i;
        return -1;
    }

    internal void ClearSelection()
    {
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            Document.Bones[boneIndex].IsHeadSelected = false;
            Document.Bones[boneIndex].IsTailSelected = false;
        }
        Document.SelectedHeadCount = 0;
        Document.SelectedTailCount = 0;
    }

    internal void UpdateConnectedToggleState()
    {
        for (var i = Document.BoneCount - 1; i >= 0; i--)
        {
            var bone = Document.Bones[i];
            if (bone.IsHeadSelected && bone.ParentIndex >= 0)
            {
                Document.CurrentConnected = bone.IsConnected;
                return;
            }
        }
    }

    private bool HasSelectedHeadWithParent()
    {
        for (var i = 0; i < Document.BoneCount; i++)
        {
            var bone = Document.Bones[i];
            if (bone.IsHeadSelected && bone.ParentIndex >= 0)
                return true;
        }
        return false;
    }

    internal void SaveState()
    {
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            var src = Document.Bones[boneIndex];
            ref var saved = ref _savedBones[boneIndex];
            saved.HeadWorld = src.HeadWorld;
            saved.TailWorld = src.TailWorld;
            saved.Length = (src.TailWorld - src.HeadWorld).Length();
            saved.Radius = src.Radius;
        }
    }


    internal void HandleBoxSelect(Rect bounds)
    {
        if (!Input.IsShiftDown(InputScope.All))
            ClearSelection();

        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            var b = Document.Bones[boneIndex];
            var headPos = b.HeadWorld + Document.Position;
            var tailPos = b.TailWorld + Document.Position;

            if (bounds.Contains(headPos))
                SelectHeadJoint(boneIndex, true);

            if (bounds.Contains(tailPos))
                SelectTailJoint(boneIndex, true);
        }

        UpdateConnectedToggleState();
    }


    private void DrawBoneNames()
    {
        if (!Workspace.ShowNames)
            return;

        var renamingBoneIndex = (Mode is BoneMode bm) ? bm.RenamingBoneIndex : -1;

        TextRender.SetOutline(EditorStyle.Workspace.NameOutlineColor, EditorStyle.Workspace.NameOutline, 0.2f);

        using (Graphics.PushState())
        {
            var font = EditorAssets.Fonts.Seguisb;
            Graphics.SetLayer(EditorLayer.Names);

            var fontSize = EditorStyle.Workspace.NameSize * Gizmos.ZoomRefScale;

            for (var i = 0; i < Document.BoneCount; i++)
            {
                if (i == renamingBoneIndex) continue;

                ref var b = ref Document.Bones[i];
                b.NamePosition = (b.HeadWorld + b.TailWorld) * 0.5f + Document.Position;
                var textSize = TextRender.Measure(b.Name, font, fontSize);
                var textOffset = new Vector2(b.NamePosition.X - textSize.X * 0.5f, b.NamePosition.Y - textSize.Y * 0.5f);
                Graphics.SetTransform(Matrix3x2.CreateTranslation(textOffset));
                Graphics.SetColor(b.IsSelected ? EditorStyle.Workspace.SelectionColor: EditorStyle.Workspace.NameColor);
                TextRender.Draw(b.Name, font, fontSize, order: (ushort)(b.IsSelected ? 1 : 0));
            }
        }

        TextRender.ClearOutline();
    }

    private void OnSelectAll()
    {
        for (var i = 0; i < Document.BoneCount; i++)
            SelectBone(i, true);
    }

    #region Move

    internal void ApplyMoveDelta(Vector2 delta)
    {
        for (var i = 0; i < Document.BoneCount; i++)
        {
            var bone = Document.Bones[i];
            if (!bone.IsSelected)
                continue;

            var snappedDelta = delta;
            if (Input.IsCtrlDown(InputScope.All))
            {
                if (bone.IsHeadSelected)
                    snappedDelta = Grid.SnapToPixelGrid(_savedBones[i].HeadWorld + delta) - _savedBones[i].HeadWorld;
                else if (bone.IsTailSelected)
                    snappedDelta = Grid.SnapToPixelGrid(_savedBones[i].TailWorld + delta) - _savedBones[i].TailWorld;
            }

            if (bone.IsFullySelected)
            {
                bone.HeadWorld = _savedBones[i].HeadWorld + snappedDelta;
                bone.TailWorld = _savedBones[i].TailWorld + snappedDelta;
            }
            else if (bone.IsHeadSelected)
            {
                bone.HeadWorld = _savedBones[i].HeadWorld + snappedDelta;
            }
            else if (bone.IsTailSelected)
            {
                bone.TailWorld = _savedBones[i].TailWorld + snappedDelta;
            }
        }

        EnforceConnectedConstraints();
    }

    private void EnforceConnectedConstraints()
    {
        const float minLength = 0.05f;

        bool changed;
        do
        {
            changed = false;
            for (var i = 0; i < Document.BoneCount; i++)
            {
                var bone = Document.Bones[i];
                if (!bone.IsConnected || bone.ParentIndex < 0)
                    continue;

                var parent = Document.Bones[bone.ParentIndex];
                if (bone.HeadWorld != parent.TailWorld)
                {
                    if (bone.IsHeadSelected)
                    {
                        // Child head is being dragged - move parent tail to match
                        var parentDir = bone.HeadWorld - parent.HeadWorld;
                        var parentLen = parentDir.Length();

                        if (parentLen < minLength)
                        {
                            var dir = parentLen > 0.0001f
                                ? parentDir / parentLen
                                : new Vector2(1, 0);
                            bone.HeadWorld = parent.HeadWorld + dir * minLength;
                        }

                        parent.TailWorld = bone.HeadWorld;
                    }
                    else
                    {
                        bone.HeadWorld = parent.TailWorld;
                    }
                    changed = true;
                }
            }
        } while (changed);
    }

    #endregion

    #region Extrude Tool

    private void BeginCreateBone()
    {
        if (Document.SelectedBoneCount != 1)
            return;

        if (Document.BoneCount >= Skeleton.MaxBones)
            return;

        var parentBoneIndex = GetFirstSelectedBoneIndex();
        if (parentBoneIndex == -1)
            return;

        var parentBone = Document.Bones[parentBoneIndex];

        Undo.Record(Document);

        var newBone = Document.Bones[Document.BoneCount];
        newBone.Name = Document.GetUniqueBoneName();
        newBone.Index = Document.BoneCount;
        newBone.ParentIndex = parentBoneIndex;
        newBone.Transform = BoneTransform.Identity;
        newBone.Length = parentBone.Length;

        // Set world points - new bone starts at parent's head, extending in same direction
        var parentDir = parentBone.TailWorld - parentBone.HeadWorld;
        var parentLen = parentDir.Length();
        var dir = parentLen > 0.0001f ? parentDir / parentLen : new Vector2(1, 0);
        newBone.HeadWorld = parentBone.HeadWorld;
        newBone.TailWorld = parentBone.HeadWorld + dir * newBone.Length;

        Document.BoneCount++;

        Document.NotifyBoneAdded(Document.BoneCount - 1);
        Document.UpdateTransforms();
        ClearSelection();
        SelectBone(Document.BoneCount - 1, true);

        if (Mode is BoneMode boneMode)
        {
            boneMode.SetIgnoreUp();
            boneMode.BeginMoveDrag(recordUndo: false);
        }
    }

    #endregion

    #region Remove

    private void HandleDelete()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        // Find the lowest index being deleted to know which sprites need recording
        var lowestDeletedIndex = int.MaxValue;
        for (var i = 0; i < Document.BoneCount; i++)
        {
            if (IsBoneSelected(i) && i < lowestDeletedIndex)
                lowestDeletedIndex = i;
        }

        Undo.BeginGroup();
        foreach (var doc in Project.Documents.OfType<SpriteDocument>())
            if (doc.Skeleton.Value == Document)
                Undo.Record(doc);

        Undo.Record(Document);

        for (var i = Document.BoneCount - 1; i >= 0; i--)
        {
            if (!IsBoneSelected(i)) continue;
            var boneName = Document.Bones[i].Name;
            Document.RemoveBone(i);
            Document.NotifyBoneRemoved(i, boneName);
        }

        // Rebuild transforms from world points after deletion
        Document.BuildTransformsFromWorldPoints();

        Undo.EndGroup();

        ClearSelection();
        Document.IncrementVersion();
    }

    #endregion

    #region Rename

    private void HandleRename()
    {
        var boneIndex = GetFirstSelectedBoneIndex();
        if (boneIndex == -1 || Document.SelectedBoneCount != 1)
            return;

        if (Mode is BoneMode boneMode)
            boneMode.BeginRename(boneIndex, Document.Bones[boneIndex].Name);
    }

    #endregion

    private void DrawSkeleton()
    {
        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(Document.Transform);

            for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
            {
                ref readonly var b = ref Document.Bones[boneIndex];
                var lineSelected = b.IsHeadSelected && b.IsTailSelected;
                var headPos = b.HeadWorld;
                var tailPos = b.TailWorld;

                if (b.ParentIndex >= 0 && !b.IsConnected)
                {
                    ref readonly var p = ref Document.Bones[b.ParentIndex];
                    Graphics.SetSortGroup(SortGroupBones);
                    Gizmos.SetColor(EditorStyle.Skeleton.ParentLineColor);
                    Gizmos.DrawDashedLine(b.HeadWorld, p.TailWorld);
                }

                Graphics.SetSortGroup(lineSelected ? SortGroupSelectedBones : SortGroupBones);
                Gizmos.DrawBone(headPos, tailPos, selected: lineSelected);
                Graphics.SetSortGroup(b.IsHeadSelected ? SortGroupSelectedBones : SortGroupBones);
                Gizmos.DrawJoint(headPos, b.IsHeadSelected);
                Graphics.SetSortGroup(b.IsTailSelected ? SortGroupSelectedBones : SortGroupBones);
                Gizmos.DrawJoint(tailPos, b.IsTailSelected);

                if (b.Radius > 0)
                {
                    Graphics.SetSortGroup(SortGroupBones);
                    Gizmos.SetColor(EditorStyle.Skeleton.EnvelopeColor);
                    Gizmos.DrawEnvelope(headPos, tailPos, b.Radius, b.Radius);
                }
            }
        }
    }
}
