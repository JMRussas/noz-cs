//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public abstract partial class SpriteEditor(SpriteDocument document) : DocumentEditor(document)
{
    private static partial class WidgetIds
    {
        public static partial WidgetId GroupBoneDropDown { get; }
        public static partial WidgetId GroupSortOrder { get; }
    }

    public new SpriteDocument Document => (SpriteDocument)base.Document;

    protected abstract bool IsNodeSelected(SpriteNode node);
    protected virtual bool IsNodeActive(SpriteNode node) => false;
    protected abstract void OnNodeClicked(SpriteNode node);
    protected abstract void OnOutlinerChanged();
    protected virtual void OnVisibilityChanged(SpriteNode node) { }
    protected virtual string GetNodeFallbackName(SpriteNode node) => "Node";
    protected virtual Sprite GetNodeIcon(SpriteNode node) => node is SpriteGroup
        ? EditorAssets.Sprites.IconFolder
        : EditorAssets.Sprites.IconPath;
    protected virtual bool ReverseChildren => false;

    protected virtual void OnNodeRightClicked(SpriteNode node, bool isHovered) { }
    protected virtual Sprite? GetNodePreview(SpriteNode node) => null;
    protected virtual void OnNodeFrameSwitch(int frameIndex) { }

    protected void DrawSkeletonOverlay()
    {
        var skeleton = Document.Skeleton.Value;
        if (skeleton == null)
            return;

        using (Graphics.PushState())
        {
            Graphics.SetSortGroup(0);
            Graphics.SetLayer(EditorLayer.DocumentEditor);
            foreach (var bound in skeleton.Attachments)
            {
                if (bound is not SpriteDocument sprite || sprite == Document) continue;
                Graphics.SetBlendMode(BlendMode.Alpha);
                Graphics.SetTransform(Document.Transform);
                sprite.DrawSprite(alpha: 0.3f);
            }
        }

        using (Gizmos.PushState(EditorLayer.DocumentEditor))
        {
            Graphics.SetSortGroup(6);
            Graphics.SetTransform(Document.Transform);

            for (var boneIndex = 0; boneIndex < skeleton.BoneCount; boneIndex++)
                Gizmos.DrawBoneAndJoints(skeleton, boneIndex, selected: false);
        }
    }

    protected virtual void PopulateDragNodes(SpriteNode clickedNode, List<SpriteNode> dragNodes)
    {
        dragNodes.Clear();
        dragNodes.Add(clickedNode);
    }

    protected void GroupInspectorUI()
    {
        SpriteGroup? selectedGroup = null;
        var selectedCount = 0;
        Document.Root.ForEach(group =>
        {
            if (group == Document.Root || !IsNodeSelected(group))
                return;
            selectedGroup = group;
            selectedCount++;
        });

        if (selectedCount != 1 || selectedGroup == null)
            return;

        var group = selectedGroup;
        using var _ = Inspector.BeginSection("GROUP SPRITE");
        if (Inspector.IsSectionCollapsed)
            return;

        if (Document.Skeleton.IsResolved)
        {
            using (Inspector.BeginProperty("Bone"))
            {
                var skeleton = Document.Skeleton.Value!;
                UI.DropDown(WidgetIds.GroupBoneDropDown, () =>
                {
                    var items = new List<PopupMenuItem>();
                    for (var i = 0; i < skeleton.BoneCount; i++)
                    {
                        var boneName = skeleton.Bones[i].Name;
                        items.Add(new PopupMenuItem
                        {
                            Label = boneName,
                            Handler = () =>
                            {
                                Undo.Record(Document);
                                group.BoneName = boneName;
                                OnGroupSpriteChanged();
                            }
                        });
                    }
                    items.Add(new PopupMenuItem
                    {
                        Label = "None",
                        Handler = () =>
                        {
                            Undo.Record(Document);
                            group.BoneName = null;
                            OnGroupSpriteChanged();
                        }
                    });
                    return [.. items];
                }, group.BoneName ?? "None", EditorAssets.Sprites.IconBone);
            }
        }

        using (Inspector.BeginProperty("Sort Order"))
        {
            EditorUI.SortOrderDropDown(WidgetIds.GroupSortOrder, group.SortOrderId, id =>
            {
                Undo.Record(Document);
                group.SortOrderId = id;
                OnGroupSpriteChanged();
            });
        }
    }

    private void OnGroupSpriteChanged()
    {
        Document.IncrementVersion();
        AtlasManager.UpdateSource(Document);
        AssetManifest.IsModified = true;
        Document.Skeleton.Value?.UpdateSprites();
    }
}
