//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Engine.UI;

public enum Align
{
    Min,
    Center,
    Max
}

public static class AlignExtensions
{
    private static readonly float[] AlignFactor = [0.0f, 0.5f, 1.0f];

    public static float ToFactor(this Align align) => AlignFactor[(int)align];
}
