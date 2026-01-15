//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;

namespace NoZ.Editor
{
    partial class MSDF
    {
        struct SignedDistance
        {
            public static readonly SignedDistance Infinite = new SignedDistance(double.NegativeInfinity, 1.0);

            public double distance;
            public double dot;

            public SignedDistance(double distance, double dot)
            {
                this.distance = distance;
                this.dot = dot;
            }

            public static bool operator <(SignedDistance lhs, SignedDistance rhs)
            {
                return Math.Abs(lhs.distance) < Math.Abs(rhs.distance) || (Math.Abs(lhs.distance) == Math.Abs(rhs.distance) && lhs.dot < rhs.dot);
            }

            public static bool operator >(SignedDistance lhs, SignedDistance rhs)
            {
                return Math.Abs(lhs.distance) > Math.Abs(rhs.distance) || (Math.Abs(lhs.distance) == Math.Abs(rhs.distance) && lhs.dot > rhs.dot);
            }

            public static bool operator <=(SignedDistance lhs, SignedDistance rhs)
            {
                return Math.Abs(lhs.distance) < Math.Abs(rhs.distance) || (Math.Abs(lhs.distance) == Math.Abs(rhs.distance) && lhs.dot <= rhs.dot);
            }

            public static bool operator >=(SignedDistance lhs, SignedDistance rhs)
            {
                return Math.Abs(lhs.distance) > Math.Abs(rhs.distance) || (Math.Abs(lhs.distance) == Math.Abs(rhs.distance) && lhs.dot >= rhs.dot);
            }
        }
    }
}
