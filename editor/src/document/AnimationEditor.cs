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

internal class AnimationEditor : DocumentEditor
{
    private static readonly string[] FrameTimeStrings = ["0", "4", "8", "12", "16", "20", "24", "28", "32", "36", "40", "44", "48", "52", "56", "60"];

    private const int OnionSkinButtonId = 1;
    private const int PlayButtonId = 2;
    private const int RootMotionButtonId = 3;
    private const int LoopButtonId = 4;
    private const int AddFrameButtonId = 5;
    private const int SkeletonButtonId = 6;
    private const int SkeletonPopupId = 7;
    private const int FirstFrameId = 64;
    private const int FirstPopupItemId = 128;

    private AnimationEditorState _state = AnimationEditorState.Default;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;
    private bool _showSkeletonPopup;
    private Vector2 _selectionCenter;
    private Vector2 _selectionCenterWorld;
    private bool _onionSkin;
    private Vector2 _rootMotionDelta;
    private bool _rootMotion = true;
    private float _playSpeed = 1f;

    public new AnimationDocument Document => (AnimationDocument)base.Document;

    public AnimationEditor(AnimationDocument document) : base(document)
    {
        var exitEditCommand = new Command { Name = "Exit Edit Mode", Handler = Workspace.ToggleEdit, Key = InputCode.KeyTab };
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
            new Command { Name = "Remove Hold", Handler = RemoveHoldFrame, Key = InputCode.KeyH, Shift = true },
            new Command { Name = "Toggle Onion Skin", Handler = ToggleOnionSkin, Key = InputCode.KeyO, Shift = true },
            new Command { Name = "Toggle Root Motion", Handler = ToggleRootMotion, Key = InputCode.KeyM, Shift = true },
            new Command { Name = "Toggle Loop", Handler = ToggleLoop, Key = InputCode.KeyL },
            new Command { Name = "Increase Play Speed", Handler = IncreasePlaySpeed, Key = InputCode.KeyRight },
            new Command { Name = "Decrease Play Speed", Handler = DecreasePlaySpeed, Key = InputCode.KeyLeft },
            new Command { Name = "Mirror Pose", Handler = MirrorPose, Key = InputCode.KeyM },
        ];

        bool HasSelection() => Document.SelectedBoneCount > 0;

        ContextMenu = new ContextMenuDef
        {
            Title = "Animation",
            Items =
            [
                ContextMenuItem.FromCommand(copyCommand, enabled: HasSelection),
                ContextMenuItem.FromCommand(pasteCommand, enabled: Clipboard.Is<AnimationFrameData>),
                ContextMenuItem.Separator(),
                ContextMenuItem.FromCommand(exitEditCommand),
            ]
        };

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
        if (!_showSkeletonPopup) return;

        var anchorRect = UI.GetElementRect(EditorStyle.CanvasId.DocumentEditor, SkeletonButtonId);
        using var _ = UI.BeginPopup(SkeletonPopupId, EditorStyle.AnimationEditor.SkeletonPopup with { AnchorRect = anchorRect });
        if (UI.IsClosed())
        {
            _showSkeletonPopup = false;
            return;
        }

        using var __ = UI.BeginColumn(EditorStyle.Popup.Root with { Size = Size.Fit });

