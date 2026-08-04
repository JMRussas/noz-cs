//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Diagnostics;

namespace NoZ;

public struct AnimationState
{
    private float _time;
    private float _speed;
    private float _blendTime;
    private float _blendDuration;
    private float _blendElapsed;
    private AssetHandle<Animation> _animation;
    private AssetHandle<Animation> _blendAnimation;

    public bool IsPlaying { get; private set; }
    public readonly Animation? Animation => _animation.HasValue() ? (Animation)_animation : null;
    
    public void Update() => Update(Time.DeltaTime);

    public void Update(float dt)
    {
        if (!_animation.HasValue())
            return;

        _time += dt * _speed;

        var animation = (Animation)_animation;
        if (animation.IsLooping)
            _time %= animation.Duration;
        else
        {
            _time = MathF.Min(_time, animation.Duration);
            IsPlaying = _time < animation.Duration;
        }   

        if (_blendAnimation.HasValue())
        {
            var blendAnimation = (Animation)_blendAnimation;
            _blendTime += dt;
            if (blendAnimation.IsLooping)
                _blendTime %= blendAnimation.Duration;
            else
                _blendTime = MathF.Min(_blendTime, blendAnimation.Duration);

            var blendElapsed = _blendTime;
            if (blendElapsed >= _blendDuration)
                _blendAnimation = default;
        }
    }

    public void Play(Animation animation, float normalizedTime = 0f, float speed = 1.0f, float blendTime = 0.1f)
    {
        if (blendTime > 0)
        {
            _blendAnimation = _animation;
            _blendDuration = blendTime;            
            _blendTime = _time;
        }
        else
        {
            _blendAnimation = default;
            _blendDuration = 0f;
            _blendTime = 0f;
        }

        _blendElapsed = 0f;
        _animation = animation;
        _speed = speed;
        _time = normalizedTime * animation.Duration;
        IsPlaying = true;
    }

    public readonly void Sample(Span<AnimationTransform> pose)
    {
        Debug.Assert(_animation.HasValue());
        Debug.Assert(pose.Length == ((Animation)_animation).Skeleton.BoneCount);

        ((Animation)_animation).Sample(_time, pose);
        
        if (_blendAnimation.HasValue() && _blendElapsed < _blendDuration)
        {
            var blendAnimation = (Animation)_blendAnimation;
            Span<AnimationTransform> blendPose = stackalloc AnimationTransform[blendAnimation.Skeleton.BoneCount];   
            blendAnimation.Sample(_blendTime, blendPose);

            var blendT = _blendElapsed / _blendDuration;
            blendT = MathEx.SmoothStep(blendT);

            for (var i = 0; i < blendPose.Length; i++)
                pose[i] = AnimationTransform.Lerp(blendPose[i], pose[i], blendT);
        }   
    }
}
