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

    [ElementId("Root")]
    [ElementId("PreviewButton")]
    [ElementId("ConnectedButton")]
    private static partial class ElementId { }

    private struct SavedBone
    {
        public Vector2 HeadWorld;
        public Vector2 TailWorld;
        public float Length;
    }

    public new SkeletonDocument Document => (SkeletonDocument)base.Document;

    private readonly SavedBone[] _savedBones = new SavedBone[Skeleton.MaxBones];
    private Vector2 _selectionCenter;
    private Vector2 _selectionCenterWorld;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;
    private bool _showPreview = true;

    private readonly Command[] _commands;

    public SkeletonEditor(SkeletonDocument document) : base(document)
    {
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab };
        var deleteCommand = new Command { Name = "Delete", Handler = HandleDelete, Key = InputCode.KeyX, Icon = EditorAssets.Sprites.IconDelete };
        var moveCommand = new Command { Name = "Move", Handler = OnMoveCommand, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var scaleCommand = new Command { Name = "Scale", Handler = HandleScale, Key = InputCode.KeyS };
        var renameCommand = new Command { Name = "Rename", Handler = HandleRename, Key = InputCode.KeyF2 };
        var selectAllCommand = new Command { Name = "Select All", Handler = OnSelectAll, Key = InputCode.KeyA };

        _commands =
        [
            exitEditCommand,
            moveCommand,
            deleteCommand,
            scaleCommand,
            renameCommand,
            selectAllCommand,
            new Command { Name = "Create Bone", Handler = BeginCreateBone, Key = InputCode.KeyV },
        ];

        bool HasSelection() => Document.SelectedBoneCount > 0;

        ContextMenu = new PopupMenuDef
        {
            Title = "Skeleton",
            Items =
            [
                PopupMenuItem.FromCommand(renameCommand, enabled: () => Document.SelectedBoneCount == 1),
                PopupMenuItem.FromCommand(deleteCommand, enabled: HasSelection),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(moveCommand, enabled: HasSelection),
                PopupMenuItem.FromCommand(scaleCommand, enabled: HasSelection),
                PopupMenuItem.Separator(),
                PopupMenuItem.FromCommand(exitEditCommand),
            ]
        };

        Commands = _commands;
        ClearSelection();
    }

    public override void Dispose()
    {        
        ClearSelection();
        base.Dispose();
    }

    public override void OnUndoRedo()
    {
    }

    public override void Update()
    {
        UpdateDefaultState();
        DrawSkeleton();

        if (_showPreview)
            using (Graphics.PushState())
            {
                Graphics.SetSortGroup(SortGroupSkin);
                Document.DrawSprites();
            }            

        DrawBoneNames();
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.Toolbar.Root);

        UI.Flex();

        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
        {
            var hasSelectableHead = HasSelectedHeadWithParent();
            if (EditorUI.Button(
                ElementId.ConnectedButton,
                EditorAssets.Sprites.IconConnected,
                selected: Document.CurrentConnected,
                disabled: !hasSelectableHead,
                toolbar: true))
            {
                ToggleConnected();
            }

            EditorUI.ToolbarSpacer();

            if (EditorUI.Button(ElementId.PreviewButton, EditorAssets.Sprites.IconPreview, toolbar: true))
                _showPreview = !_showPreview;
        }

        UI.Flex();
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

        Document.MarkModified();
    }

    public override void UpdateUI()
    {
        using (UI.BeginColumn(ElementId.Root, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();
            UI.Spacer(EditorStyle.Control.Spacing);
        }
    }

    private bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].IsSelected;

    private void SelectHeadJoint(int boneIndex, bool selected)
    {
        var bone = Document.Bones[boneIndex];
        if (bone.IsHeadSelected == selected)
            return;

        bone.IsHeadSelected = selected;
        Document.SelectedHeadCount += selected ? 1 : -1;
    }

    private void SelectTailJoint(int boneIndex, bool selected)
    {
        var bone = Document.Bones[boneIndex];
        if (bone.IsTailSelected == selected)
            return;

        bone.IsTailSelected = selected;
        Document.SelectedTailCount += selected ? 1 : -1;
    }

    private void SelectBone(int boneIndex, bool selected)
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

    private void ClearSelection()
    {
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            Document.Bones[boneIndex].IsHeadSelected = false;
            Document.Bones[boneIndex].IsTailSelected = false;
        }
        Document.SelectedHeadCount = 0;
        Document.SelectedTailCount = 0;
    }

    private void UpdateConnectedToggleState()
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

    private void UpdateSelectionCenter()
    {
        var center = Vector2.Zero;
        var centerCount = 0f;

        for (var i = 0; i < Document.BoneCount; i++)
        {
            var bone = Document.Bones[i];

            if (bone.IsHeadSelected)
            {
                center += bone.HeadWorld;
                centerCount += 1f;
            }

            if (bone.IsTailSelected)
            {
                center += bone.TailWorld;
                centerCount += 1f;
            }
        }

        _selectionCenter = centerCount < float.Epsilon ? center : center / centerCount;
        _selectionCenterWorld = _selectionCenter + Document.Position;
    }

    private void SaveState()
    {
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            var src = Document.Bones[boneIndex];
            ref var saved = ref _savedBones[boneIndex];
            saved.HeadWorld = src.HeadWorld;
            saved.TailWorld = src.TailWorld;
            saved.Length = (src.TailWorld - src.HeadWorld).Length();
        }
    }

    private bool TrySelect()
    {
        var shiftDown = Input.IsShiftDown(InputScope.All);
        var hit = Document.HitTestJoints(Workspace.MouseWorldPosition, cycle: !shiftDown);
        if (hit.BoneIndex == -1)
            return false;

        if (!shiftDown)
            ClearSelection();

        var bone = Document.Bones[hit.BoneIndex];

        switch (hit.HitType)
        {
            case BoneHitType.Head:
                if (shiftDown)
                    SelectHeadJoint(hit.BoneIndex, !bone.IsHeadSelected);
                else
                    SelectHeadJoint(hit.BoneIndex, true);
                break;

            case BoneHitType.Tail:
                if (shiftDown)
                    SelectTailJoint(hit.BoneIndex, !bone.IsTailSelected);
                else
                    SelectTailJoint(hit.BoneIndex, true);
                break;

            case BoneHitType.Bone:
                if (shiftDown)
                    SelectBone(hit.BoneIndex, !bone.IsFullySelected);
                else
                    SelectBone(hit.BoneIndex, true);
                break;
        }

        UpdateConnectedToggleState();
        return true;
    }

    private void HandleBoxSelect(Rect bounds)
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

    private void UpdateDefaultState()
    {
        if (Workspace.ActiveTool == null && Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            Workspace.BeginTool(new BoxSelectTool(HandleBoxSelect));
            return;
        }

        if (!_ignoreUp && !Workspace.WasDragging && Input.WasButtonReleased(InputCode.MouseLeft))
        {
            _clearSelectionOnUp = false;

            if (TrySelect())
                return;

            _clearSelectionOnUp = true;
        }

        _ignoreUp = _ignoreUp && !Input.WasButtonReleased(InputCode.MouseLeft);

        if (Input.WasButtonReleased(InputCode.MouseLeft) && _clearSelectionOnUp)
            ClearSelection();
    }

    private void DrawBoneNames()
    {
        if (!Workspace.ShowNames)
            return;

        var renamingBoneIndex = -1;
        if (Workspace.ActiveTool is RenameTool && Document.SelectedBoneCount == 1)
            renamingBoneIndex = GetFirstSelectedBoneIndex();

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

    #region Move Tool

    private void OnMoveCommand() => BeginMoveTool(true);

    private void BeginMoveTool(bool recordUndo)
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        SaveState();
        if (recordUndo)
            Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: UpdateMoveTool,
            commit: _ => { Document.MarkModified(); Document.NotifyTransformsChanged(); },
            cancel: Undo.Cancel
        ));
    }

    private void UpdateMoveTool(Vector2 delta)
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

    #region Scale Tool

    private void HandleScale()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        UpdateSelectionCenter();
        SaveState();
        Undo.Record(Document);

        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);

        Workspace.BeginTool(new ScaleTool(
            _selectionCenterWorld,
            worldOrigin,
            update: UpdateScaleTool,
            commit: _ => { Document.MarkModified(); Document.NotifyTransformsChanged(); },
            cancel: Undo.Cancel
        ));
    }

    private void UpdateScaleTool(Vector2 scale)
    {
        var pivot = Input.IsShiftDown(InputScope.All) ? Vector2.Zero : _selectionCenter;

        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            var bone = Document.Bones[boneIndex];
            if (!bone.IsSelected)
                continue;

            var sb = _savedBones[boneIndex];

            if (bone.IsHeadSelected)
            {
                var offset = sb.HeadWorld - pivot;
                bone.HeadWorld = pivot + offset * scale;
            }

            if (bone.IsTailSelected)
            {
                var offset = sb.TailWorld - pivot;
                bone.TailWorld = pivot + offset * scale;
            }
        }

        EnforceConnectedConstraints();
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

        _ignoreUp = true;
        BeginMoveTool(false);
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
        foreach (var doc in DocumentManager.Documents.OfType<SpriteDocument>())
            if (doc.Binding.Skeleton == Document)
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
        Document.MarkModified();
    }

    #endregion

    #region Rename

    private void HandleRename()
    {
        var boneIndex = GetFirstSelectedBoneIndex();
        if (boneIndex == -1 || Document.SelectedBoneCount != 1)
            return;

        var bone = Document.Bones[boneIndex];
        var oldName = bone.Name;

        Workspace.BeginTool(new RenameTool(
            bone.Name,
            () => Document.Bones[boneIndex].NamePosition,
            newName =>
            {
                if (newName == oldName)
                    return;

                Undo.Record(Document);
                bone.Name = newName;
                Document.NotifyBoneRenamed(boneIndex, oldName, newName);
                Document.MarkModified();
                Notifications.Add($"renamed bone to '{newName}'");
            }
        ));
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
            }
        }
    }
}
