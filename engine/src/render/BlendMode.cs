//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public enum BlendMode : byte
{
    None = 0,           // No blending (opaque)
    Alpha = 1,          // Standard alpha blend: src*srcA + dst*(1-srcA)
    Additive = 2,       // Additive: src*srcA + dst
    Multiply = 3,       // Multiply: src * dst
    Premultiplied = 4,  // Premultiplied alpha: src + dst*(1-srcA)
}
