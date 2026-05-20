//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

[Flags]
public enum AnimationFlags : byte
{
    None = 0,
    Looping = 1 << 0,
    Additive = 1 << 1
}

public struct AnimationBone
{
    public byte Index;
}

[StructLayout(LayoutKind.Sequential)]
public struct AnimationFrame
{
    public byte Transform0;
    public byte Transform1;
    public float Fraction0;
    public float Fraction1;
}

[StructLayout(LayoutKind.Sequential)]
public struct AnimationTransform
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;

    public static readonly AnimationTransform Identity = new()
    {
        Position = Vector2.Zero,
        Rotation = 0f,
        Scale = Vector2.One
    };

    public static AnimationTransform Lerp(in AnimationTransform a, in AnimationTransform b, float t)
    {
        return new AnimationTransform
        {
            Position = Vector2.Lerp(a.Position, b.Position, t),
            Rotation = MathEx.LerpAngle(a.Rotation, b.Rotation, t),
            Scale = Vector2.Lerp(a.Scale, b.Scale, t)
        };
    }
}

public class Animation : Asset
{
    public int BoneCount { get; private set; }
    public int TransformCount { get; private set; }
    public int FrameCount { get; private set; }
    public int FrameRate { get; private set; }
    public float FrameRateInv { get; private set; }
    public float Duration { get; private set; }
    public AnimationFlags Flags { get; private set; }

    public AnimationBone[] Bones { get; private set; } = [];
    public AnimationTransform[] Transforms { get; private set; } = [];
    public AnimationFrame[] Frames { get; private set; } = [];

    public int StateCount { get; private set; }
    public int[] StateValues { get; private set; } = [];

    public bool IsLooping => (Flags & AnimationFlags.Looping) != 0;
    public bool IsAdditive => (Flags & AnimationFlags.Additive) != 0;

    private Animation(string name) : base(AssetType.Animation, name)
    {
    }

    public Animation() : base(AssetType.Animation) { }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Animation, "Animation", typeof(Animation), Load));
    }

    protected override void Load(BinaryReader reader)
    {
        var boneCount = reader.ReadByte();
        var transformCount = reader.ReadByte();
        var frameCount = reader.ReadByte();
        var frameRate = reader.ReadByte();
        var flags = (AnimationFlags)reader.ReadByte();

        BoneCount = boneCount;
        TransformCount = transformCount;
        FrameCount = frameCount;
        FrameRate = frameRate;
        FrameRateInv = 1f / frameRate;
        Duration = frameCount * FrameRateInv;
        Flags = flags;

        Bones = new AnimationBone[boneCount];
        Transforms = new AnimationTransform[boneCount * transformCount];
        Frames = new AnimationFrame[frameCount + 1];

        for (var i = 0; i < boneCount; i++)
            Bones[i].Index = reader.ReadByte();

        for (var i = 0; i < boneCount * transformCount; i++)
        {
            Transforms[i].Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            Transforms[i].Rotation = reader.ReadSingle();
            Transforms[i].Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }

        for (var i = 0; i < frameCount; i++)
        {
            Frames[i].Transform0 = reader.ReadByte();
            Frames[i].Transform1 = reader.ReadByte();
            Frames[i].Fraction0 = reader.ReadSingle();
            Frames[i].Fraction1 = reader.ReadSingle();
        }

        if (frameCount > 0)
            Frames[frameCount] = Frames[frameCount - 1];

        if (reader.BaseStream.Position < reader.BaseStream.Length)
        {
            StateCount = reader.ReadInt32();
            StateValues = new int[StateCount * frameCount];
            for (var s = 0; s < StateCount; s++)
                for (var f = 0; f < frameCount; f++)
                    StateValues[s * frameCount + f] = reader.ReadInt32();
        }
    }

    public int GetStateValue(int stateIndex, int frameIndex)
    {
        if (stateIndex < 0 || stateIndex >= StateCount || FrameCount == 0) return 0;
        var f = Math.Clamp(frameIndex, 0, FrameCount - 1);
        return StateValues[stateIndex * FrameCount + f];
    }

    private static Animation? Load(Stream stream, string name)
    {
        var animation = new Animation(name);
        var reader = new BinaryReader(stream);
        animation.Load(reader);
        return animation;
    }

    public static Animation Create(
        string name,
        Skeleton skeleton,
        int frameCount,
        int frameRate,
        AnimationTransform[] transforms,
        AnimationFlags flags)
    {
        var animation = new Animation(name)
        {
            BoneCount = skeleton.BoneCount,
            FrameCount = frameCount,
            TransformCount = frameCount,
            FrameRate = frameRate,
            FrameRateInv = 1f / frameRate,
            Flags = flags,
            Transforms = transforms,
            Frames = new AnimationFrame[frameCount + 1]
        };
        animation.Duration = frameCount * animation.FrameRateInv;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            ref var frame = ref animation.Frames[frameIndex];
            frame.Transform0 = (byte)frameIndex;
            frame.Transform1 = (byte)(frameIndex + 1);
            frame.Fraction0 = 0f;
            frame.Fraction1 = 1f;
        }

        if ((flags & AnimationFlags.Looping) != 0)
            animation.Frames[frameCount - 1].Transform1 = 0;
        else
            animation.Frames[frameCount - 1].Transform1 = animation.Frames[frameCount - 1].Transform0;

        animation.Frames[frameCount] = animation.Frames[frameCount - 1];

        return animation;
    }

    public ref AnimationTransform GetTransform(int boneIndex, int transformIndex)
    {
        return ref Transforms[transformIndex * BoneCount + boneIndex];
    }

    public ref AnimationFrame GetFrame(int frameIndex)
    {
        return ref Frames[Math.Clamp(frameIndex, 0, FrameCount)];
    }
}