        for (int i=0; i<DocumentManager.Count; i++)
        {
            var doc = DocumentManager.Get(i);
            if (doc.Def.Type != AssetType.Skeleton) continue;

            if (EditorUI.PopupItem(FirstPopupItemId + i, doc.Name, selected: doc as SkeletonDocument == Document.Skeleton))
            {
                _showSkeletonPopup = false;
                Undo.Record(Document);
                Document.MarkModified();
                Document.SetSkeleton(doc as SkeletonDocument);
            }
        }
    }

    private void SkeletonButtonUI()
    {
        void ButtonContent()
        {
            EditorUI.ControlIcon(EditorAssets.Sprites.IconBone);
            if (Document.Skeleton == null)
                EditorUI.ControlPlaceholderText("Select Skeleton...");
            else
                EditorUI.ControlPlaceholderText(Document.Skeleton.Name);
        }

        using (UI.BeginContainer(ContainerStyle.Fit))
        {
            if (EditorUI.Control(SkeletonButtonId, ButtonContent, false, false, false))
                _showSkeletonPopup = true;

            SkeletonPopupUI();
        }
    }

    private void TimelineUI(int currentFrame, bool isPlaying)
    {
        using (UI.BeginRow(EditorStyle.AnimationEditor.TickContainer))
        {
            UI.Spacer(EditorStyle.AnimationEditor.FrameSpacerWidth);
            var blockCount = AnimationDocument.MaxFrames / 4;
            for (var blockIndex = 0; blockIndex < blockCount; blockIndex++)
            {
                using (UI.BeginContainer(EditorStyle.AnimationEditor.FrameBlock))
                    UI.Label(FrameTimeStrings[blockIndex], EditorStyle.AnimationEditor.FrameBlockText);

                UI.Container(EditorStyle.AnimationEditor.FrameBlockSeparator);
            }
        }

        using (UI.BeginContainer(EditorStyle.AnimationEditor.FrameContainer))
        using (UI.BeginRow())
        {
            UI.Container(EditorStyle.AnimationEditor.FrameSeparator);

            for (var frameIndex = 0; frameIndex < AnimationDocument.MaxFrames; frameIndex++)
            {
                if (frameIndex < Document.FrameCount)
                {
                    var selected = Document.CurrentFrame == frameIndex;
                    using (UI.BeginContainer(
                        FirstFrameId + frameIndex,
                        selected
                            ? EditorStyle.AnimationEditor.SelectedFrame
                            : EditorStyle.AnimationEditor.Frame))
                    {
                        UI.Container(selected
                            ? EditorStyle.AnimationEditor.SelectedFrameDot
                            : EditorStyle.AnimationEditor.FrameDot);

                        if (UI.WasPressed())
                        {
                            Document.CurrentFrame = frameIndex;
                            Document.UpdateTransforms();
                            SetDefaultState();
                        }
                    }
                }
                else
                {
                    using (UI.BeginContainer(frameIndex % 4 == 0
                        ? EditorStyle.AnimationEditor.FourthFrame
                        : EditorStyle.AnimationEditor.EmptyFrame))
                    {

                    }
                }

                UI.Container(EditorStyle.AnimationEditor.FrameSeparator);
            }
        }

        UI.Container(EditorStyle.AnimationEditor.LayerSeparator);
#if false

        using (UI.BeginRow())
        {
            //var lastRealFrameIndex = -1;
            for (var frameIndex = 0; frameIndex <= AnimationDocument.MaxFrames; frameIndex++)
            {
                using (UI.BeginContainer(EditorStyle.AnimationEditor.TickContainer))
                {

                }

                var isLastTick = frameIndex == AnimationDocument.MaxFrames;
                var tickStyle = isLastTick
                    ? EditorStyle.AnimationEditor.Tick with { Width = EditorStyle.AnimationEditor.BorderWidth }
                    : EditorStyle.AnimationEditor.Tick;

                using (UI.BeginContainer(tickStyle))
                {
                    var realFrameIndex = Document.GetRealFrameIndex(frameIndex);

                    if (UI.WasPressed() && realFrameIndex < Document.FrameCount)
                    {
                        Document.CurrentFrame = realFrameIndex;
                        Document.UpdateTransforms();
                        SetDefaultState();
                    }

                    if (UI.IsHovered())
                        UI.Container(ContainerStyle.Default with { Color = EditorStyle.AnimationEditor.TickHoverColor });

                    // Event indicator
                    if (realFrameIndex < Document.FrameCount &&
                        realFrameIndex != lastRealFrameIndex &&
                        !string.IsNullOrEmpty(Document.Frames[realFrameIndex].EventName))
                    {
                        using (UI.BeginContainer(ContainerStyle.Default with { AlignX = Align.Center, AlignY = Align.Center }))
                        {
                            var eventColor = realFrameIndex == currentFrame ? Color.White : EditorStyle.AnimationEditor.EventColor;
                            UI.Container(ContainerStyle.Default with
                            {
                                Width = EditorStyle.AnimationEditor.FrameDotSize * 2,
                                Height = EditorStyle.AnimationEditor.FrameDotSize * 2,
                                Color = eventColor
                            });
                        }
                    }

                    lastRealFrameIndex = realFrameIndex;

                    // Tick mark
                    if (frameIndex % 4 == 0 || (isPlaying && frameIndex == currentFrame))
                    {
                        var tickColor = isPlaying && frameIndex == currentFrame ? Color.White : EditorStyle.AnimationEditor.TickColor;
                        UI.Container(EditorStyle.AnimationEditor.TickBorder with { Color = tickColor });
                    }
                    else
                    {
                        UI.Container(EditorStyle.AnimationEditor.ShortTick);
                    }
                }
    }
}
#endif
    }

    private static void OtherUI()
    {
#if false

            // Ticks row


            UI.Container(EditorStyle.AnimationEditor.HorizontalBorder);

            // Frames row
            using (UI.BeginRow())
            {
                var frameIndexWithHolds = 0;

                for (var frameIndex = 0; frameIndex < Document.FrameCount; frameIndex++)
                {
                    var frame = Document.Frames[frameIndex];
                    var frameWidth = EditorStyle.AnimationEditor.FrameWidth + EditorStyle.AnimationEditor.FrameWidth * frame.Hold;
                    var isSelected = frameIndex == currentFrame;

                    var frameStyle = isSelected
                        ? EditorStyle.AnimationEditor.SelectedFrame with { Width = frameWidth }
                        : EditorStyle.AnimationEditor.Frame with { Width = frameWidth };

                    using (UI.BeginContainer(frameStyle))
                    {
                        if (UI.IsHovered())
                            UI.Container(ContainerStyle.Default with { Color = EditorStyle.AnimationEditor.TickHoverColor });

                        if (UI.WasPressed())
                        {
                            Document.CurrentFrame = frameIndex;
                            Document.UpdateTransforms();
                            SetDefaultState();
                        }

                        UI.Container(EditorStyle.AnimationEditor.FrameBorder);
                        UI.Container(EditorStyle.AnimationEditor.FrameDot);
                    }

                    frameIndexWithHolds += 1 + frame.Hold;
                }

                // Empty frames
                for (; frameIndexWithHolds < frameCount; frameIndexWithHolds++)
                {
                    using (UI.BeginContainer(EditorStyle.AnimationEditor.EmptyFrame))
                    {
                        UI.Container(EditorStyle.AnimationEditor.FrameBorder);
                    }
                }

                // Right border
                UI.Container(EditorStyle.AnimationEditor.FrameBorder);
            }

            UI.Container(EditorStyle.AnimationEditor.HorizontalBorder);
                //UI.Spacer(EditorStyle.AnimationEditor.ButtonMarginY);
#endif
    }

    private void ToolbarUI()
    {
        using var _ = UI.BeginRow(EditorStyle.Toolbar.Root);

        using (UI.BeginFlex())
        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
        {
            if (EditorUI.Button(AddFrameButtonId, EditorAssets.Sprites.IconKeyframe, toolbar: true))
                InsertFrameAfter();
        }

        if (EditorUI.Button(PlayButtonId, EditorAssets.Sprites.IconPlay, selected: Document.IsPlaying, toolbar: true))
            TogglePlayback();

        using (UI.BeginFlex())
        using (UI.BeginRow(new ContainerStyle { Spacing = EditorStyle.Control.Spacing }))
        {
            UI.Flex();

            if (EditorUI.Button(LoopButtonId, EditorAssets.Sprites.IconLoop, selected: Document.IsLooping, toolbar: true))
            {
                Undo.Record(Document);
                Document.MarkMetaModified();
                Document.IsLooping = !Document.IsLooping;
            }

            if (EditorUI.Button(RootMotionButtonId, EditorAssets.Sprites.IconRootMotion, selected: Document.IsRootMotion, toolbar: true))
            {
                Undo.Record(Document);
                Document.MarkMetaModified();
                Document.IsRootMotion = !Document.IsRootMotion;
                _rootMotion = Document.IsRootMotion;
            }

            if (EditorUI.Button(OnionSkinButtonId, EditorAssets.Sprites.IconOnion, selected: _onionSkin, toolbar: true))
                _onionSkin = !_onionSkin;
        }

        EditorUI.ToolbarSpacer();
        SkeletonButtonUI(); 
    }

    public override void UpdateUI()
    {
        var isPlaying = _state == AnimationEditorState.Play;
        var currentFrame = Document.CurrentFrame;

        using (UI.BeginCanvas(id: EditorStyle.CanvasId.DocumentEditor))
        using (UI.BeginContainer(EditorStyle.AnimationEditor.Root))
        using (UI.BeginColumn(ContainerStyle.Fit))
        {
            ToolbarUI();

            using (UI.BeginColumn(EditorStyle.Panel.UnpaddedContent with { Spacing = 0.0f}))
            {
                TimelineUI(currentFrame, isPlaying);

            }
        }
    }

    private void UpdateDefaultState()
    {
        if (Workspace.ActiveTool == null && Workspace.DragStarted)
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

        if (_rootMotion)
        {
            ref var rootTransform = ref Document.GetFrameTransform(0, Document.CurrentFrame);
            _rootMotionDelta.X += rootTransform.Position.X * Time.DeltaTime * _playSpeed * 12f;
        }

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
        var offset = _rootMotion ? Vector2.Zero : new Vector2(-Vector2.Transform(Vector2.Zero, Document.LocalToWorld[0]).X, 0);
        return Matrix3x2.CreateTranslation(Document.Position + offset + _rootMotionDelta);
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
        _rootMotionDelta = Vector2.Zero;
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
                Document.MarkModified();
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
                Document.MarkModified();
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

        Document.MarkModified();
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
        Document.MarkModified();
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

        _rootMotionDelta = Vector2.Zero;
        Document.Play();
        _state = AnimationEditorState.Play;
    }

    private void InsertFrameBefore()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.InsertFrame(Document.CurrentFrame);
        Document.UpdateTransforms();
        Document.MarkModified();
    }

    private void InsertFrameAfter()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.InsertFrame(Document.CurrentFrame + 1);
        Document.UpdateTransforms();
        Document.MarkModified();
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
        Document.MarkModified();
    }

    private void DeleteFrame()
    {
        Undo.Record(Document);
        Document.CurrentFrame = Document.DeleteFrame(Document.CurrentFrame);
        Document.UpdateTransforms();
        Document.MarkModified();
    }

    private void AddHoldFrame()
    {
        Undo.Record(Document);
        Document.Frames[Document.CurrentFrame].Hold++;
        Document.MarkModified();
    }

    private void RemoveHoldFrame()
    {
        if (Document.Frames[Document.CurrentFrame].Hold <= 0)
            return;

        Undo.Record(Document);
        Document.Frames[Document.CurrentFrame].Hold = Math.Max(0, Document.Frames[Document.CurrentFrame].Hold - 1);
        Document.MarkModified();
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

        Document.MarkModified();
        Document.UpdateTransforms();
    }

    private void ToggleOnionSkin()
    {
        _onionSkin = !_onionSkin;
    }

    private void ToggleRootMotion()
    {
        _rootMotion = !_rootMotion;
        _rootMotionDelta = Vector2.Zero;
        UpdateSelectionCenter();
        Document.UpdateTransforms();
    }

    private void ToggleLoop()
    {
        Undo.Record(Document);
        Document.SetLooping(!Document.IsLooping);
        Document.MarkModified();
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

        Document.MarkModified();
    }

    private static float GetRotation(Matrix3x2 transform)
    {
        return MathF.Atan2(transform.M12, transform.M11) * 180f / MathF.PI;
    }

    private void DrawBones()
    {
        if (Document.Skeleton == null) return; 

        for (var boneIndex = 0; boneIndex < Document.Skeleton.BoneCount; boneIndex++)
        {
            var bone = Document.Skeleton.Bones[boneIndex];
            var animationBone = Document.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Graphics.SetSortGroup((ushort)(animationBone.IsSelected ? 2 : 1));
            var boneColor = animationBone.IsSelected
                ? EditorStyle.Skeleton.SelectedBoneColor
                : EditorStyle.Skeleton.BoneColor; 
            Gizmos.DrawBone(p0, p1, boneColor, order: (ushort)(boneIndex * 2 + 1));
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

        var boneRadius = EditorStyle.Skeleton.BoneSize * Gizmos.ZoomRefScale;

        var prevFrame = (Document.CurrentFrame - 1 + Document.FrameCount) % Document.FrameCount;
        Document.UpdateTransforms(prevFrame);
        var prevColor = new Color(1f, 0f, 0f, 0.25f);

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Gizmos.DrawBone(p0, p1, prevColor);
            Gizmos.SetColor(prevColor);
            Gizmos.DrawCircle(p0, boneRadius);
        }

        var nextFrame = (Document.CurrentFrame + 1) % Document.FrameCount;
        Document.UpdateTransforms(nextFrame);
        var nextColor = new Color(0f, 1f, 0f, 0.25f);

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            var boneTransform = Document.LocalToWorld[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Gizmos.DrawBone(p0, p1, nextColor);
            Gizmos.SetColor(nextColor);
            Gizmos.DrawCircle(p0, boneRadius);
        }

        Document.UpdateTransforms();
    }

    public override void OnUndoRedo()
    {
        Document.UpdateSkeleton();
        Document.UpdateTransforms();
    }
}
