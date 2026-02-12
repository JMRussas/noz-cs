//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;

namespace NoZ;

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
    public static float Mix(float a, float b, float t) => a + (b - a) * t;
    public static double Mix(double a, double b, double t) => a + (b - a) * t;

    public static float InverseMix(float a, float b, float value)
    {
        if (MathF.Abs(b - a) < Epsilon) return 0f;
        return (value - a) / (b - a);
    }

    public static float Remap(float value, float fromMin, float fromMax, float toMin, float toMax)
    {
        var t = InverseMix(fromMin, fromMax, value);
        return Mix(toMin, toMax, t);
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
    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInQuadratic(float t) => t * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseOutQuadratic(float t) => 1f - (1f - t) * (1f - t);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInOutQuadratic(float t)
    {
        if (t < 0.5f) return 2f * t * t;
        var f = 2f * t - 1f;
        return -0.5f * (f * (f - 2f) - 1f);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInCubic(float t) => t * t * t;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseOutCubic(float t) => 1f - MathF.Pow(1f - t, 3f);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInOutCubic(float t)
    {
        return t < 0.5f
            ? 4f * t * t * t
            : 1f - MathF.Pow(-2f * t + 2f, 3f) / 2f;
    }

    private const float BackOvershoot = 1.70158f;

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInBack(float t) => t * t * ((BackOvershoot + 1f) * t - BackOvershoot);

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseOutBack(float t)
    {
        var u = t - 1f;
        return u * u * ((BackOvershoot + 1f) * u + BackOvershoot) + 1f;
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float EaseInOutBack(float t)
    {
        const float c = BackOvershoot * 1.525f;
        return t < 0.5f
            ? 2f * t * t * ((c + 1f) * 2f * t - c)
            : 1f + 2f * (t - 1f) * (t - 1f) * ((c + 1f) * 2f * (t - 1f) + c);
    }

    private delegate float EasingDelegate(float t);

    private static readonly EasingDelegate[] EasingFunctions =
    [
        t => t,                 // None
        EaseInQuadratic,        // QuadIn
        EaseOutQuadratic,       // QuadOut
        EaseInOutQuadratic,     // QuadInOut
        EaseInCubic,            // CubicIn
        EaseOutCubic,           // CubicOut
        EaseInOutCubic,         // CubicInOut
        EaseInBack,             // BackIn
        EaseOutBack,            // BackOut
        EaseInOutBack,          // BackInOut
    ];

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    public static float Ease(Easing easing, float t) => EasingFunctions[(int)easing](t);

    public static float SmoothStep(float t) => t * t * (3f - 2f * t);

    public static float SmoothStep(float a, float b, float t)
    {
        t = Clamp01(t);
        t = SmoothStep(t);
        return Mix(a, b, t);
    }

    public static float SmootherStep(float a, float b, float t)
    {
        t = Clamp01(t);
        t = t * t * t * (t * (t * 6f - 15f) + 10f);
        return Mix(a, b, t);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static void SmoothDampCoefficients(float smoothTime, float deltaTime, out float omega, out float exp)
    {
        smoothTime = MathF.Max(0.0001f, smoothTime);
        omega = 2f / smoothTime;
        var x = omega * deltaTime;
        exp = 1f / (1f + x + 0.48f * x * x + 0.235f * x * x * x);
    }

    [MethodImpl(MethodImplOptions.AggressiveInlining)]
    private static float SmoothDampStep(float change, float target, ref float velocity, float omega, float exp, float deltaTime)
    {
        var temp = (velocity + omega * change) * deltaTime;
        velocity = (velocity - omega * temp) * exp;
        return target + (change + temp) * exp;
    }

    public static Vector2 SmoothDamp(
        Vector2 current,
        Vector2 target,
        ref Vector2 currentVelocity,
        float smoothTime,
        float maxSpeed,
        float deltaTime)
    {
        SmoothDampCoefficients(smoothTime, deltaTime, out var omega, out var exp);
        var change = current - target;
        var originalTo = target;
        var maxChange = maxSpeed * smoothTime;
        if (change.LengthSquared() > maxChange * maxChange)
            change = Vector2.Normalize(change) * maxChange;
        target = current - change;
        var output = new Vector2(
            SmoothDampStep(change.X, target.X, ref currentVelocity.X, omega, exp, deltaTime),
            SmoothDampStep(change.Y, target.Y, ref currentVelocity.Y, omega, exp, deltaTime));
        if (Vector2.Dot(originalTo - current, output - originalTo) > 0f)
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
        float maxSpeed,
        float deltaTime)
    {
        SmoothDampCoefficients(smoothTime, deltaTime, out var omega, out var exp);
        var change = current - target;
        var originalTo = target;
        var maxChange = maxSpeed * smoothTime;
        change = Math.Clamp(change, -maxChange, maxChange);
        target = current - change;
        var output = SmoothDampStep(change, target, ref currentVelocity, omega, exp, deltaTime);
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

    public static float DistanceFromLineSegment(Vector2 lineStart, Vector2 lineEnd, Vector2 point)
    {
        var line = lineEnd - lineStart;
        var lengthSqr = line.LengthSquared();
        if (lengthSqr < 0.0001f)
            return Vector2.Distance(point, lineStart);

        var t = MathF.Max(0, MathF.Min(1, Vector2.Dot(point - lineStart, line) / lengthSqr));
        var projection = lineStart + t * line;
        return Vector2.Distance(point, projection);
    }

    public static float RandomRange(float min, float max) =>
        ((float)Random.Shared.NextDouble()) * (max - min) + min;
}
