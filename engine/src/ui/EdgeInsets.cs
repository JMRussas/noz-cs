//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Security.Cryptography;

namespace NoZ;

public readonly struct EdgeInsets(float top, float left, float bottom, float right)
{
    public readonly float L = left;
    public readonly float R = right;
    public readonly float T = top;
    public readonly float B = bottom;

    public EdgeInsets(float all) : this(all, all, all, all)
    {
    }

    public bool IsZero => T == 0 && L == 0 && B == 0 && R == 0;
    public float Horizontal => L + R;
    public float Vertical => B + T;

    public unsafe float GetAxis(int axis) 
    {
        fixed(float* p = &T)
            return p[axis * 2] + p[axis * 2 + 1];
    }

    public unsafe float GetMin(int axis)
    {
        fixed(float* p = &T)
            return p[axis * 2];
    }

    public unsafe float GetMax(int axis)
    {
        fixed (float* p = &T)
            return p[axis * 2 + 1];
    }

    public static EdgeInsets All(float v) => new(v, v, v, v);
    public static EdgeInsets Top(float v) => new(v, v, 0, 0);
    public static EdgeInsets Bottom(float v) => new(0, 0, v, 0);
    public static EdgeInsets Left(float v) => new(0, v, 0, 0);
    public static EdgeInsets Right(float v) => new(0, 0, 0, v);
    public static EdgeInsets TopBottom(float v) => new(v, 0, v, 0);
    public static EdgeInsets BottomRight(float v) => new(0, 0, v, v);
    public static EdgeInsets LeftRight(float v) => new(0, v, 0, v);
    public static EdgeInsets LeftRight(float l, float r) => new(0, l, 0, r);
    public static EdgeInsets TopLeft(float t, float l) => new(t, 0, l, 0);

    public static EdgeInsets Symmetric(float vertical, float horizontal) =>
        new(horizontal, horizontal, vertical, vertical);

    public static readonly EdgeInsets Zero = new(0, 0, 0, 0);

    public override string ToString()
    {
        if (L == R && R == T && T == B)
            return $"{L}";

        if (L == 0 && R == 0)
        {
            if (T == B)
                return $"<V:{T}>";
            else if (T != 0 && B != 0)
                return $"<T:{T}, B:{B}>";
            else if (B != 0)
                return $"<B:{B}>";
            else if (T != 0)
                return $"<T:{T}>";
        }

        if (T == 0 && B == 0)
        {
            if (R == L)
                return $"<H:{R}>";
            else if (L != 0 && R != 0)
                return $"<L:{L}, R:{R}>";
            else if (R != 0)
                return $"<R:{R}>";
            else if (L != 0)
                return $"<L:{L}>";
        }


        if (L == R && T == B)
            return $"<H:{L}, V:{T}>";

        return $"<L:{L}, R:{R}, T:{T}, B:{B}>";
    }
}
