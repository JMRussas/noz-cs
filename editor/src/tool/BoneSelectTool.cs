//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public class BoneSelectTool : Tool
{
    private readonly Action<SkeletonDocument, int> _commit;
    private SkeletonDocument? _hoverSkeleton;
    private int _hoverBoneIndex = -1;

    public BoneSelectTool(Action<SkeletonDocument, int> commit)
    {
        _commit = commit;
    }

    public override void Begin()
    {
        Cursor.Set(SystemCursor.Crosshair);
    }

    public override void Update()
    {
        if (Input.WasButtonPressed(InputCode.KeyEscape) || Input.WasButtonPressed(InputCode.MouseRight))
        {
            Workspace.CancelTool();
            return;
        }

        UpdateHover();

        if (Input.WasButtonPressed(InputCode.MouseLeft))
        {
            if (_hoverSkeleton != null && _hoverBoneIndex >= 0)
            {
                _commit(_hoverSkeleton, _hoverBoneIndex);
                Input.ConsumeButton(InputCode.MouseLeft);
            }
            Workspace.EndTool();
        }
    }

    private void UpdateHover()
    {
        _hoverSkeleton = null;
        _hoverBoneIndex = -1;

        var mousePos = Workspace.MouseWorldPosition;

        foreach (var doc in DocumentManager.Documents)
        {
            if (doc is not SkeletonDocument skeleton)
                continue;

            if (!doc.Loaded || !doc.PostLoaded || doc.IsClipped)
                continue;

            if (!skeleton.Bounds.Contains(mousePos - skeleton.Position))
                continue;

            _hoverSkeleton = skeleton;

            var boneIndex = skeleton.HitTestBone(mousePos);
            if (boneIndex >= 0)
            {
                _hoverBoneIndex = boneIndex;
                return;
            }
        }
    }

    public override void Draw()
    {
        if (_hoverSkeleton == null && _hoverBoneIndex < 0)
            return;

        using (Gizmos.PushState(EditorLayer.Tool))
        {
            _hoverSkeleton?.DrawBounds(true);

            if (_hoverBoneIndex >= 0)
            {
                var bone = _hoverSkeleton!.Bones[_hoverBoneIndex];
                var transform = Matrix3x2.CreateTranslation(_hoverSkeleton.Position);
                var p0 = Vector2.Transform(Vector2.Zero, bone.LocalToWorld * transform);
                var p1 = Vector2.Transform(new Vector2(bone.Length, 0), bone.LocalToWorld * transform);
                Gizmos.DrawBone(p0, p1, EditorStyle.Skeleton.SelectedBoneColor, order: 100);
            }
        }
    }
}
