//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public enum VfxCurveType : byte
{
    Linear = 0,
    EaseIn,
    EaseOut,
    EaseInOut,
    Quadratic,
    Cubic,
    Sine,
    CubicBezier,
    Bell
}

public struct VfxRange
{
    public float Min;
    public float Max;

    public VfxRange(float min, float max) { Min = min; Max = max; }
    public VfxRange(float value) { Min = value; Max = value; }

    public static readonly VfxRange Zero = new(0, 0);
    public static readonly VfxRange One = new(1, 1);
}

public struct VfxIntRange
{
    public int Min;
    public int Max;

    public VfxIntRange(int min, int max) { Min = min; Max = max; }
    public VfxIntRange(int value) { Min = value; Max = value; }

    public static readonly VfxIntRange Zero = new(0, 0);
}

public struct VfxColorRange
{
    public Color Min;
    public Color Max;

    public VfxColorRange(Color min, Color max) { Min = min; Max = max; }
    public VfxColorRange(Color value) { Min = value; Max = value; }

    public static readonly VfxColorRange White = new(Color.White, Color.White);
}

public struct VfxVec2Range
{
    public Vector2 Min;
    public Vector2 Max;

    public VfxVec2Range(Vector2 min, Vector2 max) { Min = min; Max = max; }
    public VfxVec2Range(Vector2 value) { Min = value; Max = value; }

    public static readonly VfxVec2Range Zero = new(Vector2.Zero, Vector2.Zero);
}

public struct VfxFloatCurve
{
    public VfxCurveType Type;
    public VfxRange Start;
    public VfxRange End;
    public Vector4 Bezier; // (x1, y1, x2, y2) control points, only used when Type == CubicBezier

    public static readonly VfxFloatCurve Zero = new() { Type = VfxCurveType.Linear, Start = VfxRange.Zero, End = VfxRange.Zero };
    public static readonly VfxFloatCurve One = new() { Type = VfxCurveType.Linear, Start = VfxRange.One, End = VfxRange.One };
}

public struct VfxColorCurve
{
    public VfxCurveType Type;
    public VfxColorRange Start;
    public VfxColorRange End;
    public Vector4 Bezier;

    public static readonly VfxColorCurve White = new() { Type = VfxCurveType.Linear, Start = VfxColorRange.White, End = VfxColorRange.White };
}

public struct VfxParticleDef
{
    public VfxVec2Range Gravity;
    public VfxRange Duration;
    public VfxRange Drag;
    public VfxFloatCurve Size;
    public VfxFloatCurve Speed;
    public VfxColorCurve Color;
    public VfxFloatCurve Opacity;
    public VfxFloatCurve Rotation;
}

public struct VfxEmitterDef
{
    public VfxIntRange Rate;
    public VfxIntRange Burst;
    public VfxRange Duration;
    public VfxRange Angle;
    public VfxVec2Range Spawn;
    public VfxVec2Range Direction;
    public bool WorldSpace;
    public VfxParticleDef Particle;
}

public class Vfx : Asset
{
    internal const ushort Version = 4;

    public VfxRange Duration { get; internal set; }
    public VfxEmitterDef[] EmitterDefs { get; internal set; } = [];
    public Rect Bounds { get; internal set; }
    public bool Loop { get; internal set; }

    internal Vfx(string name) : base(AssetType.Vfx, name) { }

    private static Asset Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var vfx = new Vfx(name);

        // Bounds
        vfx.Bounds = new Rect(
            reader.ReadSingle(), reader.ReadSingle(),
            reader.ReadSingle(), reader.ReadSingle());

        // Duration
        vfx.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
        vfx.Loop = reader.ReadBoolean();

        // Emitters
        var emitterCount = reader.ReadInt32();
        vfx.EmitterDefs = new VfxEmitterDef[emitterCount];

        for (var i = 0; i < emitterCount; i++)
        {
            ref var e = ref vfx.EmitterDefs[i];

            e.Rate = new VfxIntRange(reader.ReadInt32(), reader.ReadInt32());
            e.Burst = new VfxIntRange(reader.ReadInt32(), reader.ReadInt32());
            e.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            e.Angle = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            e.Spawn = new VfxVec2Range(
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            e.Direction = new VfxVec2Range(
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            e.WorldSpace = reader.ReadBoolean();

            // Particle def
            ref var p = ref e.Particle;
            p.Duration = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.Size = ReadFloatCurve(reader);
            p.Speed = ReadFloatCurve(reader);
            p.Color = ReadColorCurve(reader);
            p.Opacity = ReadFloatCurve(reader);
            p.Gravity = new VfxVec2Range(
                new Vector2(reader.ReadSingle(), reader.ReadSingle()),
                new Vector2(reader.ReadSingle(), reader.ReadSingle()));
            p.Drag = new VfxRange(reader.ReadSingle(), reader.ReadSingle());
            p.Rotation = ReadFloatCurve(reader);

            // mesh name (skip - not used for flat quads)
            var meshNameLen = reader.ReadInt32();
            if (meshNameLen > 0)
                reader.ReadBytes(meshNameLen);
        }

        return vfx;
    }

    private static VfxFloatCurve ReadFloatCurve(BinaryReader reader)
    {
        var curve = new VfxFloatCurve
        {
            Type = (VfxCurveType)reader.ReadByte(),
            Start = new VfxRange(reader.ReadSingle(), reader.ReadSingle()),
            End = new VfxRange(reader.ReadSingle(), reader.ReadSingle())
        };
        if (curve.Type == VfxCurveType.CubicBezier)
            curve.Bezier = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return curve;
    }

    private static VfxColorCurve ReadColorCurve(BinaryReader reader)
    {
        var curve = new VfxColorCurve
        {
            Type = (VfxCurveType)reader.ReadByte(),
            Start = new VfxColorRange(
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle())),
            End = new VfxColorRange(
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()),
                new Color(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle()))
        };
        if (curve.Type == VfxCurveType.CubicBezier)
            curve.Bezier = new Vector4(reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle(), reader.ReadSingle());
        return curve;
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Vfx, "Vfx", typeof(Vfx), Load, Version));
    }
}
