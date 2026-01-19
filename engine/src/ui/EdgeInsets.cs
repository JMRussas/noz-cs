//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Engine.UI;

public readonly struct EdgeInsets(float top, float left, float bottom, float right)
{
    public readonly float T = top;
    public readonly float L = left;
    public readonly float B = bottom;
    public readonly float R = right;

    public EdgeInsets(float all) : this(all, all, all, all)
    {
    }

    public float Horizontal => L + R;
    public float Vertical => T + B;

    public static EdgeInsets All(float v) => new(v, v, v, v);
    public static EdgeInsets Top(float v) => new(v, 0, 0, 0);
    public static EdgeInsets Bottom(float v) => new(0, 0, v, 0);
    public static EdgeInsets Left(float v) => new(0, v, 0, 0);
    public static EdgeInsets Right(float v) => new(0, 0, 0, v);
    public static EdgeInsets TopBottom(float v) => new(v, 0, v, 0);
    public static EdgeInsets LeftRight(float v) => new(0, v, 0, v);
    public static EdgeInsets LeftRight(float l, float r) => new(0, l, 0, r);
    public static EdgeInsets TopLeft(float t, float l) => new(t, l, 0, 0);

    public static EdgeInsets Symmetric(float vertical, float horizontal) =>
        new(vertical, horizontal, vertical, horizontal);

    public static readonly EdgeInsets Zero = new(0, 0, 0, 0);
}
