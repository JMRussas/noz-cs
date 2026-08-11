//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

internal partial class BoneMode : EditorMode<SkeletonEditor>
{
    private static partial class ElementId
    {
        public static partial WidgetId RenameTextBox { get; }
    }

    private enum DragState { None, MoveBones, BoxSelect }

    private DragState _dragState;
    private bool _clearSelectionOnUp;
    private bool _ignoreUp;

    // Move drag state
    private Vector2 _moveStartWorld;

    // Box select state (uses Workspace.DragWorldPosition + MouseWorldPosition)

    // Rename state
    private bool _isRenaming;
    private int _renameBoneIndex;
    private string _renameOriginalName = "";
    private string _renameCurrentText = "";

    public bool IsRenaming => _isRenaming;
    public int RenamingBoneIndex => _isRenaming ? _renameBoneIndex : -1;

    public override void Update()
    {
        // Rename mode — handled mostly in DrawUI, but check cancel/commit here
        if (_isRenaming)
        {
            if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All))
            {
                Input.ConsumeButton(InputCode.KeyEscape);
                EndRename(commit: false);
                return;
            }

            if (Input.WasButtonPressed(InputCode.KeyEnter, InputScope.All))
            {
                Input.ConsumeButton(InputCode.KeyEnter);
                EndRename(commit: true);
                return;
            }
            return;
        }

        // Active drag states
        switch (_dragState)
        {
            case DragState.MoveBones:
                if (Input.WasButtonPressed(InputCode.KeyEscape, InputScope.All) ||
                    Input.WasButtonPressed(InputCode.MouseRight, InputScope.All))
                {
                    CancelMoveDrag();
                    return;
                }
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All))
                {
                    CommitMoveDrag();
                    return;
                }
                UpdateMoveDrag();
                return;

            case DragState.BoxSelect:
                if (Input.WasButtonReleased(InputCode.MouseLeft, InputScope.All) || !Workspace.IsDragging)
                {
                    CommitBoxSelect();
                    return;
                }
                return;
        }

        // Idle: detect drag start or click
        if (Workspace.DragStarted && Workspace.DragButton == InputCode.MouseLeft)
        {
            HandleDragStart();
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
            Editor.ClearSelection();
    }

    private void HandleDragStart()
    {
        var hit = Editor.Document.HitTestJoints(Workspace.DragWorldPosition, cycle: false);
        if (hit.BoneIndex >= 0)
        {
            var bone = Editor.Document.Bones[hit.BoneIndex];
            var isSelected = hit.HitType switch
            {
                BoneHitType.Head => bone.IsHeadSelected,
                BoneHitType.Tail => bone.IsTailSelected,
                BoneHitType.Bone => bone.IsFullySelected,
                _ => false
            };

            if (!isSelected)
            {
                if (!Input.IsShiftDown(InputScope.All))
                    Editor.ClearSelection();

                switch (hit.HitType)
                {
                    case BoneHitType.Head:
                        Editor.SelectHeadJoint(hit.BoneIndex, true);
                        break;
                    case BoneHitType.Tail:
                        Editor.SelectTailJoint(hit.BoneIndex, true);
                        break;
                    case BoneHitType.Bone:
                        Editor.SelectBone(hit.BoneIndex, true);
                        break;
                }

                Editor.UpdateConnectedToggleState();
            }

            BeginMoveDrag(recordUndo: true);
            return;
        }

        // Empty space: box select
        _dragState = DragState.BoxSelect;
    }

    private bool TrySelect()
    {
        var shiftDown = Input.IsShiftDown(InputScope.All);
        var hit = Editor.Document.HitTestJoints(Workspace.MouseWorldPosition, cycle: !shiftDown);
        if (hit.BoneIndex == -1)
            return false;

        if (!shiftDown)
            Editor.ClearSelection();

        var bone = Editor.Document.Bones[hit.BoneIndex];

        switch (hit.HitType)
        {
            case BoneHitType.Head:
                if (shiftDown) Editor.SelectHeadJoint(hit.BoneIndex, !bone.IsHeadSelected);
                else Editor.SelectHeadJoint(hit.BoneIndex, true);
                break;
            case BoneHitType.Tail:
                if (shiftDown) Editor.SelectTailJoint(hit.BoneIndex, !bone.IsTailSelected);
                else Editor.SelectTailJoint(hit.BoneIndex, true);
                break;
            case BoneHitType.Bone:
                if (shiftDown) Editor.SelectBone(hit.BoneIndex, !bone.IsFullySelected);
                else Editor.SelectBone(hit.BoneIndex, true);
                break;
        }

        Editor.UpdateConnectedToggleState();
        return true;
    }

    // --- Move Drag ---

    internal void BeginMoveDrag(bool recordUndo)
    {
        Editor.SaveState();
        if (recordUndo)
            Undo.Record(Editor.Document);
        _moveStartWorld = Workspace.MouseWorldPosition;
        _dragState = DragState.MoveBones;
    }

    private void UpdateMoveDrag()
    {
        var delta = Workspace.MouseWorldPosition - _moveStartWorld;
        Editor.ApplyMoveDelta(delta);
    }

    private void CommitMoveDrag()
    {
        UpdateMoveDrag();
        Editor.Document.IncrementVersion();
        Editor.Document.NotifyTransformsChanged();
        Input.ConsumeButton(InputCode.MouseLeft);
        _dragState = DragState.None;
    }

    private void CancelMoveDrag()
    {
        Undo.Cancel();
        _dragState = DragState.None;
    }

    // --- Box Select ---

    private void CommitBoxSelect()
    {
        var p0 = Workspace.DragWorldPosition;
        var p1 = Workspace.MouseWorldPosition;
        var bounds = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));
        Input.ConsumeButton(InputCode.MouseLeft);
        Editor.HandleBoxSelect(bounds);
        _dragState = DragState.None;
    }

    // --- Rename ---

    internal void BeginRename(int boneIndex, string name)
    {
        _isRenaming = true;
        _renameBoneIndex = boneIndex;
        _renameOriginalName = name;
        _renameCurrentText = name;
        UI.SetHot(ElementId.RenameTextBox);
    }

    private void EndRename(bool commit)
    {
        if (commit && !string.IsNullOrWhiteSpace(_renameCurrentText) && _renameCurrentText != _renameOriginalName)
        {
            var oldName = _renameOriginalName;
            Undo.Record(Editor.Document);
            Editor.Document.Bones[_renameBoneIndex].Name = _renameCurrentText;
            Editor.Document.NotifyBoneRenamed(_renameBoneIndex, oldName, _renameCurrentText);
            Editor.Document.IncrementVersion();
        }
        _isRenaming = false;
        UI.ClearHot();
    }

    // --- Extrude (create bone) support ---

    internal void SetIgnoreUp() => _ignoreUp = true;

    // --- Draw ---

    public override void Draw()
    {
        if (_dragState == DragState.BoxSelect)
        {
            var p0 = Workspace.DragWorldPosition;
            var p1 = Workspace.MouseWorldPosition;
            var rect = Rect.FromMinMax(Vector2.Min(p0, p1), Vector2.Max(p0, p1));

            using var _ = Gizmos.PushState(EditorLayer.Tool);
            Graphics.SetColor(EditorStyle.BoxSelect.FillColor);
            Graphics.Draw(rect);
            Graphics.SetColor(EditorStyle.BoxSelect.LineColor);
            Gizmos.DrawRect(rect, EditorStyle.BoxSelect.LineWidth);
        }
    }

    public override void DrawUI()
    {
        if (!_isRenaming) return;

        var bone = Editor.Document.Bones[_renameBoneIndex];
        var worldPos = (bone.HeadWorld + bone.TailWorld) * 0.5f + Editor.Document.Position;
        var screenPos = Workspace.Camera.WorldToScreen(worldPos);
        var uiPos = UI.ScreenToUI(screenPos);

        var textStyle = EditorStyle.RenameTool.Text;
        var font = textStyle.Font ?? UI.DefaultFont;
        var textSize = TextRender.Measure(_renameCurrentText.AsSpan(), font, textStyle.FontSize);
        var padding = EditorStyle.RenameTool.Content.Padding;
        var textInputHeight = textStyle.Height.IsFixed ? textStyle.Height.Value : textStyle.FontSize * 1.8f;

        var border = EditorStyle.RenameTool.Content.BorderWidth;
        uiPos.X -= textSize.X * 0.5f + padding.L + border;
        uiPos.Y -= textInputHeight * 0.5f + padding.T + border;

        using (UI.BeginContainer(EditorStyle.RenameTool.Content with { AlignX = Align.Min, AlignY = Align.Min, Margin = EdgeInsets.TopLeft(uiPos.Y, uiPos.X) }))
        {
            _renameCurrentText = UI.TextInput(ElementId.RenameTextBox, _renameCurrentText, textStyle);

            if (UI.HotEnter())
                UI.SetWidgetText(ElementId.RenameTextBox, _renameOriginalName, selectAll: true);

            if (UI.HotExit())
                EndRename(commit: true);
        }
    }
}
