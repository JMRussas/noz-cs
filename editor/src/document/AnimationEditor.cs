//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal enum AnimationEditorState
{
    Default,
    Play
}

internal partial class AnimationEditor : DocumentEditor
{
    internal static readonly string[] FrameTimeStrings = ["0", "4", "8", "12", "16", "20", "24", "28", "32", "36", "40", "44", "48", "52", "56", "60"];

    internal struct DopeSheetFrame
    {
        public int Hold;
    }

    internal static bool DopeSheet(WidgetId baseId, ReadOnlySpan<DopeSheetFrame> frames, ref int currentFrame, int maxFrames, bool isPlaying)
    {
        int oldCurrentFrame = currentFrame;

        using var _ = UI.BeginColumn();

        UI.Container(EditorStyle.Dopesheet.LayerSeparator);

        using (UI.BeginRow(EditorStyle.Dopesheet.HeaderContainer))
        {
            UI.Container(EditorStyle.Dopesheet.FrameSeparator);
            var blockCount = maxFrames / 4;
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                using (UI.BeginContainer(EditorStyle.Dopesheet.TimeBlock))
                    UI.Text(FrameTimeStrings[blockIndex], EditorStyle.Dopesheet.TimeText);

                UI.Container(EditorStyle.Dopesheet.FrameSeparator);
            }
        }

        UI.Container(EditorStyle.Dopesheet.LayerSeparator);

        using (UI.BeginRow(EditorStyle.Dopesheet.FrameContainer))
        {
            UI.Container(EditorStyle.Dopesheet.FrameSeparator);

            var slotIndex = 0;
            var frameIndex = 0;
            for (; frameIndex < frames.Length; frameIndex++)
            {
                var selected = oldCurrentFrame == frameIndex;
                using (UI.BeginRow(baseId + frameIndex))
                {
                    if (UI.WasPressed())
                        currentFrame = frameIndex;

                    using (UI.BeginContainer(selected
                        ? EditorStyle.Dopesheet.SelectedFrame
                        : EditorStyle.Dopesheet.Frame))
                    {
                        UI.Container(selected
                            ? EditorStyle.Dopesheet.SelectedFrameDot
                            : EditorStyle.Dopesheet.FrameDot);

                    }

                    slotIndex++;

                    int hold = frames[frameIndex].Hold;
                    if (hold <= 0)
                    {
                        UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                        continue;
                    }

                    for (int holdIndex = 0; holdIndex < hold && slotIndex < maxFrames; holdIndex++, slotIndex++)
                    {
                        using (UI.BeginContainer(selected
                            ? EditorStyle.Dopesheet.SelectedFrame
                            : EditorStyle.Dopesheet.Frame))
                        {
                            //UI.Container(selected
                            //    ? EditorStyle.Dopesheet.SelectedFrameDot
                            //    : EditorStyle.Dopesheet.FrameDot);
                            //if (UI.WasPressed())
                            //    currentFrame = slotIndex;
                        }

                        if (holdIndex < hold - 1)
                        {
                            UI.Container(selected
                                ? EditorStyle.Dopesheet.SelectedHoldSeparator
                                : EditorStyle.Dopesheet.HoldSeparator);
                        }
                    }

                    UI.Container(EditorStyle.Dopesheet.FrameSeparator);
                }
            }

            for (; slotIndex < maxFrames; slotIndex++)
            {
                UI.Container(slotIndex % 4 == 0
                    ? EditorStyle.Dopesheet.FourthEmptyFrame
                    : EditorStyle.Dopesheet.EmptyFrame);

                UI.Container(EditorStyle.Dopesheet.FrameSeparator);
            }
        }

        UI.Container(EditorStyle.Dopesheet.LayerSeparator);

