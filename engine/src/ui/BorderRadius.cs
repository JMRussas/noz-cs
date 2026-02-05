//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public readonly struct BorderRadius
{
    public readonly float TopLeft;
    public readonly float TopRight;
    public readonly float BottomLeft;
    public readonly float BottomRight;

    private BorderRadius(float topLeft, float topRight, float bottomLeft, float bottomRight)
    {
        TopLeft = topLeft;
        TopRight = topRight;
        BottomLeft = bottomLeft;
        BottomRight = bottomRight;
    }

    public bool IsZero => TopLeft == 0 && TopRight == 0 && BottomLeft == 0 && BottomRight == 0;
    public float Max => Math.Max(Math.Max(TopLeft, TopRight), Math.Max(BottomLeft, BottomRight));

    public static BorderRadius Circular(float radius) => new(radius, radius, radius, radius);

    public static BorderRadius Only(
        float topLeft = 0,
        float topRight = 0,
        float bottomLeft = 0,
        float bottomRight = 0) => new(topLeft, topRight, bottomLeft, bottomRight);

    public static BorderRadius Vertical(float top = 0, float bottom = 0) =>
        new(top, top, bottom, bottom);

    public static BorderRadius Horizontal(float left = 0, float right = 0) =>
        new(left, right, left, right);

    public static implicit operator BorderRadius(float v) => new(v, v, v, v);

    public static readonly BorderRadius Zero = new(0, 0, 0, 0);

    public override string ToString()
    {
        if (TopLeft == TopRight && TopRight == BottomLeft && BottomLeft == BottomRight)
            return $"{TopLeft}";
        return $"<TL:{TopLeft}, TR:{TopRight}, BL:{BottomLeft}, BR:{BottomRight}>";
    }
}
