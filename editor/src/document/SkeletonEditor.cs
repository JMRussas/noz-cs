//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal class SkeletonEditor : DocumentEditor
{
    private const int RootId = 1;
    private const int PreviewButtonId = 2;

    private const int SortGroupSkin = 0;
    private const int SortGroupBones = 1;
    private const int SortGroupSelectedBones = 2;

    private struct SavedBone
    {
        public BoneTransform Transform;
        public Matrix3x2 LocalToWorld;
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
        var moveCommand = new Command { Name = "Move", Handler = HanleMove, Key = InputCode.KeyG, Icon = EditorAssets.Sprites.IconMove };
        var rotateCommand = new Command { Name = "Rotate", Handler = HandleRotate, Key = InputCode.KeyR };
        var scaleCommand = new Command { Name = "Scale", Handler = HandleScale, Key = InputCode.KeyS };
        var renameCommand = new Command { Name = "Rename", Handler = HandleRename, Key = InputCode.KeyF2 };

        _commands =
        [
            exitEditCommand,
            moveCommand,
            deleteCommand,
            scaleCommand,
            rotateCommand,
            renameCommand,
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
                PopupMenuItem.FromCommand(rotateCommand, enabled: HasSelection),
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
        Document.UpdateTransforms();
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
            if (EditorUI.Button(PreviewButtonId, EditorAssets.Sprites.IconPreview, toolbar: true))
                _showPreview = !_showPreview;
        }

        UI.Flex();
    }

    public override void UpdateUI()
    {
        using (UI.BeginCanvas(id: EditorStyle.CanvasId.DocumentEditor))
        using (UI.BeginColumn(RootId, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();
            UI.Spacer(EditorStyle.Control.Spacing);
        }
    }

    private bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].IsSelected;

    private bool IsAncestorSelected(int boneIndex)
    {
        var parentIndex = Document.Bones[boneIndex].ParentIndex;
        while (parentIndex >= 0)
        {
            if (Document.Bones[parentIndex].IsSelected)
                return true;
            parentIndex = Document.Bones[parentIndex].ParentIndex;
        }
        return false;
    }

    private void SetBoneSelected(int boneIndex, bool selected)
    {
        if (IsBoneSelected(boneIndex) == selected)
            return;

        Document.Bones[boneIndex].IsSelected = selected;
        Document.SelectedBoneCount += selected ? 1 : -1;
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
            SetBoneSelected(boneIndex, false);
    }

    private void UpdateSelectionCenter()
    {
        var center = Vector2.Zero;
        var centerCount = 0f;

        for (var i = 0; i < Document.BoneCount; i++)
        {
            if (!IsBoneSelected(i))
                continue;

            center += Vector2.Transform(Vector2.Zero, Document.LocalToWorld[i]);
            centerCount += 1f;
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
            saved.Transform = src.Transform;
            saved.Length = src.Length;
            saved.LocalToWorld = Document.LocalToWorld[boneIndex];
        }
    }

    private bool TrySelect()
    {
        var cycle = !Input.IsShiftDown();
        var boneIndex = Document.HitTestBone(Workspace.MouseWorldPosition, cycle);
        if (boneIndex == -1)
            return false;

        if (Input.IsShiftDown())
            SetBoneSelected(boneIndex, !Document.Bones[boneIndex].IsSelected);
        else
        {
            ClearSelection();
            SetBoneSelected(boneIndex, true);
        }

        return true;
    }

    private void HandleBoxSelect(Rect bounds)
    {
        if (!Input.IsShiftDown())
            ClearSelection();

        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            var b = Document.Bones[boneIndex];
            ref readonly var m = ref Document.LocalToWorld[boneIndex];
            var boneStart = Vector2.Transform(Vector2.Zero, m) + Document.Position;
            var boneEnd = Vector2.Transform(new Vector2(b.Length, 0), m) + Document.Position;
            if (bounds.Contains(boneStart) || bounds.Contains(boneEnd))
                SetBoneSelected(boneIndex, true);
        }
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
                b.NamePosition = Vector2.Transform(new Vector2(b.Length * 0.5f, 0), Document.LocalToWorld[i]) + Document.Position;
                var textSize = TextRender.Measure(b.Name, font, fontSize);
                var textOffset = new Vector2(b.NamePosition.X - textSize.X * 0.5f, b.NamePosition.Y - textSize.Y * 0.5f);
                Graphics.SetTransform(Matrix3x2.CreateTranslation(textOffset));
                Graphics.SetColor(b.IsSelected ? EditorStyle.Workspace.SelectionColor: EditorStyle.Workspace.NameColor);
                TextRender.Draw(b.Name, font, fontSize, order: (ushort)(b.IsSelected ? 1 : 0));
            }
        }

        TextRender.ClearOutline();
    }

    private void CounterActParentTransform(int parentIndex)
    {
        var parent = Document.Bones[parentIndex];
        ref readonly var parentWorldToLocal = ref Document.WorldToLocal[parentIndex];

        for (var childIndex = 0; childIndex < Document.BoneCount; childIndex++)
        {
            var child = Document.Bones[childIndex];
            if (child.ParentIndex != parentIndex || IsBoneSelected(childIndex))
                continue;

            var savedChild = _savedBones[childIndex];
            var newLocal = savedChild.LocalToWorld * parentWorldToLocal;

            child.Transform.Position = new Vector2(newLocal.M31, newLocal.M32);
            child.Transform.Rotation = GetRotation(newLocal);
        }
    }

    private static float GetRotation(Matrix3x2 matrix)
    {
        var scaleX = MathF.Sqrt(matrix.M11 * matrix.M11 + matrix.M12 * matrix.M12);
        return MathF.Atan2(matrix.M12 / scaleX, matrix.M11 / scaleX) * 180f / MathF.PI;
    }

    #region Move Tool

    private void HanleMove()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        SaveState();
        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: UpdateMoveTool,
            commit: _ => { Document.MarkModified(); Document.NotifyTransformsChanged(); },
            cancel: Undo.Cancel
        ));
    }

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
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex) || IsAncestorSelected(boneIndex))
                continue;

            var b = Document.Bones[boneIndex];
            var p = boneIndex > 0
                ? Document.Bones[b.ParentIndex]
                : Document.Bones[0];
            var sb = _savedBones[boneIndex];

            b.Transform.Position = Vector2.Transform(
                Vector2.Transform(Vector2.Zero, sb.LocalToWorld) + delta,
                Document.WorldToLocal[int.Max(b.ParentIndex, 0)]);

            if (Input.IsCtrlDown(InputScope.All))
                b.Transform.Position = Grid.SnapToPixelGrid(b.Transform.Position);
        }

        Document.UpdateTransforms();

        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            CounterActParentTransform(boneIndex);
        }

        Document.UpdateTransforms();
    }

    #endregion

    #region Rotate Tool

    private void HandleRotate()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        UpdateSelectionCenter();
        SaveState();
        Undo.Record(Document);

        Matrix3x2.Invert(Document.Transform, out var invTransform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, Document.Transform);

        Workspace.BeginTool(new RotateTool(
            _selectionCenterWorld,
            _selectionCenter,
            worldOrigin,
            Vector2.Zero,
            invTransform,
            update: UpdateRotateTool,
            commit: _ => { Document.MarkModified(); Document.NotifyTransformsChanged(); },
            cancel: Undo.Cancel
        ));
    }

    private void UpdateRotateTool(float angle)
    {
        var angleDeg = angle * 180f / MathF.PI;

        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            var b = Document.Bones[boneIndex];
            var sb = _savedBones[boneIndex];

            if (Input.IsCtrlDown())
                b.Transform.Rotation = SnapAngle(sb.Transform.Rotation + angleDeg);
            else
                b.Transform.Rotation = sb.Transform.Rotation + angleDeg;
        }

        Document.UpdateTransforms();

        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            CounterActParentTransform(boneIndex);
        }

        Document.UpdateTransforms();
    }

    private static float SnapAngle(float angle)
    {
        const float snapIncrement = 15f;
        return MathF.Round(angle / snapIncrement) * snapIncrement;
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
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            var b = Document.Bones[boneIndex];
            var sb = _savedBones[boneIndex];
            b.Length = Math.Clamp(sb.Length * scale.X, 0.05f, 10.0f);
        }

        Document.UpdateTransforms();
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
        Document.BoneCount++;

        Document.NotifyBoneAdded(Document.BoneCount - 1);
        Document.UpdateTransforms();
        ClearSelection();
        SetBoneSelected(Document.BoneCount - 1, true);

        _ignoreUp = true;
        BeginMoveTool(false);
    }

    #endregion

    #region Remove

    private void HandleDelete()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        Undo.Record(Document);

        for (var i = Document.BoneCount - 1; i >= 0; i--)
        {
            if (!IsBoneSelected(i)) continue;
            var boneName = Document.Bones[i].Name;
            Document.RemoveBone(i);
            Document.NotifyBoneRemoved(i, boneName);
        }

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
            Graphics.SetSortGroup(SortGroupBones);
            Graphics.SetTransform(Document.Transform);

            for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
            {
                var b = Document.Bones[boneIndex];
                var selected = b.IsSelected;
                var boneColor = selected
                    ? EditorStyle.Skeleton.SelectedBoneColor
                    : EditorStyle.Skeleton.BoneColor;

                ref readonly var boneLocalToWorld = ref Document.LocalToWorld[boneIndex];
                var p0 = Vector2.Transform(Vector2.Zero, boneLocalToWorld);
                var p1 = Vector2.Transform(new Vector2(b.Length, 0), boneLocalToWorld);

                if (b.ParentIndex >= 0)
                {
                    var parentTransform = Document.GetParentLocalToWorld(b, boneLocalToWorld);
                    var pp = Vector2.Transform(Vector2.Zero, parentTransform);
                    Graphics.SetSortGroup(SortGroupBones);
                    Gizmos.SetColor(EditorStyle.Skeleton.ParentLineColor);
                    Gizmos.DrawDashedLine(pp, p0, order: 1);
                }

                Graphics.SetSortGroup(selected ? SortGroupSelectedBones : SortGroupBones);
                Gizmos.DrawBone(p0, p1, boneColor, order: 1);
            }
        }
    }
}
