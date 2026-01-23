//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct SkeletonBoneTransform
{
    public Vector2 Position;
    public float Rotation;
    public Vector2 Scale;

    public static readonly SkeletonBoneTransform Identity = new()
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
    public SkeletonBoneTransform Transform;
    public Matrix3x2 BindPose;
}

public class Skeleton : Asset
{
    public int BoneCount { get; private set; }
    public Bone[] Bones { get; private set; } = [];

    private Skeleton(string name) : base(AssetType.Skeleton, name)
    {
    }

    public static Skeleton Load(BinaryReader reader, string name, string[] nameTable)
    {
        var skeleton = new Skeleton(name);

        var boneCount = reader.ReadByte();
        skeleton.BoneCount = boneCount;
        skeleton.Bones = new Bone[boneCount];

        for (var i = 0; i < boneCount; i++)
        {
            ref var bone = ref skeleton.Bones[i];
            bone.Name = nameTable[i];
            bone.Index = i;
            bone.ParentIndex = reader.ReadSByte();
            bone.Transform.Position = new Vector2(reader.ReadSingle(), reader.ReadSingle());
            bone.Transform.Rotation = reader.ReadSingle();
            bone.Transform.Scale = new Vector2(reader.ReadSingle(), reader.ReadSingle());

            bone.BindPose = new Matrix3x2(
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

    public ref Matrix3x2 GetBindPose(int boneIndex)
    {
        return ref Bones[boneIndex].BindPose;
    }
}
