//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal class SkeletonEditor : DocumentEditor
{
    public new SkeletonDocument Document => (SkeletonDocument)base.Document;

    private readonly BoneData[] _savedBones = new BoneData[SkeletonDocument.MaxBones];
    private Vector2 _selectionCenter;
    private Vector2 _selectionCenterWorld;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;

    private readonly Command[] _commands;

    public SkeletonEditor(SkeletonDocument document) : base(document)
    {
        for (var i = 0; i < SkeletonDocument.MaxBones; i++)
            _savedBones[i] = new BoneData();

        _commands =
        [
            new Command { Name = "Move", ShortName = "move", Handler = BeginMoveTool, Key = InputCode.KeyG },
            new Command { Name = "Rotate", ShortName = "rotate", Handler = BeginRotateTool, Key = InputCode.KeyR },
            new Command { Name = "Scale Length", ShortName = "scale", Handler = BeginScaleTool, Key = InputCode.KeyS },
            new Command { Name = "Parent", ShortName = "parent", Handler = BeginParentTool, Key = InputCode.KeyP },
            new Command { Name = "Unparent", ShortName = "unparent", Handler = BeginUnparentTool, Key = InputCode.KeyP, Ctrl = true },
            new Command { Name = "Extrude", ShortName = "extrude", Handler = BeginExtrudeTool, Key = InputCode.KeyE, Ctrl = true },
            new Command { Name = "Delete", ShortName = "delete", Handler = HandleRemove, Key = InputCode.KeyX },
            new Command { Name = "Rename", ShortName = "rename", Handler = BeginRenameCommand, Key = InputCode.KeyF2 },
            new Command { Name = "Reset Rotation", ShortName = "resetrot", Handler = ResetRotation, Key = InputCode.KeyR, Alt = true },
            new Command { Name = "Reset Translation", ShortName = "resettrans", Handler = ResetTranslation, Key = InputCode.KeyG, Alt = true },
        ];

        ClearSelection();
    }

    public override Command[]? GetCommands() => _commands;

    public override void OnUndoRedo()
    {
        Document.UpdateTransforms();
    }

    public override void Update()
    {
        UpdateDefaultState();
        DrawSkeleton();
        UpdateBoneNames();
    }

    private bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].Selected;

    private bool IsAncestorSelected(int boneIndex)
    {
        var parentIndex = Document.Bones[boneIndex].ParentIndex;
        while (parentIndex >= 0)
        {
            if (Document.Bones[parentIndex].Selected)
                return true;
            parentIndex = Document.Bones[parentIndex].ParentIndex;
        }
        return false;
    }

    private void SetBoneSelected(int boneIndex, bool selected)
    {
        if (IsBoneSelected(boneIndex) == selected)
            return;

        Document.Bones[boneIndex].Selected = selected;
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

            var bone = Document.Bones[i];
            center += Vector2.Transform(Vector2.Zero, bone.LocalToWorld);
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
            var dst = _savedBones[boneIndex];
            dst.Name = src.Name;
            dst.Index = src.Index;
            dst.ParentIndex = src.ParentIndex;
            dst.Transform = src.Transform;
            dst.LocalToWorld = src.LocalToWorld;
            dst.WorldToLocal = src.WorldToLocal;
            dst.Length = src.Length;
            dst.Selected = src.Selected;
        }
    }

    private void RevertToSavedState()
    {
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            var src = _savedBones[boneIndex];
            var dst = Document.Bones[boneIndex];
            dst.Name = src.Name;
            dst.Index = src.Index;
            dst.ParentIndex = src.ParentIndex;
            dst.Transform = src.Transform;
            dst.LocalToWorld = src.LocalToWorld;
            dst.WorldToLocal = src.WorldToLocal;
            dst.Length = src.Length;
            dst.Selected = src.Selected;
        }

        Document.UpdateTransforms();
        UpdateSelectionCenter();
    }

    private bool TrySelect()
    {
        var boneIndex = Document.HitTestBone(Workspace.MouseWorldPosition);
        if (boneIndex == -1)
            return false;

        if (Input.IsShiftDown())
            SetBoneSelected(boneIndex, !Document.Bones[boneIndex].Selected);
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
            var boneStart = Vector2.Transform(Vector2.Zero, b.LocalToWorld) + Document.Position;
            var boneEnd = Vector2.Transform(new Vector2(b.Length, 0), b.LocalToWorld) + Document.Position;

            if (bounds.Contains(boneStart) || bounds.Contains(boneEnd))
                SetBoneSelected(boneIndex, true);
        }
    }

    private void UpdateDefaultState()
    {
        if (Workspace.ActiveTool == null && Workspace.DragStarted)
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

    private void UpdateBoneNames()
    {
        if (!Workspace.ShowNames)
            return;

        using (Graphics.PushState())
        {
            var font = EditorAssets.Fonts.Seguisb;
            Graphics.SetLayer(EditorLayer.Names);

            var scale = 1f / Workspace.Zoom;
            var fontSize = EditorStyle.Workspace.NameSize * scale;

            for (var i = 0; i < Document.BoneCount; i++)
            {
                var b = Document.Bones[i];
                var p = Vector2.Transform(new Vector2(b.Length * 0.5f, 0), b.LocalToWorld) + Document.Position;

                var textSize = TextRender.Measure(b.Name, font, fontSize);
                var textX = p.X - textSize.X * 0.5f;
                var textY = p.Y;

                Graphics.SetTransform(Matrix3x2.CreateTranslation(textX, textY));
                Graphics.SetColor(b.Selected ? EditorStyle.SelectionColor : EditorStyle.TextColor);
                TextRender.Draw(b.Name, font, fontSize);
            }
        }
    }

    private void CounterActParentTransform(int parentIndex)
    {
        var parent = Document.Bones[parentIndex];

        for (var childIndex = 0; childIndex < Document.BoneCount; childIndex++)
        {
            var child = Document.Bones[childIndex];
            if (child.ParentIndex != parentIndex || IsBoneSelected(childIndex))
                continue;

            var savedChild = _savedBones[childIndex];
            var newLocal = savedChild.LocalToWorld * parent.WorldToLocal;

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

    private void BeginMoveTool()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        SaveState();
        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: UpdateMoveTool,
            commit: _ => Document.MarkModified(),
            cancel: CancelTool
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
            commit: _ => Document.MarkModified(),
            cancel: CancelTool
        ));
    }

    private void UpdateMoveTool(Vector2 delta)
    {
        for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex) || IsAncestorSelected(boneIndex))
                continue;

            var b = Document.Bones[boneIndex];
            var p = boneIndex > 0 ? Document.Bones[b.ParentIndex] : Document.Bones[0];
            var sb = _savedBones[boneIndex];

            var worldPos = Vector2.Transform(Vector2.Zero, sb.LocalToWorld) + delta;
            b.Transform.Position = Vector2.Transform(worldPos, p.WorldToLocal);
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

    private void BeginRotateTool()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        UpdateSelectionCenter();
        SaveState();
        Undo.Record(Document);

        Matrix3x2.Invert(Document.Transform, out var invTransform);

        Workspace.BeginTool(new RotateTool(
            _selectionCenterWorld,
            _selectionCenter,
            invTransform,
            update: UpdateRotateTool,
            commit: _ => Document.MarkModified(),
            cancel: CancelTool
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

    private void BeginScaleTool()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        UpdateSelectionCenter();
        SaveState();
        Undo.Record(Document);

        Workspace.BeginTool(new ScaleTool(
            _selectionCenterWorld,
            update: UpdateScaleTool,
            commit: _ => Document.MarkModified(),
            cancel: CancelTool
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

    #region Parent Tool

    private void BeginParentTool()
    {
        if (Document.SelectedBoneCount != 1)
            return;

        Workspace.BeginTool(new SelectTool(CommitParentTool));
    }

    private void CommitParentTool(Vector2 position)
    {
        var boneIndex = Document.HitTestBone(position);
        if (boneIndex == -1)
            return;

        Undo.BeginGroup();
        Undo.Record(Document);

        var selectedBone = GetFirstSelectedBoneIndex();
        boneIndex = Document.ReparentBone(selectedBone, boneIndex);

        ClearSelection();
        SetBoneSelected(boneIndex, true);

        Undo.EndGroup();
        Document.MarkModified();
    }

    private void BeginUnparentTool()
    {
        var selectedBone = GetFirstSelectedBoneIndex();
        if (selectedBone <= 0)
            return;

        var bone = Document.Bones[selectedBone];
        if (bone.ParentIndex <= 0)
            return;

        Undo.BeginGroup();
        Undo.Record(Document);

        var parentParent = Document.Bones[bone.ParentIndex].ParentIndex;
        var newIndex = Document.ReparentBone(selectedBone, parentParent);

        ClearSelection();
        SetBoneSelected(newIndex, true);

        Undo.EndGroup();
        Document.MarkModified();
    }

    #endregion

    #region Extrude Tool

    private void BeginExtrudeTool()
    {
        if (Document.SelectedBoneCount != 1)
            return;

        if (Document.BoneCount >= SkeletonDocument.MaxBones)
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

        Document.UpdateTransforms();
        ClearSelection();
        SetBoneSelected(Document.BoneCount - 1, true);

        _ignoreUp = true;
        BeginMoveTool(false);
    }

    #endregion

    #region Remove

    private void HandleRemove()
    {
        if (Document.SelectedBoneCount <= 0)
            return;

        Undo.BeginGroup();
        Undo.Record(Document);

        for (var i = Document.BoneCount - 1; i >= 0; i--)
        {
            if (!IsBoneSelected(i))
                continue;

            Document.RemoveBone(i);
        }

        Undo.EndGroup();
        ClearSelection();
        Document.MarkModified();
    }

    #endregion

    #region Rename

    private void BeginRenameCommand()
    {
        var boneIndex = GetFirstSelectedBoneIndex();
        if (boneIndex == -1)
            return;

        if (Document.SelectedBoneCount != 1)
        {
            Notifications.Add("Can only rename a single selected bone");
            return;
        }

        // TODO: Implement proper text input dialog for bone rename
        // For now, show the current bone name
        Notifications.Add($"Selected bone: {Document.Bones[boneIndex].Name}");
    }

    #endregion

    #region Reset

    private void ResetRotation()
    {
        Undo.Record(Document);

        for (var boneIndex = 1; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            Document.Bones[boneIndex].Transform.Rotation = 0;
        }

        Document.UpdateTransforms();
        Document.MarkModified();
    }

    private void ResetTranslation()
    {
        Undo.Record(Document);

        if (IsBoneSelected(0))
            Document.Bones[0].Transform.Position = Vector2.Zero;

        for (var boneIndex = 1; boneIndex < Document.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            var b = Document.Bones[boneIndex];
            var p = Document.Bones[b.ParentIndex];
            b.Transform.Position = new Vector2(p.Length, 0);
        }

        Document.UpdateTransforms();
        Document.MarkModified();
    }

    #endregion

    private void CancelTool()
    {
        Undo.Cancel();
        RevertToSavedState();
    }

    private void DrawSkeleton()
    {
        using (Gizmos.PushState(EditorLayer.Gizmo))
        {
            Graphics.SetTransform(Document.Transform);

            var lineWidth = EditorStyle.SkeletonBoneWidth * Gizmos.ZoomRefScale;
            var originSize = EditorStyle.SkeletonBoneRadius * Gizmos.ZoomRefScale;
            var dashLength = EditorStyle.SkeletonParentDash * Gizmos.ZoomRefScale;

            for (var boneIndex = 0; boneIndex < Document.BoneCount; boneIndex++)
            {
                var b = Document.Bones[boneIndex];
                var selected = b.Selected;
                var boneColor = selected ? EditorStyle.SelectionColor : EditorStyle.SkeletonBoneColor;

                var p0 = Vector2.Transform(Vector2.Zero, b.LocalToWorld);
                var p1 = Vector2.Transform(new Vector2(b.Length, 0), b.LocalToWorld);

                if (b.ParentIndex >= 0)
                {
                    var parentTransform = Document.GetParentLocalToWorld(b, b.LocalToWorld);
                    var pp = Vector2.Transform(Vector2.Zero, parentTransform);
                    Gizmos.DrawDashedLine(pp, p0, lineWidth, dashLength, boneColor);
                }

                Gizmos.DrawBone(p0, p1, lineWidth, boneColor);
                Gizmos.SetColor(boneColor);
                Gizmos.DrawCircle(p0, originSize);
            }
        }
    }
}

internal class SelectTool : Tool
{
    private readonly Action<Vector2> _commit;

    public SelectTool(Action<Vector2> commit)
    {
        _commit = commit;
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        if (Input.WasButtonPressed(InputCode.MouseLeft))
        {
            _commit(Workspace.MouseWorldPosition);
            Input.ConsumeButton(InputCode.MouseLeft);
            Workspace.EndTool();
        }
    }

    public override void Draw()
    {
        using (Gizmos.PushState(EditorLayer.Tool))
        {
            Graphics.SetColor(EditorStyle.SelectionColor.WithAlpha(0.5f));
            Gizmos.DrawRect(Workspace.MouseWorldPosition, EditorStyle.Shape.AnchorSize * 2f);
        }
    }
}
