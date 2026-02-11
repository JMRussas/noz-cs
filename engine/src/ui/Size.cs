//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ;

public enum SizeMode : byte
{
    Default,
    Percent,
    Fixed,
    Fit
}

public struct Size
{
    public float Value = 0.0f;
    public SizeMode Mode = SizeMode.Default;

    public Size()
    {
    }

    public Size(float value)
    {
        Value = value;
        Mode = SizeMode.Fixed;
    }

    public static implicit operator Size(float value) => new(value);

    public readonly bool IsFixed => Mode == SizeMode.Fixed;
    public readonly bool IsPercent => Mode == SizeMode.Percent;
    public readonly bool IsFit => Mode == SizeMode.Fit;

    public static Size Percent(float value=1.0f) => new() { Value = value, Mode = SizeMode.Percent };
    public static readonly Size Fit = new() { Value = 0.0f, Mode = SizeMode.Fit };

    public static readonly Size Default = new();

    public override string ToString() => Mode switch
    {
        SizeMode.Default => "Default",
        SizeMode.Percent => $"Percent({Value})",
        SizeMode.Fixed => $"Fixed({Value})",
        SizeMode.Fit => "Fit",
        _ => "Unknown"
    };
}

public struct Size2(Size width, Size height)
{
    public Size Width = width;
    public Size Height = height;

    // index opertaor
    public unsafe Size this[int index]
    {
        get
        {
            fixed (Size* ptr = &Width)
                return ptr[index];
        }
        set
        {
            fixed (Size* ptr = &Width)
                ptr[index] = value;
        }
    }

    public static implicit operator Size2(float value) => new(value, value);
    public static implicit operator Size2(Size size) => new(size, size);
    public override string ToString() => $"Width: {Width}, Height: {Height}";

    public static readonly Size2 Default = new(Size.Default, Size.Default);
    public static readonly Size2 Fit = new(Size.Fit, Size.Fit);
}
