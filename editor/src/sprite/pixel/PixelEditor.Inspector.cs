//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

public partial class PixelEditor
{
    private static partial class WidgetIds
    {
        public static partial WidgetId BrushColor { get; }
        public static partial WidgetId FilterDropDown { get; }
        public static partial WidgetId TileXToggle { get; }
        public static partial WidgetId TileYToggle { get; }
    }

    public override void InspectorUI()
    {
        GroupInspectorUI();
        SpriteInspectorUI();
        EdgesInspectorUI();
        TilingInspectorUI();
    }

    private void TilingInspectorUI()
    {
        if (!Document.ShowTiling) return;

        using var _ = Inspector.BeginSection("TILING");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginProperty("Tile X"))
        {
            if (UI.Toggle(WidgetIds.TileXToggle, Document.TileX, EditorStyle.Inspector.Toggle))
            {
                Undo.Record(Document);
                Document.TileX = !Document.TileX;
            }
        }

        using (Inspector.BeginProperty("Tile Y"))
        {
            if (UI.Toggle(WidgetIds.TileYToggle, Document.TileY, EditorStyle.Inspector.Toggle))
            {
                Undo.Record(Document);
                Document.TileY = !Document.TileY;
            }
        }
    }

    private void SpriteInspectorUI()
    {
        using var _ = Inspector.BeginSection("SPRITE");
        if (Inspector.IsSectionCollapsed) return;

        using (Inspector.BeginProperty("Filter"))
        {
            var current = Document.TextureFilterOverride ?? TextureFilter.Point;
            var filterLabel = Document.TextureFilterOverride.HasValue
                ? $"{current}"
                : $"{current} (Default)";
            UI.DropDown(WidgetIds.FilterDropDown, () =>
            [
                new PopupMenuItem
                {
                    Label = "Point (Default)",
                    Handler = () =>
                    {
                        Undo.Record(Document);
                        Document.TextureFilterOverride = null;
                        Document.MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                        InvalidateComposite();
                    }
                },
                new PopupMenuItem
                {
                    Label = "Linear",
                    Handler = () =>
                    {
                        Undo.Record(Document);
                        Document.TextureFilterOverride = TextureFilter.Linear;
                        Document.MarkSpriteDirty();
                        AssetManifest.IsModified = true;
                        InvalidateComposite();
                    }
                }
            ], filterLabel);
        }
    }
}
