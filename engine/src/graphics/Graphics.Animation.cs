//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;
using System.Numerics;

namespace NoZ;

public static partial class Graphics
{
    public static int CurrentBoneIndex => CurrentState.BoneIndex;

    public static void SetBones(Animator animator) =>
        SetBones(animator.Skeleton, animator.BoneTransforms);

    public static void SetBones(Skeleton skeleton, ReadOnlySpan<Matrix3x2> transforms) =>
        SetBones(skeleton.BindPoses.AsReadonlySpan(), transforms);

    public static void SetBones(ReadOnlySpan<Matrix3x2> skeleton, ReadOnlySpan<Matrix3x2> transforms)
    {
        Debug.Assert(skeleton.Length <= Skeleton.MaxBones);
        Debug.Assert(transforms.Length == skeleton.Length);

        // BoneIndex is flat index: row * 64, so vertex bone index + BoneIndex = flat index
        CurrentState.BoneIndex = (ushort)(_boneRow * Skeleton.MaxBones);

        // Write transforms to the current row in _boneData
        // Each bone is 2 texels (8 floats): [M11,M12,M31,0], [M21,M22,M32,0]
        var rowOffset = _boneRow * BoneTextureWidth * 4;
        ref readonly var viewTransform = ref CurrentState.Transform;
        for (var i = 0; i < transforms.Length; i++)
        {
            ref readonly var mm = ref transforms[i];
            var m = skeleton[i] * mm * viewTransform;
            var texelOffset = rowOffset + i * 8;
            // Texel 0: M11, M12, M31, 0
            _boneData[texelOffset + 0] = m.M11;
            _boneData[texelOffset + 1] = m.M12;
            _boneData[texelOffset + 2] = m.M31;
            _boneData[texelOffset + 3] = 0;
            // Texel 1: M21, M22, M32, 0
            _boneData[texelOffset + 4] = m.M21;
            _boneData[texelOffset + 5] = m.M22;
            _boneData[texelOffset + 6] = m.M32;
            _boneData[texelOffset + 7] = 0;
        }

        _boneRow++;
    }
    
    private static void GetPose(
        AnimationState state,
        Span<AnimationTransform> pose,
        ReadOnlySpan<AnimationState> add)
    {
        var animation = state.Animation!;
        var skeleton = animation.Skeleton;        
        state.Sample(pose);

        Span<AnimationTransform> addPose = stackalloc AnimationTransform[skeleton.BoneCount];   
        for (var i = 0; i < add.Length; i++)
        {            
            GetPose(add[i], addPose, default);

            for (int b = 0; b < skeleton.BoneCount; b++)
            {
                ref var baseTransform = ref pose[b];
                ref var addTransform = ref addPose[b];
                baseTransform = new AnimationTransform
                {
                    Position = baseTransform.Position + addTransform.Position,
                    Rotation = baseTransform.Rotation + addTransform.Rotation,
                    Scale = baseTransform.Scale * addTransform.Scale
                };                
            }
        }   
    }

    public static void SetBones(AnimationState state, ReadOnlySpan<AnimationState> add = default)
    {
        if (!state.IsPlaying)
            return;

        var animation = state.Animation!;
        var skeleton = animation.Skeleton;
        Span<AnimationTransform> pose = stackalloc AnimationTransform[skeleton.BoneCount];
        GetPose(state, pose, add);

        Span<Matrix3x2> boneMatrices = stackalloc Matrix3x2[skeleton.BoneCount];
        CalculateBoneMatrices(pose, skeleton, boneMatrices);
        SetBones(skeleton, boneMatrices);
    }

    private static void CalculateBoneMatrices(
        Span<AnimationTransform> pose,
        Skeleton skeleton,
        Span<Matrix3x2> boneTransforms)
    {
        for (var boneIndex = 0; boneIndex < pose.Length; boneIndex++)
        {
            ref var transform = ref pose[boneIndex];

            var position = transform.Position;
            var rotation = transform.Rotation;
            var scale = transform.Scale;

            // if (_boneModifiers != null)
            // {
            //     ref var modifier = ref _boneModifiers[boneIndex];
            //     position += modifier.Position;
            //     rotation += modifier.Rotation;
            //     scale *= modifier.Scale;
            // }

            var localMatrix =
                Matrix3x2.CreateScale(scale) *
                Matrix3x2.CreateRotation(MathEx.Deg2Rad * rotation) *
                Matrix3x2.CreateTranslation(position);

            ref var bone = ref skeleton.GetBone(boneIndex);
            if (bone.ParentIndex >= 0)
                boneTransforms[boneIndex] = localMatrix * boneTransforms[bone.ParentIndex];
            else
                boneTransforms[boneIndex] = localMatrix;
        }
    }
}
