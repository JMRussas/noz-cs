//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public enum UIScaleMode
{
    ConstantPixelSize,
    ScaleWithScreenSize
}

public enum ScreenMatchMode
{
    MatchWidthOrHeight,
    Expand,
    Shrink
}

public class UIConfig
{
    public string Shader { get; init; } = "ui";
    public string DefaultFont { get; init; } = "";
    public string AtlasArray { get; init; } = "";

    public UIScaleMode ScaleMode { get; init; } = UIScaleMode.ScaleWithScreenSize;
    public Vector2Int ReferenceResolution { get; init; } = new(1920, 1080);
    public ScreenMatchMode ScreenMatchMode { get; init; } = ScreenMatchMode.MatchWidthOrHeight;
    public float MatchWidthOrHeight { get; init; } = 0.5f;

    public ushort UILayer { get; init; } = 1000;
}
