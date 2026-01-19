//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace NoZ.Engine.UI;

public enum SizeMode : byte
{
    Default,
    Inherit,
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

    public static implicit operator Size(float value) => new Size(value);

    public static Size Inherit(float value=1.0f) => new() { Value = value, Mode = SizeMode.Inherit };

    public static readonly Size Default = new();

    public override string ToString() => Mode switch
    {
        SizeMode.Default => "Default",
        SizeMode.Inherit => $"Inherit({Value})",
        SizeMode.Fixed => $"Fixed({Value})",
        SizeMode.Fit => "Fit",
        _ => "Unknown"
    };
}
