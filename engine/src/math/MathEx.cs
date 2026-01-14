//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public static class MathEx
{
    // Constants
    public const float Pi = MathF.PI;
    public const float TwoPi = MathF.PI * 2f;
    public const float HalfPi = MathF.PI * 0.5f;
    public const float Deg2Rad = MathF.PI / 180f;
    public const float Rad2Deg = 180f / MathF.PI;
    public const float Epsilon = 1.192092896e-07f;

    // Size constants
    public const int MB = 1024 * 1024;
    public const long GB = 1024L * 1024L * 1024L;

    // Power of 2
    public static uint NextPowerOf2(uint n)
    {
        if (n <= 1) return 1;
        n--;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        return n + 1;
    }

    public static ulong NextPowerOf2(ulong n)
    {
        if (n <= 1) return 2;
        n--;
        n |= n >> 1;
        n |= n >> 2;
        n |= n >> 4;
        n |= n >> 8;
        n |= n >> 16;
        n |= n >> 32;
        return n + 1;
    }

    public static int FloorToInt(float v) => (int)MathF.Floor(v);
    public static int CeilToInt(float v) => (int)MathF.Ceiling(v);
    public static int RoundToInt(float v) => (int)(v + 0.5f);
    public static float Lerp(float a, float b, float t) => a + (b - a) * t;
    public static double Lerp(double a, double b, double t) => a + (b - a) * t;

    public static float InverseLerp(float a, float b, float value)
    {
        if (MathF.Abs(b - a) < Epsilon) return 0f;
        return (value - a) / (b - a);
    }

    public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        var t = InverseLerp(fromMin, fromMax, value);
        return Lerp(toMin, toMax, t);
    }

    // Clamping
    public static float Clamp01(float v) => Math.Clamp(v, 0f, 1f);

    // Comparison
    public static bool Approximately(float a, float b, float epsilon = 1e-6f) => MathF.Abs(a - b) <= epsilon;
    public static bool Approximately(double a, double b, double epsilon = 1e-6) => Math.Abs(a - b) <= epsilon;

    // Misc
    public static float Sqr(float x) => x * x;
    public static double Sqr(double x) => x * x;

    public static float Repeat(float t, float length)
    {
        return Math.Clamp(t - MathF.Floor(t / length) * length, 0f, length);
    }

    public static float PingPong(float t, float length)
    {
        t = Repeat(t, length * 2f);
        return length - MathF.Abs(t - length);
    }

    // Angle utilities
    public static float Radians(float degrees) => degrees * Deg2Rad;
    public static float Degrees(float radians) => radians * Rad2Deg;

    public static float NormalizeAngle(float angle)
    {
        angle = Repeat(angle, 360f);
        return angle;
    }

    public static float NormalizeAngle180(float angle)
    {
        angle = NormalizeAngle(angle);
        if (angle > 180f) angle -= 360f;
        return angle;
    }

    public static float DeltaAngle(float current, float target)
    {
        var delta = Repeat(target - current, 360f);
        if (delta > 180f) delta -= 360f;
        return delta;
    }

    public static float LerpAngle(float a, float b, float t)
    {
        var delta = DeltaAngle(a, b);
        return a + delta * t;
    }

    // Easing functions
    public static float EaseInQuadratic(float t) => t * t;
    public static float EaseOutQuadratic(float t) => 1f - (1f - t) * (1f - t);
    public static float EaseInOutQuadratic(float t)
    {
        if (t < 0.5f) return 2f * t * t;
        var f = 2f * t - 1f;
        return -0.5f * (f * (f - 2f) - 1f);
    }

    public static float EaseInCubic(float t) => t * t * t;
    public static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);
    public static float EaseInOutCubic(float t)
    {
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    public static float SmoothStep(float a, float b, float t)
    {
        t = Clamp01(t);
        t = t * t * (3f - 2f * t);
        return Lerp(a, b, t);
    }

    public static float SmootherStep(float a, float b, float t)
    {
        t = Clamp01(t);
        t = t * t * t * (t * (t * 6f - 15f) + 10f);
        return Lerp(a, b, t);
    }

    public static float SmoothDamp(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        smoothTime = MathF.Max(0.0001f, smoothTime);
        var omega = 2f / smoothTime;

        var x = omega * deltaTime;
        var exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);

        var change = current - target;
        var originalTo = target;

        var maxChange = maxSpeed * smoothTime;
        change = Math.Clamp(change, -maxChange, maxChange);
        target = current - change;

        var temp = (currentVelocity + omega * change) * deltaTime;
        currentVelocity = (currentVelocity - omega * temp) * exp;

        var output = target + (change + temp) * exp;

        // Prevent overshooting
        if (originalTo - current > 0f == output > originalTo)
        {
            output = originalTo;
            currentVelocity = (output - originalTo) / deltaTime;
        }

        return output;
    }

    public static float SmoothDamp(
        float current,
        float target,
        ref float currentVelocity,
        float smoothTime,
        float deltaTime)
    {
        return SmoothDamp(current, target, ref currentVelocity, smoothTime, float.PositiveInfinity, deltaTime);
    }

    public static uint FourCC(byte a, byte b, byte c, byte d)
    {
        return (uint)d | ((uint)c << 8) | ((uint)b << 16) | ((uint)a << 24);
    }

    public static uint FourCC(char a, char b, char c, char d)
    {
        return FourCC((byte)a, (byte)b, (byte)c, (byte)d);
    }
}
