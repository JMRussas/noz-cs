//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public class Animator
{
    private readonly Skeleton _skeleton;
    private readonly Matrix3x2[] _boneTransforms;
    private readonly AnimationTransform[] _poseA;
    private readonly AnimationTransform[] _poseB;

    private Animation? _animation;
    private Animation? _blendFromAnimation;
    private float _time;
    private float _blendFromTime;
    private float _blendDuration;
    private float _blendElapsed;

    public Skeleton Skeleton => _skeleton;
    public Animation? CurrentAnimation => _animation;
    public float Time => _time;
    public float NormalizedTime => _animation != null && _animation.Duration > 0 ? _time / _animation.Duration : 0f;
    public bool IsPlaying => _animation != null;
    public bool IsBlending => _blendFromAnimation != null && _blendElapsed < _blendDuration;
    public ReadOnlySpan<Matrix3x2> BoneTransforms => _boneTransforms;

    public Animator(Skeleton skeleton)
    {
        _skeleton = skeleton;
        _boneTransforms = new Matrix3x2[skeleton.BoneCount];
        _poseA = new AnimationTransform[skeleton.BoneCount];
        _poseB = new AnimationTransform[skeleton.BoneCount];

        for (var i = 0; i < skeleton.BoneCount; i++)
        {
            _boneTransforms[i] = Matrix3x2.Identity;
            _poseA[i] = AnimationTransform.Identity;
            _poseB[i] = AnimationTransform.Identity;
        }
    }

    public void Play(Animation animation, float normalizedTime = 0f)
    {
        _animation = animation;
        _time = normalizedTime * animation.Duration;
        _blendFromAnimation = null;
        _blendElapsed = 0f;
        _blendDuration = 0f;
    }

    public void CrossFade(Animation animation, float blendDuration, float normalizedTime = 0f)
    {
        if (_animation == null || blendDuration <= 0f)
        {
            Play(animation, normalizedTime);
            return;
        }

        _blendFromAnimation = _animation;
        _blendFromTime = _time;
        _blendDuration = blendDuration;
        _blendElapsed = 0f;

        _animation = animation;
        _time = normalizedTime * animation.Duration;
    }

    public void Update(float dt)
    {
        if (_animation == null)
            return;

        _time += dt;

        if (_animation.IsLooping)
            _time %= _animation.Duration;
        else
            _time = MathF.Min(_time, _animation.Duration);

        if (_blendFromAnimation != null)
        {
            _blendFromTime += dt;
            if (_blendFromAnimation.IsLooping)
                _blendFromTime %= _blendFromAnimation.Duration;
            else
                _blendFromTime = MathF.Min(_blendFromTime, _blendFromAnimation.Duration);

            _blendElapsed += dt;
            if (_blendElapsed >= _blendDuration)
                _blendFromAnimation = null;
        }

        UpdateBoneTransforms();
    }

    public void Evaluate(float normalizedTime)
    {
        if (_animation == null)
            return;

        _time = normalizedTime * _animation.Duration;
        UpdateBoneTransforms();
    }

    private void UpdateBoneTransforms()
    {
        if (_animation == null)
            return;

        SampleAnimation(_animation, _time, _poseA);

        if (_blendFromAnimation != null && _blendElapsed < _blendDuration)
        {
            SampleAnimation(_blendFromAnimation, _blendFromTime, _poseB);

            var blendT = _blendElapsed / _blendDuration;
            blendT = MathEx.SmoothStep(blendT);

            for (var i = 0; i < _skeleton.BoneCount; i++)
                _poseA[i] = AnimationTransform.Lerp(_poseB[i], _poseA[i], blendT);
        }

        CalculateBoneMatrices(_poseA);
    }

    private static void SampleAnimation(Animation animation, float time, AnimationTransform[] outPose)
    {
        var frameFloat = time * animation.FrameRate;
        var frameIndex = (int)frameFloat;
        var frameFraction = frameFloat - frameIndex;

        ref var frame = ref animation.GetFrame(frameIndex);

        for (var boneIndex = 0; boneIndex < animation.BoneCount; boneIndex++)
        {
            var skeletonBoneIndex = animation.Bones[boneIndex].Index;
            ref var transform0 = ref animation.GetTransform(boneIndex, frame.Transform0);
            ref var transform1 = ref animation.GetTransform(boneIndex, frame.Transform1);

            var t = frame.Fraction0 + (frame.Fraction1 - frame.Fraction0) * frameFraction;
            outPose[skeletonBoneIndex] = AnimationTransform.Lerp(transform0, transform1, t);
        }
    }

    private void CalculateBoneMatrices(AnimationTransform[] pose)
    {
        for (var boneIndex = 0; boneIndex < _skeleton.BoneCount; boneIndex++)
        {
            ref var transform = ref pose[boneIndex];

            var localMatrix =
                Matrix3x2.CreateScale(transform.Scale) *
                Matrix3x2.CreateRotation(MathEx.Deg2Rad * transform.Rotation) *
                Matrix3x2.CreateTranslation(transform.Position);

            ref var bone = ref _skeleton.GetBone(boneIndex);
            if (bone.ParentIndex >= 0)
                _boneTransforms[boneIndex] = localMatrix * _boneTransforms[bone.ParentIndex];
            else
                _boneTransforms[boneIndex] = localMatrix;
        }
    }

    public ref readonly Matrix3x2 GetBoneTransform(int boneIndex)
    {
        return ref _boneTransforms[boneIndex];
    }

    public void SetBones(Span<Matrix3x2> destination)
    {
        for (var i = 0; i < _skeleton.BoneCount; i++)
            destination[i] = _boneTransforms[i];
    }
}
