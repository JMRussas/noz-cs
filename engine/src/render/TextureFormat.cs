//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public enum TextureFormat : byte
{
    RGBA8 = 0,
    RGB8 = 1,
    R8 = 2,
    RG8 = 3,
}

public enum TextureFilter : byte
{
    Nearest = 0,
    Linear = 1,
}

public enum TextureClamp : byte
{
    Repeat = 0,
    Clamp = 1,
}
