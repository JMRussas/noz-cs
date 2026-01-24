//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

[Flags]
public enum AnimationFlags : byte
{
    None = 0,
    Looping = 1 << 0,
    RootMotion = 1 << 1
}

public struct AnimationBone
{
    public byte Index;
}

public struct AnimationFrame
{
    public byte Transform0;
    public byte Transform1;
    public byte Event;
    public float Fraction0;
    public float Fraction1;
    public float RootMotion0;
    public float RootMotion1;
}

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
            Rotation = a.Rotation + (b.Rotation - a.Rotation) * t,
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

    public bool IsLooping => (Flags & AnimationFlags.Looping) != 0;
    public bool IsRootMotion => (Flags & AnimationFlags.RootMotion) != 0;

    private Animation(string name) : base(AssetType.Animation, name)
    {
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Animation, typeof(Animation), Load));
    }

    private static Animation? Load(Stream stream, string name)
    {
        var reader = new BinaryReader(stream);
        var animation = new Animation(name);

        var boneCount = reader.ReadByte();
        var transformCount = reader.ReadByte();
        var frameCount = reader.ReadByte();
        var frameRate = reader.ReadByte();
        var flags = (AnimationFlags)reader.ReadByte();

        animation.BoneCount = boneCount;
        animation.TransformCount = transformCount;
        animation.FrameCount = frameCount;
        animation.FrameRate = frameRate;
        animation.FrameRateInv = 1f / frameRate;
        animation.Duration = frameCount * animation.FrameRateInv;
        animation.Flags = flags;

        animation.Bones = new AnimationBone[boneCount];
        animation.Transforms = new AnimationTransform[boneCount * transformCount];
        animation.Frames = new AnimationFrame[frameCount + 1];

        for (var i = 0; i < boneCount; i++)
            animation.Bones[i].Index = reader.ReadByte();

        for (var i = 0; i < boneCount * transformCount; i++)
        {
            animation.Transforms[i].Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            animation.Transforms[i].Rotation = reader.ReadSingle();
            animation.Transforms[i].Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());
        }

        for (var i = 0; i < frameCount; i++)
        {
            animation.Frames[i].Event = reader.ReadByte();
            animation.Frames[i].Transform0 = reader.ReadByte();
            animation.Frames[i].Transform1 = reader.ReadByte();
            animation.Frames[i].Fraction0 = reader.ReadSingle();
            animation.Frames[i].Fraction1 = reader.ReadSingle();
            animation.Frames[i].RootMotion0 = reader.ReadSingle();
            animation.Frames[i].RootMotion1 = reader.ReadSingle();
        }

        animation.Frames[frameCount] = animation.Frames[frameCount - 1];

        return animation;
    }

    public static Animation Create(
        string name,
        Skeleton skeleton,
        int frameCount,
        int frameRate,
        AnimationTransform[] transforms,
        AnimationEvent[]? events,
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

        var boneStride = animation.BoneCount;

        for (var frameIndex = 0; frameIndex < frameCount; frameIndex++)
        {
            ref var frame = ref animation.Frames[frameIndex];
            frame.Transform0 = (byte)frameIndex;
            frame.Transform1 = (byte)(frameIndex + 1);
            frame.Fraction0 = 0f;
            frame.Fraction1 = 1f;
            frame.Event = 0;
            frame.RootMotion0 = 0f;
            frame.RootMotion1 = transforms[frameIndex * boneStride].Position.X;

            transforms[frameIndex * boneStride].Position = new Vector2(0, transforms[frameIndex * boneStride].Position.Y);

            if (events != null)
            {
                for (var eventIndex = 0; eventIndex < events.Length; eventIndex++)
                {
                    if (events[eventIndex].Frame == frameIndex)
                    {
                        frame.Event = events[eventIndex].Id;
                        break;
                    }
                }
            }
        }

        if ((flags & AnimationFlags.Looping) != 0)
            animation.Frames[frameCount - 1].Transform1 = 0;
        else
            animation.Frames[frameCount - 1].Transform1 = animation.Frames[frameCount - 1].Transform0;

        animation.Frames[frameCount] = animation.Frames[frameCount - 1];

        return animation;
    }

    public void AddEvent(int frame, byte eventId)
    {
        if (frame < 0 || frame >= FrameCount)
            return;
        Frames[frame].Event = eventId;
    }

    public void AddEvents(AnimationEvent[] events)
    {
        foreach (var e in events)
        {
            if (e.Frame < 0 || e.Frame >= FrameCount)
                continue;
            Frames[e.Frame].Event = e.Id;
        }
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

public struct AnimationEvent
{
    public int Frame;
    public byte Id;
}
