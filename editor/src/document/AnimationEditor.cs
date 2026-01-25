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
    public new AnimationDocument Document => (AnimationDocument)base.Document;

    private AnimationEditorState _state = AnimationEditorState.Default;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;
    private Vector2 _selectionCenter;
    private Vector2 _selectionCenterWorld;
    private bool _onionSkin;
    private AnimationFrameData _clipboard = new();
    private Vector2 _rootMotionDelta;
    private bool _rootMotion = true;
    private float _playSpeed = 1f;

    private readonly Command[] _commands;

    public AnimationEditor(AnimationDocument document) : base(document)
    {
        _commands =
        [
            new Command { Name = "Toggle Playback", ShortName = "play", Handler = TogglePlayback, Key = InputCode.KeySpace },
            new Command { Name = "Previous Frame", ShortName = "prev", Handler = PreviousFrame, Key = InputCode.KeyQ },
            new Command { Name = "Next Frame", ShortName = "next", Handler = NextFrame, Key = InputCode.KeyE },
            new Command { Name = "Move", ShortName = "move", Handler = BeginMoveTool, Key = InputCode.KeyG },
            new Command { Name = "Rotate", ShortName = "rotate", Handler = BeginRotateTool, Key = InputCode.KeyR },
            new Command { Name = "Reset Rotation", ShortName = "rrot", Handler = ResetRotation, Key = InputCode.KeyR, Shift = true },
            new Command { Name = "Reset Position", ShortName = "rpos", Handler = ResetPosition, Key = InputCode.KeyG, Shift = true },
            new Command { Name = "Select All", ShortName = "all", Handler = SelectAll, Key = InputCode.KeyA },
            new Command { Name = "Insert Frame Before", ShortName = "insbefore", Handler = InsertFrameBefore, Key = InputCode.KeyI },
            new Command { Name = "Insert Frame After", ShortName = "insafter", Handler = InsertFrameAfter, Key = InputCode.KeyO },
            new Command { Name = "Insert Frame Lerp", ShortName = "inslerp", Handler = InsertFrameAfterLerp, Key = InputCode.KeyO, Alt = true },
            new Command { Name = "Delete Frame", ShortName = "delframe", Handler = DeleteFrame, Key = InputCode.KeyX },
            new Command { Name = "Add Hold", ShortName = "hold+", Handler = AddHoldFrame, Key = InputCode.KeyH },
            new Command { Name = "Remove Hold", ShortName = "hold-", Handler = RemoveHoldFrame, Key = InputCode.KeyH, Shift = true },
            new Command { Name = "Copy Keys", ShortName = "copy", Handler = CopyKeys, Key = InputCode.KeyC, Ctrl = true },
            new Command { Name = "Paste Keys", ShortName = "paste", Handler = PasteKeys, Key = InputCode.KeyV, Ctrl = true },
            new Command { Name = "Toggle Onion Skin", ShortName = "onion", Handler = ToggleOnionSkin, Key = InputCode.KeyO, Shift = true },
            new Command { Name = "Toggle Root Motion", ShortName = "root", Handler = ToggleRootMotion, Key = InputCode.KeyM, Shift = true },
            new Command { Name = "Toggle Loop", ShortName = "loop", Handler = ToggleLoop, Key = InputCode.KeyL },
            new Command { Name = "Increase Play Speed", ShortName = "speed+", Handler = IncreasePlaySpeed, Key = InputCode.KeyRight },
            new Command { Name = "Decrease Play Speed", ShortName = "speed-", Handler = DecreasePlaySpeed, Key = InputCode.KeyLeft },
            new Command { Name = "Mirror Pose", ShortName = "mirror", Handler = MirrorPose, Key = InputCode.KeyM },
        ];

        Commands = _commands;
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

    public override void UpdateUI()
    {
        var frameCount = Math.Max(Document.GetFrameCountWithHolds(), EditorStyle.AnimationEditor.MinFrames);
        var panelWidth = frameCount * EditorStyle.AnimationEditor.FrameWidth + EditorStyle.AnimationEditor.Padding * 2 + 1;

        var isPlaying = _state == AnimationEditorState.Play;
        var currentFrame = Document.CurrentFrame;

        using (UI.BeginCanvas(id: EditorStyle.CanvasId.DocumentEditor))
        using (UI.BeginContainer(EditorStyle.AnimationEditor.Root))
        using (UI.BeginContainer(EditorStyle.AnimationEditor.Panel with { Width = panelWidth }))
        using (UI.BeginColumn())
        {
            using (UI.BeginRow(EditorStyle.Overlay.Toolbar))
            {
                //DopeSheetButton("M", false, MirrorPose);
                //UI.Flex();
                //DopeSheetButton("L", Document.IsLooping, ToggleLoop);
                //DopeSheetButton("R", _rootMotion, ToggleRootMotion);
                //DopeSheetButton("O", _onionSkin, ToggleOnionSkin);
            }

            UI.Container(EditorStyle.AnimationEditor.HorizontalBorder);

            // Ticks row
            using (UI.BeginRow())
            {
                var lastRealFrameIndex = -1;
                for (var frameIndex = 0; frameIndex <= frameCount; frameIndex++)
                {
                    var isLastTick = frameIndex == frameCount;
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
            var boneTransform = Document.AnimatorBones[boneIndex] * baseTransform;
            var boneStart = Vector2.Transform(Vector2.Zero, boneTransform);
            var boneEnd = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            if (bounds.Contains(boneStart) || bounds.Contains(boneEnd))
                SetBoneSelected(boneIndex, true);
        }
    }

    private Matrix3x2 GetBaseTransform()
    {
        var offset = _rootMotion ? Vector2.Zero : new Vector2(-Vector2.Transform(Vector2.Zero, Document.AnimatorBones[0]).X, 0);
        return Matrix3x2.CreateTranslation(Document.Position + offset + _rootMotionDelta);
    }

    private bool TrySelectBone()
    {
        var bones = new int[SkeletonDocument.MaxBones];
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

    private bool IsBoneSelected(int boneIndex) => Document.Bones[boneIndex].Selected;

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

        Document.Bones[boneIndex].Selected = selected;
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
            center += Vector2.Transform(Vector2.Zero, Document.AnimatorBones[boneIndex]);
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
                Matrix3x2.Invert(Document.AnimatorBones[parentIndex], out var invParent);
                var rotatedDelta = Vector2.TransformNormal(delta, invParent);
                frame.Position = bone.SavedTransform.Position + rotatedDelta;
            }
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

        Workspace.BeginTool(new RotateTool(
            _selectionCenterWorld,
            _selectionCenter,
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
        for (var boneIndex = 0; boneIndex < SkeletonDocument.MaxBones; boneIndex++)
            _clipboard.Transforms[boneIndex] = Document.Frames[Document.CurrentFrame].Transforms[boneIndex];
    }

    private void PasteKeys()
    {
        Undo.Record(Document);

        for (var boneIndex = 0; boneIndex < SkeletonDocument.MaxBones; boneIndex++)
        {
            if (!IsBoneSelected(boneIndex))
                continue;
            Document.Frames[Document.CurrentFrame].Transforms[boneIndex] = _clipboard.Transforms[boneIndex];
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

        var savedWorldTransforms = new Matrix3x2[SkeletonDocument.MaxBones];
        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            savedWorldTransforms[boneIndex] = Document.AnimatorBones[boneIndex];

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
                ? Document.AnimatorBones[bone.ParentIndex]
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

    private void DrawEditor()
    {
        var skeleton = Document.Skeleton;
        if (skeleton == null)
            return;

        var baseTransform = GetBaseTransform();

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetTransform(baseTransform);

            if (_state != AnimationEditorState.Play)
            {
                DrawOnionSkin();
            }

            var lineWidth = EditorStyle.Skeleton.BoneWidth * Gizmos.ZoomRefScale;
            var boneRadius = EditorStyle.Skeleton.BoneSize * Gizmos.ZoomRefScale;

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            {
                if (IsBoneSelected(boneIndex))
                    continue;

                var bone = skeleton.Bones[boneIndex];
                var boneTransform = Document.AnimatorBones[boneIndex];
                var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
                var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

                Gizmos.DrawBone(p0, p1, lineWidth, EditorStyle.Skeleton.BoneColor);
                Gizmos.SetColor(EditorStyle.Skeleton.BoneColor);
                Gizmos.DrawCircle(p0, boneRadius);
            }

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
            {
                if (!IsBoneSelected(boneIndex))
                    continue;

                var bone = skeleton.Bones[boneIndex];
                var boneTransform = Document.AnimatorBones[boneIndex];
                var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
                var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

                Gizmos.DrawBone(p0, p1, lineWidth, EditorStyle.SelectionColor);
                Gizmos.SetColor(EditorStyle.SelectionColor);
                Gizmos.DrawCircle(p0, boneRadius);
            }
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

        var lineWidth = EditorStyle.Skeleton.BoneWidth * Gizmos.ZoomRefScale;
        var boneRadius = EditorStyle.Skeleton.BoneSize * Gizmos.ZoomRefScale;

        var prevFrame = (Document.CurrentFrame - 1 + Document.FrameCount) % Document.FrameCount;
        Document.UpdateTransforms(prevFrame);
        var prevColor = new Color(1f, 0f, 0f, 0.25f);

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            var boneTransform = Document.AnimatorBones[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Gizmos.DrawBone(p0, p1, lineWidth, prevColor);
            Gizmos.SetColor(prevColor);
            Gizmos.DrawCircle(p0, boneRadius);
        }

        var nextFrame = (Document.CurrentFrame + 1) % Document.FrameCount;
        Document.UpdateTransforms(nextFrame);
        var nextColor = new Color(0f, 1f, 0f, 0.25f);

        for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
        {
            var bone = skeleton.Bones[boneIndex];
            var boneTransform = Document.AnimatorBones[boneIndex];
            var p0 = Vector2.Transform(Vector2.Zero, boneTransform);
            var p1 = Vector2.Transform(new Vector2(bone.Length, 0), boneTransform);

            Gizmos.DrawBone(p0, p1, lineWidth, nextColor);
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