        return oldCurrentFrame != currentFrame;
    }

    private static partial class ElementId
    {
        public static partial WidgetId Root { get; }
        public static partial WidgetId OnionSkinButton { get; }
        public static partial WidgetId PlayButton { get; }
        public static partial WidgetId LoopButton { get; }
        public static partial WidgetId AddFrameButton { get; }
        public static partial WidgetId SkeletonButton { get; }
        public static partial WidgetId ShowSkeletonButton { get; }
        public static partial WidgetId MirrorButton { get; }
        public static partial WidgetId FirstFrame { get; }        
    }

    private AnimationEditorState _state = AnimationEditorState.Default;
    private bool _showSkeleton = true;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;
    private PopupMenuItem[] _contextMenuItems;
    private Vector2 _selectionCenter;
    private Vector2 _selectionCenterWorld;
    private bool _onionSkin;
    private float _playSpeed = 1f;

    public new AnimationDocument Document => (AnimationDocument)base.Document;

    public AnimationEditor(AnimationDocument document) : base(document)
    {
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.EndEdit, Key = InputCode.KeyTab };
        var copyCommand = new Command { Name = "Copy", Handler = CopyKeys, Key = InputCode.KeyC, Ctrl = true };
        var pasteCommand = new Command { Name = "Paste", Handler = PasteKeys, Key = InputCode.KeyV, Ctrl = true };

        Commands =
        [
            exitEditCommand,
            copyCommand,
            pasteCommand,
            new Command { Name = "Toggle Playback", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Move", Handler = BeginMoveTool, Key = InputCode.KeyG },
            new Command { Name = "Rotate", Handler = BeginRotateTool, Key = InputCode.KeyR },
            new Command { Name = "Reset Rotation", Handler = ResetRotation, Key = InputCode.KeyR, Shift = true },
            new Command { Name = "Reset Rotation", Handler = ResetRotation, Key = InputCode.KeyR, Alt = true },
            new Command { Name = "Reset Position", Handler = ResetPosition, Key = InputCode.KeyG, Shift = true },
            new Command { Name = "Reset Position", Handler = ResetPosition, Key = InputCode.KeyG, Alt = true },
            new Command { Name = "Select All", Handler = SelectAll, Key = InputCode.KeyA },
            new Command { Name = "Insert Frame Before", Handler = InsertFrameBefore, Key = InputCode.KeyI },
            new Command { Name = "Insert Frame After", Handler = InsertFrameAfter, Key = InputCode.KeyO },
            new Command { Name = "Insert Frame Lerp", Handler = InsertFrameAfterLerp, Key = InputCode.KeyO, Alt = true },
            new Command { Name = "Delete Frame", Handler = DeleteFrame, Key = InputCode.KeyX },
            new Command { Name = "Add Hold", Handler = AddHoldFrame, Key = InputCode.KeyH },
            new Command { Name = "Remove Hold", Handler = RemoveHoldFrame, Key = InputCode.KeyH, Ctrl = true },
            new Command { Name = "Toggle Onion Skin", Handler = ToggleOnionSkin, Key = InputCode.KeyO, Shift = true },
            new Command { Name = "Toggle Loop", Handler = ToggleLoop, Key = InputCode.KeyL },
            new Command { Name = "Increase Play Speed", Handler = IncreasePlaySpeed, Key = InputCode.KeyRight },
            new Command { Name = "Decrease Play Speed", Handler = DecreasePlaySpeed, Key = InputCode.KeyLeft },
            new Command { Name = "Mirror Pose", Handler = MirrorPose, Key = InputCode.KeyM },
        ];

        bool HasSelection() => Document.SelectedBoneCount > 0;

        _contextMenuItems = [
            //PopupMenuItem.FromCommand(copyCommand, enabled: HasSelection),
            //PopupMenuItem.FromCommand(pasteCommand, enabled: Clipboard.Is<AnimationFrameData>),
            //PopupMenuItem.Separator(),
            //PopupMenuItem.FromCommand(exitEditCommand),
        ];

        ClearSelection();
        Document.UpdateTransforms();
    }

    public override void Update()
    {
        Document.UpdateBounds();

        if (_state == AnimationEditorState.Default)
            UpdateDefaultState();
        else if (_state == AnimationEditorState.Play)
            UpdatePlayState();

        DrawEditor();
    }

    private void SkeletonPopupUI()
    {
        void Content()
        {
            for (int i = 0; i < DocumentManager.Count; i++)
            {
                var doc = DocumentManager.Get(i);
                if (doc.Def.Type != AssetType.Skeleton) continue;

                // TODO: migrate to UI.PopupMenu
                if (EditorUI.PopupItem(doc.Name, selected: doc as SkeletonDocument == Document.Skeleton))
                {
                    // TODO: migrate to UI.PopupMenu
                    EditorUI.ClosePopup();
                    Undo.Record(Document);
                    Document.IncrementVersion();
                    Document.SetSkeleton(doc as SkeletonDocument);
                }
            }
        }

        // TODO: migrate to UI.PopupMenu
        EditorUI.Popup(ElementId.SkeletonButton, Content);
    }

    private void SkeletonButtonUI()
    {
        UI.SetChecked(EditorUI.IsPopupOpen(ElementId.SkeletonButton));
        if (UI.Button(ElementId.SkeletonButton, () =>
        {
            UI.Image(EditorAssets.Sprites.IconBone, EditorStyle.Icon.Primary);
            if (Document.Skeleton == null)
                UI.Text("Select Skeleton...", EditorStyle.Control.Text);
            else
                UI.Text(Document.Skeleton.Name, EditorStyle.Control.Text);
        }, EditorStyle.Button.Secondary))
            // TODO: migrate to UI.PopupMenu
            EditorUI.TogglePopup(ElementId.SkeletonButton);

        SkeletonPopupUI();
    }

    private void TimelineUI()
    {
        using var _ = UI.BeginContainer(new ContainerStyle { Padding = EdgeInsets.LeftRight(2) });

        var isPlaying = _state == AnimationEditorState.Play;
        var currentFrame = Document.CurrentFrame;

        Span<DopeSheetFrame> frames = stackalloc DopeSheetFrame[Document.FrameCount];
        for (var i = 0; i < Document.FrameCount; i++)
            frames[i] = new DopeSheetFrame { Hold = Document.Frames[i].Hold };
        
        if (DopeSheet(ElementId.FirstFrame, frames, ref currentFrame, AnimationDocument.MaxFrames, isPlaying))
        {
            Document.CurrentFrame = currentFrame;
            Document.UpdateTransforms();
            SetDefaultState();
        }
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.Toolbar.Root);

        using (UI.BeginFlex())
        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
        {
            if (UI.Button(ElementId.AddFrameButton, EditorAssets.Sprites.IconKeyframe, EditorStyle.Button.IconOnly))
                InsertFrameAfter();
            if (UI.Button(ElementId.MirrorButton, EditorAssets.Sprites.IconMirror, EditorStyle.Button.IconOnly))
                MirrorPose();
        }

        UI.SetChecked(Document.IsPlaying);
        if (UI.Button(ElementId.PlayButton, EditorAssets.Sprites.IconPlay, EditorStyle.Button.ToggleIcon))
            TogglePlayback();

        using (UI.BeginFlex())
        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
        {
            UI.Flex();

            UI.SetChecked(_showSkeleton);
            if (UI.Button(ElementId.ShowSkeletonButton, EditorAssets.Sprites.IconPreview, EditorStyle.Button.ToggleIcon))
                _showSkeleton = !_showSkeleton;

            UI.SetChecked(Document.IsLooping);
            if (UI.Button(ElementId.LoopButton, EditorAssets.Sprites.IconLoop, EditorStyle.Button.ToggleIcon))
            {
                Undo.Record(Document);
                Document.IsLooping = !Document.IsLooping;
            }

            UI.SetChecked(_onionSkin);
            if (UI.Button(ElementId.OnionSkinButton, EditorAssets.Sprites.IconOnion, EditorStyle.Button.ToggleIcon))
                _onionSkin = !_onionSkin;
        }

        UI.Spacer(EditorStyle.Control.Spacing);
        SkeletonButtonUI();
    }

    public override void UpdateUI()
    {
        using (UI.BeginColumn(ElementId.Root, EditorStyle.DocumentEditor.Root))
        {
            ToolbarUI();
            TimelineUI();
        }
    }

    private void UpdateDefaultState()
    {
        if (Workspace.ActiveTool == null && Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            Workspace.BeginTool(new BoxSelectTool(HandleBoxSelect));
            return;
        }

        if (!_ignoreUp && !Workspace.IsDragging && Input.WasButtonReleased(InputCode.MouseLeft))
        {
            _clearSelectionOnUp = false;
            if (TrySelectBone())
                return;

            _clearSelectionOnUp = true;
        }

        _ignoreUp &= !Input.WasButtonReleased(InputCode.MouseLeft);

        if (Input.WasButtonReleased(InputCode.MouseLeft) && _clearSelectionOnUp && !Input.IsShiftDown())
            ClearSelection();
    }

    private void UpdatePlayState()
    {
        Document.UpdatePlayback(Time.DeltaTime * _playSpeed);

        if (!Document.IsPlaying)
        {
            Document.Play();
        }
    }

    private void HandleBoxSelect(Rect bounds)
    {
        if (!Input.IsShiftDown())
            ClearSelection();

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var baseTransform = GetBaseTransform();
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex] * baseTransform;
            var boneStart = Vector2.Transform(Vector2.Zero, boneTransform);
            var boneEnd = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            if (bounds.Contains(boneStart) || bounds.Contains(boneEnd))
                SetBoneSelected(boneIndex, true);
        }
    }

    private Matrix3x2 GetBaseTransform()
    {
        return Matrix3x2.CreateTranslation(Document.Position);
    }

    private bool TrySelectBone()
    {
        Span<int> bones = stackalloc int[Skeleton.MaxBones];
        var hitCount = Document.HitTestBones(GetBaseTransform(), Workspace.MouseWorldPosition, bones);
        if (hitCount == 0)
        {
            if (!Input.IsShiftDown())
                ClearSelection();
            return false;
        }

        var hitIndex = hitCount - 1;
        for (; hitIndex >= 0; hitIndex--)
        {
            if (IsBoneSelected(bones[hitIndex]))
            {
                hitIndex++;
                break;
            }
        }

        if (hitIndex < 0 || hitIndex >= hitCount)
            hitIndex = 0;

        if (!Input.IsShiftDown())
            ClearSelection();

        SetBoneSelected(bones[hitIndex], true);
        return true;
    }

    private bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].IsSelected;

    private bool IsAncestorSelected(int boneIndex)
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return false;

        var parentIndex = skeleton.Bones[boneIndex].ParentIndex;
        while (parentIndex >= 0)
        {
            if (IsBoneSelected(parentIndex))
                return true;
            parentIndex = skeleton.Bones[parentIndex].ParentIndex;
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

    private void ClearSelection()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            SetBoneSelected(boneIndex, false);
    }

    private void UpdateSelectionCenter()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var center = Vector2.Zero;
        var centerCount = 0f;
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            center += Vector2.Transform(Vector2.Zero, Document.LocalToWorld[boneIndex]);
            centerCount += 1f;
        }

        _selectionCenter = centerCount < 0.0001f ? center : center / centerCount;
        _selectionCenterWorld = Vector2.Transform(_selectionCenter, GetBaseTransform());
    }

    private void SaveState()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            Document.Bones[boneIndex].SavedTransform = Document.GetFrameTransform(boneIndex, Document.CurrentFrame);

        UpdateSelectionCenter();
    }

    private void RevertToSavedState()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            Document.GetFrameTransform(boneIndex, Document.CurrentFrame) = Document.Bones[boneIndex].SavedTransform;

        Document.UpdateTransforms();
        UpdateSelectionCenter();
    }

    private void SetDefaultState()
    {
        if (_state == AnimationEditorState.Default)
            return;

        Document.Stop();
        Document.UpdateTransforms();
        _state = AnimationEditorState.Default;
    }

    private void BeginMoveTool()
    {
        if (Document.SelectedBoneCount <= 0 || _state != AnimationEditorState.Default)
            return;

        SaveState();
        Undo.Record(Document);

        Workspace.BeginTool(new MoveTool(
            update: delta =>
            {
                UpdateMoveTool(delta);
            },
            commit: _ =>
            {
                Document.UpdateTransforms();
                Document.IncrementVersion();
            },
            cancel: () =>
            {
                Undo.Cancel();
                RevertToSavedState();
            }
        ));
    }

    private void UpdateMoveTool(Vector2 delta)
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex) || IsAncestorSelected(boneIndex))
                continue;

            ref var frame = ref Document.GetFrameTransform(boneIndex, Document.CurrentFrame);
            var bone = Document.Bones[boneIndex];
            var parentIndex = skeleton.Bones[boneIndex].ParentIndex;

            if (parentIndex == -1)
            {
                frame.Position = bone.SavedTransform.Position + new Vector2(delta.X, 0);
            }
            else
            {
                Matrix3x2.Invert(Document.LocalToWorld[parentIndex], out var invParent);
                var rotatedDelta = Vector2.TransformNormal(delta, invParent);
                frame.Position = bone.SavedTransform.Position + rotatedDelta;
            }

            if (Input.IsCtrlDown())
                frame.Position = Grid.SnapToGrid(frame.Position);

            if (parentIndex == -1)
                frame.Position.Y = 0.0f;
        }

        Document.UpdateTransforms();
    }

    private void BeginRotateTool()
    {
        if (Document.SelectedBoneCount <= 0 || _state != AnimationEditorState.Default)
            return;

        SaveState();
        UpdateSelectionCenter();
        Undo.Record(Document);

        var invTransform = Matrix3x2.Identity;
        Matrix3x2.Invert(GetBaseTransform(), out invTransform);
        var worldOrigin = Vector2.Transform(Vector2.Zero, GetBaseTransform());

        Workspace.BeginTool(new RotateTool(
            _selectionCenterWorld,
            _selectionCenter,
            worldOrigin,
            Vector2.Zero,
            invTransform,
            update: angle =>
            {
                UpdateRotateTool(angle * 180f / MathF.PI);
            },
            commit: _ =>
            {
                Document.UpdateTransforms();
                Document.IncrementVersion();
            },
            cancel: () =>
            {
                Undo.Cancel();
                RevertToSavedState();
            }
        ));
    }

    private void UpdateRotateTool(float angle)
    {
        if (MathF.Abs(angle) < 0.0001f)
            return;

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex) || IsAncestorSelected(boneIndex))
                continue;

            ref var frame = ref Document.GetFrameTransform(boneIndex, Document.CurrentFrame);
            frame.Rotation = Document.Bones[boneIndex].SavedTransform.Rotation + angle;
        }

        Document.UpdateTransforms();
    }

    private void ResetRotation()
    {
        if (_state != AnimationEditorState.Default)
            return;

        Undo.Record(Document);
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            Document.GetFrameTransform(boneIndex, Document.CurrentFrame).Rotation = 0;
        }

        Document.IncrementVersion();
        Document.UpdateTransforms();
    }

    private void ResetPosition()
    {
        if (_state != AnimationEditorState.Default)
            return;

        Undo.Record(Document);
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            Document.GetFrameTransform(boneIndex, Document.CurrentFrame).Position = Vector2.Zero;
        }

        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void SelectAll()
    {
        if (_state != AnimationEditorState.Default)
            return;

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            SetBoneSelected(boneIndex, true);
    }

    private void PreviousFrame()
    {
        SetDefaultState();
        Document.CurrentFrame = (Document.CurrentFrame - 1 + Document.FrameCount) % Document.FrameCount;
        Document.UpdateTransforms();
    }

    private void NextFrame()
    {
        SetDefaultState();
        Document.CurrentFrame = (Document.CurrentFrame + 1) % Document.FrameCount;
        Document.UpdateTransforms();
    }

    private void TogglePlayback()
    {
        if (_state == AnimationEditorState.Play)
        {
            SetDefaultState();
            return;
        }

        if (_state != AnimationEditorState.Default)
            return;

        Document.Play();
        _state = AnimationEditorState.Play;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.InsertFrame(Document.CurrentFrame);
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.InsertFrame(Document.CurrentFrame + 1);
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void InsertFrameAfterLerp()
    {
        Undo.Record(Document);
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var prevFrame = Document.CurrentFrame;
        var newFrame = Document.InsertFrame(Document.CurrentFrame + 1);
        var nextFrame = (newFrame + 1) % Document.FrameCount;

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            ref var newTransform = ref Document.GetFrameTransform(boneIndex, newFrame);
            ref var prevTransform = ref Document.GetFrameTransform(boneIndex, prevFrame);
            ref var nextTransform = ref Document.GetFrameTransform(boneIndex, nextFrame);

            newTransform.Position = Vector2.Lerp(prevTransform.Position, nextTransform.Position, 0.5f);
            newTransform.Rotation = prevTransform.Rotation + (nextTransform.Rotation - prevTransform.Rotation) * 0.5f;
            newTransform.Scale = Vector2.Lerp(prevTransform.Scale, nextTransform.Scale, 0.5f);
        }

        Document.CurrentFrame = newFrame;
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void DeleteFrame()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.DeleteFrame(Document.CurrentFrame);
        Document.UpdateTransforms();
        Document.IncrementVersion();
    }

    private void AddHoldFrame()
    {
        Undo.Record(Document);
        Document.Frames[Document.CurrentFrame].Hold++;
        Document.IncrementVersion();
    }

    private void RemoveHoldFrame()
    {
        if (Document.Frames[Document.CurrentFrame].Hold <= 0)
            return;

        Undo.Record(Document);
        Document.Frames[Document.CurrentFrame].Hold = Math.Max(0, Document.Frames[Document.CurrentFrame].Hold - 1);
        Document.IncrementVersion();
    }

    private void CopyKeys()
    {
        var frameData = new AnimationFrameData();
        for (var boneIndex = 0; boneIndex < Skeleton.MaxBones; boneIndex++)
            frameData.Transforms[boneIndex] = Document.Frames[Document.CurrentFrame].Transforms[boneIndex];
        Clipboard.Copy(frameData);
    }

    private void PasteKeys()
    {
        var frameData = Clipboard.Get<AnimationFrameData>();
        if (frameData == null)
            return;

        Undo.Record(Document);

        for (var boneIndex = 0; boneIndex < Skeleton.MaxBones; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            Document.Frames[Document.CurrentFrame].Transforms[boneIndex] = frameData.Transforms[boneIndex];
        }

        Document.IncrementVersion();
        Document.UpdateTransforms();
    }

    private void ToggleOnionSkin()
    {
        _onionSkin = !_onionSkin;
    }

    private void ToggleLoop()
    {
        Undo.Record(Document);
        Document.SetLooping(!Document.IsLooping);
        Document.IncrementVersion();
    }

    private void IncreasePlaySpeed()
    {
        _playSpeed = MathF.Min(_playSpeed + 0.1f, 4f);
    }

    private void DecreasePlaySpeed()
    {
        _playSpeed = MathF.Max(_playSpeed - 0.1f, 0.1f);
    }

    private void MirrorPose()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        Undo.Record(Document);

        var savedWorldTransforms = new Matrix3x2[Skeleton.MaxBones];
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            savedWorldTransforms[boneIndex] = Document.LocalToWorld[boneIndex];

        for (var boneIndex = 1; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;

            var mirrorIndex = skeleton.GetMirrorBone(boneIndex);
            if (mirrorIndex == -1)
                continue;

            var bone = skeleton.Bones[boneIndex];
            var desiredWorldPos = Vector2.Transform(Vector2.Zero, savedWorldTransforms[mirrorIndex]);
            var desiredWorldRot = GetRotation(savedWorldTransforms[mirrorIndex]);

            var parentWorld = bone.ParentIndex >= 0
                ? Document.LocalToWorld[bone.ParentIndex]
                : Matrix3x2.Identity;

            Matrix3x2.Invert(parentWorld, out var invParent);
            var localPos = Vector2.Transform(desiredWorldPos, invParent);
            var framePos = localPos - bone.Transform.Position;

            var parentWorldRot = GetRotation(parentWorld);
            var frameRot = desiredWorldRot - parentWorldRot - bone.Transform.Rotation;

            ref var frame = ref Document.GetFrameTransform(boneIndex, Document.CurrentFrame);
            frame.Position = framePos;
            frame.Rotation = frameRot;

            Document.UpdateTransforms();
        }

        Document.IncrementVersion();
    }

    private static float GetRotation(Matrix3x2 transform)
    {
        return MathF.Atan2(transform.M12, transform.M11) * 180f / MathF.PI;
    }

    private void DrawBones()
    {
        if (!_showSkeleton) return;
        if (Document.Skeleton == null) return; 

        for (var boneIndex = 0; boneIndex < Document.Skeleton.BoneCount; boneIndex++)
        {
            var bone = Document.Skeleton.Bones[boneIndex];
            var animationBone = Document.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Graphics.SetSortGroup((ushort)(animationBone.IsSelected ? 2 : 1));
            Gizmos.DrawBone(p0, p1, selected: animationBone.IsSelected);
        }
    }

    private void DrawEditor()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var baseTransform = GetBaseTransform();

        using (Gizmos.PushState(EditorLayer.Document))
        {
            Graphics.SetTransform(baseTransform);
            Document.DrawOrigin();
            DrawOnionSkin();
            DrawBones();
            Graphics.SetSortGroup(0);
            Document.DrawSprites();
        }
    }

    private void DrawDopeSheet()
    {
    }

    private void DrawOnionSkin()
    {
        if (!_onionSkin || Document.FrameCount <= 1)
            return;

        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var prevFrame = (Document.CurrentFrame - 1 + Document.FrameCount) % Document.FrameCount;
        var nextFrame = (Document.CurrentFrame + 1) % Document.FrameCount;
        var prevColor = new Color(1f, 0.3f, 0.3f, 0.3f);
        var nextColor = new Color(0.3f, 1f, 0.3f, 0.3f);

        DrawOnionSkinFrame(skeleton, prevFrame, prevColor);
        DrawOnionSkinFrame(skeleton, nextFrame, nextColor);

        Document.UpdateTransforms();
    }

    private void DrawOnionSkinFrame(SkeletonDocument skeleton, int frame, Color tint)
    {
        Document.UpdateTransforms(frame);

        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Grid);

            for (var i = 0; i < skeleton.Sprites.Count; i++)
            {
                var sprite = skeleton.Sprites[i];
                sprite.DrawSprite(skeleton.WorldToLocal, Document.LocalToWorld, Document.Transform, tint: tint);
            }
        }
    }

    public override void OnUndoRedo()
    {
        Document.UpdateSkeleton();
        Document.UpdateTransforms();
    }
}
