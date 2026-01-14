//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Runtime.InteropServices;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public readonly struct Color32(byte r, byte g, byte b, byte a=255)
    : IEquatable<Color32>
{
    public readonly byte R = r;
    public readonly byte G = g;
    public readonly byte B = b;
    public readonly byte A = a;

    public bool IsTransparent => A == 0;
    public bool IsOpaque => A == 255;

    public Color32 WithAlpha(byte alpha) => new(R, G, B, alpha);
    public Color32 WithAlpha(float alpha) => new(R, G, B, (byte)(alpha * 255f));
    public Color32 MultiplyAlpha(float multiply) => new(R, G, B, (byte)(multiply * A));
    public Color32 MultiplyAlpha(byte multiply) => new(R, G, B, (byte)((multiply / 255f) * (A / 255f) * 255f));

    public Color ToColor() => new(R / 255f, G / 255f, B / 255f, A / 255f);

    public static Color32 Blend(in Color32 a, in Color32 b)
    {
        var alpha = b.A / 255f;
        return new Color32(
            (byte)(a.R + (b.R - a.R) * alpha),
            (byte)(a.G + (b.G - a.G) * alpha),
            (byte)(a.B + (b.B - a.B) * alpha),
            (byte)(a.A + (b.A - a.A) * alpha)
        );
    }

    public bool Equals(Color32 other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color32 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(Color32 left, Color32 right) => left.Equals(right);
    public static bool operator !=(Color32 left, Color32 right) => !left.Equals(right);

    public static explicit operator Color(Color32 c) => c.ToColor();

    public static readonly Color32 Black = new(0, 0, 0);
    public static readonly Color32 White = new(255, 255, 255);
    public static readonly Color32 Red = new(255, 0, 0);
    public static readonly Color32 Green = new(0, 255, 0);
    public static readonly Color32 Blue = new(0, 0, 255);
    public static readonly Color32 Transparent = new(0, 0, 0, 0);
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct Color24(byte r, byte g, byte b) : IEquatable<Color24>
{
    public readonly byte R = r;
    public readonly byte G = g;
    public readonly byte B = b;

    public Color ToColor(float alpha = 1f) => new(R / 255f, G / 255f, B / 255f, alpha);
    public Color32 ToColor32(byte alpha = 255) => new(R, G, B, alpha);

    public bool Equals(Color24 other) => R == other.R && G == other.G && B == other.B;
    public override bool Equals(object? obj) => obj is Color24 other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B);

    public static bool operator ==(Color24 left, Color24 right) => left.Equals(right);
    public static bool operator !=(Color24 left, Color24 right) => !left.Equals(right);

    public static readonly Color24 Black = new(0, 0, 0);
    public static readonly Color24 White = new(255, 255, 255);
    public static readonly Color24 Red = new(255, 0, 0);
    public static readonly Color24 Green = new(0, 255, 0);
    public static readonly Color24 Blue = new(0, 0, 255);
}

[StructLayout(LayoutKind.Sequential)]
public readonly struct Color(float r, float g, float b, float a)
    : IEquatable<Color>
{
    public readonly float R = r;
    public readonly float G = g;
    public readonly float B = b;
    public readonly float A = a;

    public bool IsTransparent => A <= 0f;
    public bool IsOpaque => A >= 1f;

    public Color WithAlpha(float alpha) => new(R, G, B, alpha);
    public Color MultiplyAlpha(float multiply) => new(R, G, B, A * multiply);

    public Color(float r, float g, float b) : this(r, g, b, 1.0f)
    {
    }
    
    public Color(byte r, byte g, byte b, byte a) : this(r / 255f, g / 255f, b / 255f, a / 255f)
    {
    }

    public Color(byte r, byte g, byte b) : this(r / 255f, g / 255f, b / 255f, 1.0f)
    {
    }
    
    public Color Clamped() => new(
        Math.Clamp(R, 0f, 1f),
        Math.Clamp(G, 0f, 1f),
        Math.Clamp(B, 0f, 1f),
        Math.Clamp(A, 0f, 1f)
    );

    public Color ToLinear() => this; // Placeholder for gamma correction

    public Color32 ToColor32() => new(
        (byte)(R * 255f),
        (byte)(G * 255f),
        (byte)(B * 255f),
        (byte)(A * 255f)
    );

    public static implicit operator Color32(Color c) => c.ToColor32();

    public Color24 ToColor24() => new(
        (byte)(R * 255f),
        (byte)(G * 255f),
        (byte)(B * 255f)
    );

    public static Color Mix(in Color a, in Color b, float t) => new(
        a.R + (b.R - a.R) * t,
        a.G + (b.G - a.G) * t,
        a.B + (b.B - a.B) * t,
        a.A + (b.A - a.A) * t
    );

    public static Color FromRgb(uint rgb) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        1.0f
    );

    public static Color FromRgba(uint rgba) => new(
        ((rgba >> 24) & 0xFF) / 255f,
        ((rgba >> 16) & 0xFF) / 255f,
        ((rgba >> 8) & 0xFF) / 255f,
        (rgba & 0xFF) / 255f
    );

    public static Color FromRgb(uint rgb, float alpha) => new(
        ((rgb >> 16) & 0xFF) / 255f,
        ((rgb >> 8) & 0xFF) / 255f,
        (rgb & 0xFF) / 255f,
        alpha
    );

    public static Color FromGrayscale(byte value) => new(value / 255f, value / 255f, value / 255f);

    public bool Equals(Color other) => R == other.R && G == other.G && B == other.B && A == other.A;
    public override bool Equals(object? obj) => obj is Color other && Equals(other);
    public override int GetHashCode() => HashCode.Combine(R, G, B, A);

    public static bool operator ==(Color left, Color right) => left.Equals(right);
    public static bool operator !=(Color left, Color right) => !left.Equals(right);

    public static Color operator +(Color a, Color b) => new(a.R + b.R, a.G + b.G, a.B + b.B, a.A + b.A);
    public static Color operator -(Color a, Color b) => new(a.R - b.R, a.G - b.G, a.B - b.B, a.A - b.A);
    public static Color operator *(Color c, float scalar) => new(c.R * scalar, c.G * scalar, c.B * scalar, c.A * scalar);
    public static Color operator *(float scalar, Color c) => c * scalar;
    public static Color operator *(Color a, Color b) => new(a.R * b.R, a.G * b.G, a.B * b.B, a.A * b.A);

    // Predefined colors
    public static readonly Color Black = new(0f, 0f, 0f);
    public static readonly Color White = new(1f, 1f, 1f);
    public static readonly Color Red = new(1f, 0f, 0f);
    public static readonly Color Green = new(0f, 1f, 0f);
    public static readonly Color Blue = new(0f, 0f, 1f);
    public static readonly Color Yellow = new(1f, 1f, 0f);
    public static readonly Color Purple = new(1f, 0f, 1f);
    public static readonly Color Transparent = new(0f, 0f, 0f, 0f);

    // Alpha variations
    public static readonly Color Black2Pct = new(0f, 0f, 0f, 0.02f);
    public static readonly Color Black5Pct = new(0f, 0f, 0f, 0.05f);
    public static readonly Color Black10Pct = new(0f, 0f, 0f, 0.1f);
    public static readonly Color White1Pct = new(1f, 1f, 1f, 0.01f);
    public static readonly Color White2Pct = new(1f, 1f, 1f, 0.02f);
    public static readonly Color White5Pct = new(1f, 1f, 1f, 0.05f);
    public static readonly Color White10Pct = new(1f, 1f, 1f, 0.1f);
}
