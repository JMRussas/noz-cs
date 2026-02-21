//
//  Faithful port of msdfgen by Viktor Chlumsky
//  https://github.com/Chlumsky/msdfgen
//

using System;

namespace NoZ.Editor.Msdf;

internal struct SignedDistance
{
    public double distance;
    public double dot;

    public SignedDistance()
    {
        distance = -double.MaxValue;
        dot = 0;
    }

    public SignedDistance(double dist, double d)
    {
        distance = dist;
        dot = d;
    }

    public static bool operator <(SignedDistance a, SignedDistance b)
    {
        return Math.Abs(a.distance) < Math.Abs(b.distance)
            || (Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot < b.dot);
    }

    public static bool operator >(SignedDistance a, SignedDistance b)
    {
        return Math.Abs(a.distance) > Math.Abs(b.distance)
            || (Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot > b.dot);
    }

    public static bool operator <=(SignedDistance a, SignedDistance b)
    {
        return Math.Abs(a.distance) < Math.Abs(b.distance)
            || (Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot <= b.dot);
    }

    public static bool operator >=(SignedDistance a, SignedDistance b)
    {
        return Math.Abs(a.distance) > Math.Abs(b.distance)
            || (Math.Abs(a.distance) == Math.Abs(b.distance) && a.dot >= b.dot);
    }
}
