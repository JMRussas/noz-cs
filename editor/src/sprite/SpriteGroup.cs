//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public class SpriteGroup : SpriteNode
{
    public string? BoneName { get; set; }
    public string? SortOrderId { get; set; }
    public bool IsSprite => BoneName != null || SortOrderId != null;

    public override bool IsExpandable => true;
    public override SpriteNode Clone()
    {
        var clone = new SpriteGroup();
        ClonePropertiesTo(clone);
        clone.BoneName = BoneName;
        clone.SortOrderId = SortOrderId;
        foreach (var child in Children)
            clone.Add(child.Clone());
        return clone;
    }
}
