//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System;

namespace NoZ.Editor
{
    partial class MSDF
    {
        private static int Sign(double n) => (0.0 < n ? 1 : 0) - (n < 0.0 ? 1 : 0);
        private static int NonZeroSign(double n) => 2 * (n > 0.0 ? 1 : 0) - 1;
        private static double ShoeLace(Vector2Double a, Vector2Double b) => (b.x - a.x) * (a.y + b.y);

        private static int SolveQuadratic(ref double x0, ref double x1, double a, double b, double c)
        {
            if (Math.Abs(a) < 1e-14)
            {
                if (Math.Abs(b) < 1e-14)
                {
                    if (c == 0)
                        return -1;
                    return 0;
                }
                x0 = -c / b;
                return 1;
            }
            double dscr = b * b - 4 * a * c;
            if (dscr > 0)
            {
                dscr = Math.Sqrt(dscr);
                x0 = (-b + dscr) / (2 * a);
                x1 = (-b - dscr) / (2 * a);
                return 2;
            }
            else if (dscr == 0)
            {
                x0 = -b / (2 * a);
                return 1;
            }
            else
                return 0;
        }

        private static int SolveCubicNormed(ref double x0, ref double x1, ref double x2, double a, double b, double c)
        {
            double a2 = a * a;
            double q = (a2 - 3 * b) / 9;
            double r = (a * (2 * a2 - 9 * b) + 27 * c) / 54;
            double r2 = r * r;
            double q3 = q * q * q;
            double A, B;
            if (r2 < q3)
            {
                double t = r / Math.Sqrt(q3);
                if (t < -1)
                    t = -1;
                if (t > 1)
                    t = 1;
                t = Math.Acos(t);
                a /= 3;
                q = -2 * Math.Sqrt(q);
                x0 = q * Math.Cos(t / 3) - a;
                x1 = q * Math.Cos((t + 2 * Math.PI) / 3) - a;
                x2 = q * Math.Cos((t - 2 * Math.PI) / 3) - a;
                return 3;
            }
            else
            {
                A = -Math.Pow(Math.Abs(r) + Math.Sqrt(r2 - q3), 1 / 3.0f);
                if (r < 0)
                    A = -A;
                B = A == 0 ? 0 : q / A;
                a /= 3;
                x0 = (A + B) - a;
                x1 = -0.5f * (A + B) - a;
                x2 = 0.5f * Math.Sqrt(3.0f) * (A - B);
                if (Math.Abs(x2) < 1e-14)
                    return 2;

                return 1;
            }
        }

        private static int SolveCubic(ref double x0, ref double x1, ref double x2, double a, double b, double c, double d)
        {
            if (Math.Abs(a) < 1e-14)
                return SolveQuadratic(ref x0, ref x1, b, c, d);
            return SolveCubicNormed(ref x0, ref x1, ref x2, b / a, c / a, d / a);
        }
    }
}
