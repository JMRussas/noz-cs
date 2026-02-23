//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Runtime.InteropServices;

namespace NoZ;

[StructLayout(LayoutKind.Sequential)]
public struct BoneTransform
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;

    public static readonly BoneTransform Identity = new()
    {
        Position = Vector2.Zero,
        Rotation = 0f,
        Scale = Vector2.One
    };
}

public struct Bone
{
    public string Name;
    public int Index;
    public int ParentIndex;
    public BoneTransform Transform;
}

public class Skeleton : Asset
{
    public const int MaxBones = 64;

    public int BoneCount { get; private set; }
    public Bone[] Bones { get; private set; } = [];
    public NativeArray<Matrix3x2> BindPoses { get; private set; }

    private Skeleton(string name) : base(AssetType.Skeleton, name)
    {
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Skeleton, "Skeleton", typeof(Skeleton), Load));
    }

    private static Skeleton? Load(Stream stream, string name)
    {
        var reader = new BinaryReader(stream);
        var skeleton = new Skeleton(name);

        var boneCount = reader.ReadByte();
        skeleton.BoneCount = boneCount;
        skeleton.Bones = new Bone[boneCount];

        skeleton.BindPoses = new NativeArray<Matrix3x2>(boneCount, boneCount);

        for (var i = 0; i < boneCount; i++)
        {
            ref var bone = ref skeleton.Bones[i];
            bone.Name = reader.ReadString();
            bone.Index = i;
            bone.ParentIndex = reader.ReadSByte();
            bone.Transform.Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            bone.Transform.Rotation = reader.ReadSingle();
            bone.Transform.Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());

            skeleton.BindPoses[i] = new Matrix3x2(
                reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle(),
                reader.ReadSingle(), reader.ReadSingle()
            );
        }

        return skeleton;
    }

    public static Skeleton Create(string name, Bone[] bones)
    {
        return new Skeleton(name)
        {
            BoneCount = bones.Length,
            Bones = bones
        };
    }

    public int GetBoneIndex(string name)
    {
        for (var i = 0; i < BoneCount; i++)
        {
            if (Bones[i].Name == name)
                return i;
        }
        return 0;
    }

    public ref Bone GetBone(int boneIndex)
    {
        return ref Bones[boneIndex];
    }
}
