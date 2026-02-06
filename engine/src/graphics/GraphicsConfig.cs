//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public class GraphicsConfig 
{
    public bool Vsync { get; init; } = true;
    public int MaxDrawCommands { get; init; } = 16384;
    public int MaxBatches { get; init; } = 4096;
    public string SpriteShader { get; init; } = "sprite";
    public required IGraphicsDriver Driver { get; init; }
    public float PixelsPerUnit { get; init; } = 64.0f;
}
