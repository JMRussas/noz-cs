//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using System.Text;

namespace NoZ;

public enum VfxCurveType : byte
{
    Linear = 0,
    Quadratic,
    Cubic,
    Quartic,
    Sine,
    Back,
    Elastic,
    Bounce,
    Bell,
}

public enum VfxEaseType : byte
{
    None = 0,
    In,
    Out,
    InOut,
}

[InlineArray(Samples)]
public struct VfxCurveLut
{
    public const int Samples = 32;
    private float _element0;

    public static VfxCurveLut Linear
    {
        get
        {
            var lut = new VfxCurveLut();
            for (var i = 0; i < Samples; i++)
                lut[i] = i / (float)(Samples - 1);
            return lut;
        }
    }
}

public struct VfxRange
{
    public float Min;
    public float Max;

    public VfxRange(float min, float max) { Min = min; Max = max; }
    public VfxRange(float value) { Min = value; Max = value; }

    public static readonly VfxRange Zero = new(0, 0);
    public static readonly VfxRange One = new(1, 1);

    public static bool operator ==(VfxRange a, VfxRange b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxRange a, VfxRange b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxRange r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxIntRange
{
    public int Min;
    public int Max;

    public VfxIntRange(int min, int max) { Min = min; Max = max; }
    public VfxIntRange(int value) { Min = value; Max = value; }

    public static readonly VfxIntRange Zero = new(0, 0);

    public static bool operator ==(VfxIntRange a, VfxIntRange b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxIntRange a, VfxIntRange b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxIntRange r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxColorRange
{
    public Color Min;
    public Color Max;

    public VfxColorRange(Color min, Color max) { Min = min; Max = max; }
    public VfxColorRange(Color value) { Min = value; Max = value; }

    public static readonly VfxColorRange White = new(Color.White, Color.White);

    public static bool operator ==(VfxColorRange a, VfxColorRange b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxColorRange a, VfxColorRange b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxColorRange r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxVec2Range
{
    public Vector2 Min;
    public Vector2 Max;

    public VfxVec2Range(Vector2 min, Vector2 max) { Min = min; Max = max; }
    public VfxVec2Range(Vector2 value) { Min = value; Max = value; }

    public static readonly VfxVec2Range Zero = new(Vector2.Zero, Vector2.Zero);

    public static bool operator ==(VfxVec2Range a, VfxVec2Range b) => a.Min == b.Min && a.Max == b.Max;
    public static bool operator !=(VfxVec2Range a, VfxVec2Range b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxVec2Range r && this == r;
    public override int GetHashCode() => HashCode.Combine(Min, Max);
}

public struct VfxFloatCurve
{
    public VfxRange Start;
    public VfxRange End;
    public VfxCurveLut Lut;

    public static readonly VfxFloatCurve Zero = new() { Start = VfxRange.Zero, End = VfxRange.Zero };
    public static readonly VfxFloatCurve One = new() { Start = VfxRange.One, End = VfxRange.One };
}

public struct VfxColorCurve
{
    public VfxColorRange Start;
    public VfxColorRange End;
    public VfxCurveLut Lut;

    public static readonly VfxColorCurve White = new() { Start = VfxColorRange.White, End = VfxColorRange.White };
}

public enum VfxSpawnShape : byte
{
    Point = 0,
    Circle = 1,
    Box = 2,
}

public enum VfxFrameMode : byte
{
    Time = 0,
    Random = 1,
}

public struct VfxSpawnCircle
{
    public float Radius;
    public float InnerRadius;
}

public struct VfxSpawnBox
{
    public Vector2 Size;
    public Vector2 InnerSize;
    public float Rotation;
}

[StructLayout(LayoutKind.Explicit)]
public struct VfxSpawnDef
{
    [FieldOffset(0)]  public VfxSpawnShape Shape;
    [FieldOffset(4)]  public Vector2 Offset;
    [FieldOffset(12)] public VfxSpawnCircle Circle;
    [FieldOffset(12)] public VfxSpawnBox Box;

    public static readonly VfxSpawnDef Default = new() { Shape = VfxSpawnShape.Point };
}

public struct VfxParticleDef
{
    public VfxFloatCurve Gravity;
    public VfxRange Duration;
    public VfxFloatCurve Size;
    public VfxFloatCurve Speed;
    public VfxColorCurve Color;
    public VfxFloatCurve Opacity;
    public VfxRange Rotation;
    public VfxFloatCurve RotationSpeed;
    public bool AlignToDirection;
    public VfxFrameMode FrameMode;
    public Sprite? Sprite;
    public ushort Sort;
}

public struct VfxEmitterDef
{
    public VfxFloatCurve Rate;
    public VfxIntRange Burst;
    public VfxRange Duration;
    public VfxSpawnDef Spawn;
    public float Direction;
    public float Spread;
    public float Radial;
    public bool WorldSpace;
    public VfxParticleDef Particle;
}

public class Vfx : Asset
{
    internal const ushort Version = 14;

    public VfxRange Duration { get; internal set; }
    public VfxEmitterDef[] EmitterDefs { get; internal set; } = [];
    public Rect Bounds { get; internal set; }
    public bool Loop { get; internal set; }

    internal Vfx(string name) : base(AssetType.Vfx, name) { }
    public Vfx() : base(AssetType.Vfx) { }

    protected override void Load(BinaryReader reader)
    {
        Bounds = new Rect(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());

        Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
        Loop = reader.ReadBoolean();

        var emitterCount = reader.ReadInt32();
        EmitterDefs = new VfxEmitterDef[emitterCount];

        for (var i = 0; i < emitterCount; i++)
        {
            ref var e = ref EmitterDefs[i];

            e.Rate = ReadFloatCurve(reader);
            e.Burst = new VfxIntRange(reader.ReadInt32(), reader.ReadInt32());
            e.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            e.Spawn = ReadSpawnDef(reader);
            e.Direction = reader.ReadSingle();
            e.Spread = reader.ReadSingle();
            e.Radial = reader.ReadSingle();
            e.WorldSpace = reader.ReadBoolean();

            ref var p = ref e.Particle;
            p.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.Size = ReadFloatCurve(reader);
            p.Speed = ReadFloatCurve(reader);
            p.Color = ReadColorCurve(reader);
            p.Opacity = ReadFloatCurve(reader);
            p.Gravity = ReadFloatCurve(reader);
            p.Rotation = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.RotationSpeed = ReadFloatCurve(reader);
            p.AlignToDirection = reader.ReadBoolean();
            p.FrameMode = (VfxFrameMode)reader.ReadByte();

            var spriteNameLen = reader.ReadInt32();
            if (spriteNameLen > 0)
            {
                var spriteName = Encoding.UTF8.GetString(reader.ReadBytes(spriteNameLen));
                p.Sprite = Asset.Load(AssetType.Sprite, spriteName) as Sprite;
            }
            p.Sprite ??= Asset.Load(AssetType.Sprite, "square") as Sprite;
            p.Sort = reader.ReadUInt16();
        }
    }

    private static Asset Load(Stream stream, string name)
    {
        var vfx = new Vfx(name);
        using var reader = new BinaryReader(stream);
        vfx.Load(reader);
        return vfx;
    }

    private static VfxSpawnDef ReadSpawnDef(BinaryReader reader)
    {
        var def = new VfxSpawnDef();
        def.Shape = (VfxSpawnShape)reader.ReadByte();
        def.Offset = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        switch (def.Shape)
        {
            case VfxSpawnShape.Circle:
                def.Circle.Radius = reader.ReadSingle();
                def.Circle.InnerRadius = reader.ReadSingle();
                break;
            case VfxSpawnShape.Box:
                def.Box.Size = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                def.Box.InnerSize = new Vector2(reader.ReadSingle(), reader.ReadSingle());
                def.Box.Rotation = reader.ReadSingle();
                break;
        }
        return def;
    }

    private static VfxFloatCurve ReadFloatCurve(BinaryReader reader)
    {
        var curve = new VfxFloatCurve
        {
            Start = new VfxRange(reader.ReadSingle(), reader.ReadSingle()),
            End = new VfxRange(reader.ReadSingle(), reader.ReadSingle()),
        };
        for (var i = 0; i < VfxCurveLut.Samples; i++)
            curve.Lut[i] = reader.ReadSingle();
        return curve;
    }

    private static VfxColorCurve ReadColorCurve(BinaryReader reader)
    {
        var curve = new VfxColorCurve
        {
            Start = new VfxColorRange(
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())),
            End = new VfxColorRange(
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())),
        };
        for (var i = 0; i < VfxCurveLut.Samples; i++)
            curve.Lut[i] = reader.ReadSingle();
        return curve;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Vfx, "Vfx", typeof(Vfx), Load, Version));
    }
}
