//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Editor;

internal interface ISpriteSource
{
    string Name { get; }

    AtlasDocument? Atlas { get; set; }

    ushort FrameCount { get; }

    Vector2Int GetFrameAtlasSize(ushort frameIndex);

    void Rasterize(PixelData<Color32> image, in AtlasSpriteRect rect, int padding);

    void UpdateAtlasUVs(AtlasDocument atlas, ReadOnlySpan<AtlasSpriteRect> allRects, int padding);

    void ClearAtlasUVs();

    void Reimport();
}
