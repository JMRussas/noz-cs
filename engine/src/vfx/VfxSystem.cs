//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ;

public struct VfxHandle
{
    public uint Id;
    public uint Version;

    public static readonly VfxHandle Invalid = new() { Id = uint.MaxValue, Version = uint.MaxValue };
}

public static class VfxSystem
{
    private const int MaxParticles = 4096;
    private const int MaxEmitters = 2024;
    private const int MaxInstances = 256;

    private struct Particle
    {
        public Vector2 Position;
        public Vector2 Velocity;
        public VfxCurveType RotationCurve;
        public float RotationStart;
        public float RotationEnd;
        public VfxCurveType ColorCurve;
        public Color ColorStart;
        public Color ColorEnd;
        public VfxCurveType SizeCurve;
        public float SizeStart;
        public float SizeEnd;
        public VfxCurveType OpacityCurve;
        public float OpacityStart;
        public float OpacityEnd;
        public VfxCurveType SpeedCurve;
        public float SpeedStart;
        public float SpeedEnd;
        public float Lifetime;
        public float Elapsed;
        public float Rotation;
        public Vector2 Gravity;
        public float Drag;
        public ushort EmitterIndex;
    }

    private struct Emitter
    {
        public int DefIndex;
        public Vfx Vfx;
        public float Rate;
        public float Elapsed;
        public float Duration;
        public float Accumulator;
        public ushort InstanceIndex;
        public int ParticleCount;
    }

    private struct Instance
    {
        public Vfx Vfx;
        public Matrix3x2 Transform;
        public float Depth;
        public int EmitterCount;
        public bool Loop;
        public uint Version;
    }

    private static Particle[] _particles = null!;
    private static bool[] _particleValid = null!;
    private static int _particleCount;

    private static Emitter[] _emitters = null!;
    private static bool[] _emitterValid = null!;
    private static int _emitterCount;

    private static Instance[] _instances = null!;
    private static bool[] _instanceValid = null!;
    private static int _instanceCount;

    public static Shader? Shader { get; set; }

    public static void Init()
    {
        _particles = new Particle[MaxParticles];
        _particleValid = new bool[MaxParticles];
        _particleCount = 0;

        _emitters = new Emitter[MaxEmitters];
        _emitterValid = new bool[MaxEmitters];
        _emitterCount = 0;

        _instances = new Instance[MaxInstances];
        _instanceValid = new bool[MaxInstances];
        _instanceCount = 0;
    }

    public static void Shutdown()
    {
        _particles = null!;
        _particleValid = null!;
        _emitters = null!;
        _emitterValid = null!;
        _instances = null!;
        _instanceValid = null!;
        Shader = null;
    }

    public static VfxHandle Play(Vfx? vfx, Vector2 position, float depth = 0f)
    {
        return Play(vfx, Matrix3x2.CreateTranslation(position), depth);
    }

    public static VfxHandle Play(Vfx? vfx, Matrix3x2 transform, float depth = 0f)
    {
        if (vfx == null || vfx.EmitterDefs.Length == 0)
            return VfxHandle.Invalid;

        var instanceIndex = AllocInstance();
        if (instanceIndex < 0)
            return VfxHandle.Invalid;

        ref var instance = ref _instances[instanceIndex];
        instance.Vfx = vfx;
        instance.Transform = transform;
        instance.Depth = depth;
        instance.EmitterCount = 0;
        instance.Loop = vfx.Loop;

        for (var i = 0; i < vfx.EmitterDefs.Length; i++)
        {
            var emitterIndex = AllocEmitter();
            if (emitterIndex < 0)
                break;

            ref var emitter = ref _emitters[emitterIndex];
            ref var def = ref vfx.EmitterDefs[i];

            emitter.Vfx = vfx;
            emitter.DefIndex = i;
            emitter.Elapsed = 0f;
            emitter.Duration = GetRandom(def.Duration);
            emitter.Accumulator = 0f;
            emitter.InstanceIndex = (ushort)instanceIndex;
            emitter.ParticleCount = 0;

            var rate = Random.Shared.Next(def.Rate.Min, Math.Max(def.Rate.Min, def.Rate.Max) + 1);
            emitter.Rate = rate > 0 ? 1f / rate : 0f;

            instance.EmitterCount++;

            // Burst particles
            var burstCount = Random.Shared.Next(def.Burst.Min, Math.Max(def.Burst.Min, def.Burst.Max) + 1);
            for (var b = 0; b < burstCount; b++)
                EmitParticle(emitterIndex);
        }

        return new VfxHandle { Id = (uint)instanceIndex, Version = instance.Version };
    }

    public static void Stop(VfxHandle handle)
    {
        var instance = GetInstance(handle);
        if (instance < 0)
            return;

        _instances[instance].Loop = false;

        // Stop all emitters but let existing particles live
        for (var i = 0; i < MaxEmitters; i++)
        {
            if (!_emitterValid[i])
                continue;

            if (_emitters[i].InstanceIndex == instance)
                _emitters[i].Rate = 0f;
        }
    }

    public static void Kill(VfxHandle handle)
    {
        var instance = GetInstance(handle);
        if (instance < 0)
            return;

        for (var i = 0; i < MaxParticles; i++)
        {
            if (!_particleValid[i])
                continue;

            if (!_emitterValid[_particles[i].EmitterIndex])
                continue;

            if (_emitters[_particles[i].EmitterIndex].InstanceIndex == instance)
                FreeParticle(i);
        }

        for (var i = 0; i < MaxEmitters; i++)
        {
            if (!_emitterValid[i])
                continue;

            if (_emitters[i].InstanceIndex == instance)
                FreeEmitter(i);
        }
    }

    public static bool IsPlaying(VfxHandle handle)
    {
        return GetInstance(handle) >= 0;
    }

    public static void Clear()
    {
        for (var i = 0; i < MaxParticles; i++)
        {
            if (_particleValid[i])
                FreeParticle(i);
        }

        for (var i = 0; i < MaxEmitters; i++)
        {
            if (_emitterValid[i])
                FreeEmitter(i);
        }

        for (var i = 0; i < MaxInstances; i++)
        {
            if (_instanceValid[i])
                FreeInstance(i);
        }
    }

    public static void Update()
    {
        UpdateEmitters();
        SimulateParticles();
    }

    public static void Render()
    {
        if (_particleCount == 0 || Shader == null)
            return;

        using (Graphics.PushState())
        {
            Graphics.SetShader(Shader);
            Graphics.SetTexture(Graphics.WhiteTexture);
            Graphics.SetBlendMode(BlendMode.Alpha);

            for (var i = 0; i < MaxParticles; i++)
            {
                if (!_particleValid[i])
                    continue;

                ref var p = ref _particles[i];

                if (!_emitterValid[p.EmitterIndex])
                    continue;

                ref var e = ref _emitters[p.EmitterIndex];

                if (!_instanceValid[e.InstanceIndex])
                    continue;

                ref var inst = ref _instances[e.InstanceIndex];

                var t = p.Elapsed / p.Lifetime;
                var size = MathEx.Mix(p.SizeStart, p.SizeEnd, EvaluateCurve(p.SizeCurve, t));
                var opacity = MathEx.Mix(p.OpacityStart, p.OpacityEnd, EvaluateCurve(p.OpacityCurve, t));
                var col = Color.Mix(p.ColorStart, p.ColorEnd, EvaluateCurve(p.ColorCurve, t));

                var particleTransform =
                    Matrix3x2.CreateScale(size) *
                    Matrix3x2.CreateRotation(p.Rotation) *
                    Matrix3x2.CreateTranslation(p.Position) *
                    inst.Transform;

                Graphics.SetColor(col.WithAlpha(opacity));
                Graphics.SetSortGroup((int)inst.Depth);
                Graphics.Draw(-0.5f, -0.5f, 1f, 1f, particleTransform);
            }
        }
    }

    // --- Private Implementation ---

    private static void UpdateEmitters()
    {
        if (_emitterCount == 0)
            return;

        var dt = Time.DeltaTime;

        for (var i = 0; i < MaxEmitters; i++)
        {
            if (!_emitterValid[i])
                continue;

            ref var e = ref _emitters[i];

            e.Elapsed += dt;

            if (e.Rate <= 0.0000001f || e.Elapsed >= e.Duration)
            {
                if (_instanceValid[e.InstanceIndex] && _instances[e.InstanceIndex].Loop)
                {
                    ref var def = ref e.Vfx.EmitterDefs[e.DefIndex];
                    e.Elapsed = 0f;
                    e.Accumulator = 0f;
                    e.Duration = GetRandom(def.Duration);

                    var burstCount = Random.Shared.Next(def.Burst.Min, Math.Max(def.Burst.Min, def.Burst.Max) + 1);
                    for (var b = 0; b < burstCount; b++)
                        EmitParticle(i);
                }
                else
                {
                    if (e.ParticleCount == 0)
                        FreeEmitter(i);
                }
                continue;
            }

            e.Accumulator += dt;

            while (e.Accumulator >= e.Rate)
            {
                EmitParticle(i);
                e.Accumulator -= e.Rate;
            }
        }
    }

    private static void SimulateParticles()
    {
        if (_particleCount == 0)
            return;

        var dt = Time.DeltaTime;

        for (var i = 0; i < MaxParticles; i++)
        {
            if (!_particleValid[i])
                continue;

            ref var p = ref _particles[i];
            p.Elapsed += dt;

            if (p.Elapsed >= p.Lifetime)
            {
                FreeParticle(i);
                continue;
            }

            var t = p.Elapsed / p.Lifetime;
            var curveT = EvaluateCurve(p.SpeedCurve, t);
            var currentSpeed = MathEx.Mix(p.SpeedStart, p.SpeedEnd, curveT);

            var velLen = p.Velocity.Length();
            var vel = velLen > 0.0001f
                ? Vector2.Normalize(p.Velocity) * currentSpeed
                : p.Velocity;

            vel += p.Gravity * dt;
            vel *= 1f - p.Drag * dt;

            p.Position += vel * dt;
            p.Velocity = vel;
            p.Rotation = MathEx.Mix(p.RotationStart, p.RotationEnd, EvaluateCurve(p.RotationCurve, t));
        }
    }

    private static void EmitParticle(int emitterIndex)
    {
        if (_particleCount >= MaxParticles)
            return;

        var particleIndex = AllocParticle();
        if (particleIndex < 0)
            return;

        ref var e = ref _emitters[emitterIndex];
        ref var def = ref e.Vfx.EmitterDefs[e.DefIndex];
        ref var pdef = ref def.Particle;

        if (!_instanceValid[e.InstanceIndex])
            return;

        ref var inst = ref _instances[e.InstanceIndex];

        var angle = MathEx.Radians(GetRandom(def.Angle));
        var dir = GetRandom(def.Direction);
        if (MathF.Abs(dir.X) < 0.0001f && MathF.Abs(dir.Y) < 0.0001f)
            dir = new Vector2(MathF.Cos(angle), MathF.Sin(angle));

        ref var p = ref _particles[particleIndex];

        // Transform spawn position by instance transform
        var spawnOffset = GetRandom(def.Spawn);
        p.Position = Vector2.TransformNormal(spawnOffset, inst.Transform);

        p.SizeStart = GetRandom(pdef.Size.Start);
        p.SizeEnd = GetRandom(pdef.Size.End);
        p.SizeCurve = pdef.Size.Type;

        p.OpacityStart = GetRandom(pdef.Opacity.Start);
        p.OpacityEnd = GetRandom(pdef.Opacity.End);
        p.OpacityCurve = pdef.Opacity.Type;

        p.SpeedStart = GetRandom(pdef.Speed.Start);
        p.SpeedEnd = GetRandom(pdef.Speed.End);
        p.SpeedCurve = pdef.Speed.Type;

        p.Velocity = dir * p.SpeedStart;

        p.ColorStart = GetRandom(pdef.Color.Start);
        p.ColorEnd = GetRandom(pdef.Color.End);
        p.ColorCurve = pdef.Color.Type;

        p.Lifetime = GetRandom(pdef.Duration);
        p.Elapsed = 0f;

        p.Gravity = GetRandom(pdef.Gravity);
        p.Drag = GetRandom(pdef.Drag);

        p.RotationStart = MathEx.Radians(GetRandom(pdef.Rotation.Start));
        p.RotationEnd = p.RotationStart + MathEx.Radians(GetRandom(pdef.Rotation.End));
        p.RotationCurve = pdef.Rotation.Type;
        p.Rotation = p.RotationStart;

        p.EmitterIndex = (ushort)emitterIndex;

        e.ParticleCount++;
    }

    private static int GetInstance(VfxHandle handle)
    {
        if (handle.Id == VfxHandle.Invalid.Id && handle.Version == VfxHandle.Invalid.Version)
            return -1;

        if (handle.Id >= MaxInstances)
            return -1;

        if (!_instanceValid[handle.Id])
            return -1;

        if (_instances[handle.Id].Version != handle.Version)
            return -1;

        return (int)handle.Id;
    }

    // --- Pool allocation ---

    private static int AllocParticle()
    {
        for (var i = 0; i < MaxParticles; i++)
        {
            if (!_particleValid[i])
            {
                _particleValid[i] = true;
                _particles[i] = default;
                _particleCount++;
                return i;
            }
        }
        return -1;
    }

    private static void FreeParticle(int index)
    {
        if (!_particleValid[index])
            return;

        ref var p = ref _particles[index];

        if (_emitterValid[p.EmitterIndex])
        {
            _emitters[p.EmitterIndex].ParticleCount--;
        }

        _particleValid[index] = false;
        _particleCount--;
    }

    private static int AllocEmitter()
    {
        for (var i = 0; i < MaxEmitters; i++)
        {
            if (!_emitterValid[i])
            {
                _emitterValid[i] = true;
                _emitters[i] = default;
                _emitterCount++;
                return i;
            }
        }
        return -1;
    }

    private static void FreeEmitter(int index)
    {
        if (!_emitterValid[index])
            return;

        ref var e = ref _emitters[index];

        if (_instanceValid[e.InstanceIndex])
        {
            _instances[e.InstanceIndex].EmitterCount--;

            if (_instances[e.InstanceIndex].EmitterCount == 0)
                FreeInstance(e.InstanceIndex);
        }

        _emitterValid[index] = false;
        _emitterCount--;
    }

    private static int AllocInstance()
    {
        for (var i = 0; i < MaxInstances; i++)
        {
            if (!_instanceValid[i])
            {
                _instanceValid[i] = true;
                _instances[i] = default;
                _instanceCount++;
                return i;
            }
        }
        return -1;
    }

    private static void FreeInstance(int index)
    {
        if (!_instanceValid[index])
            return;

        _instances[index].Version++;
        _instanceValid[index] = false;
        _instanceCount--;
    }

    // --- Curve evaluation ---

    internal static float EvaluateCurve(VfxCurveType curve, float t)
    {
        t = Math.Clamp(t, 0f, 1f);

        return curve switch
        {
            VfxCurveType.Linear => t,
            VfxCurveType.EaseIn => t * t,
            VfxCurveType.EaseOut => 1f - (1f - t) * (1f - t),
            VfxCurveType.EaseInOut => t < 0.5f ? 2f * t * t : 1f - 2f * (1f - t) * (1f - t),
            VfxCurveType.Quadratic => t * t,
            VfxCurveType.Cubic => t * t * t,
            VfxCurveType.Sine => MathF.Sin(t * MathF.PI * 0.5f),
            _ => t
        };
    }

    // --- Random helpers ---

    private static float GetRandom(VfxRange range)
    {
        if (MathF.Abs(range.Max - range.Min) < 0.0001f)
            return range.Min;
        return range.Min + (float)Random.Shared.NextDouble() * (range.Max - range.Min);
    }

    private static Vector2 GetRandom(VfxVec2Range range)
    {
        return new Vector2(
            MathEx.Mix(range.Min.X, range.Max.X, (float)Random.Shared.NextDouble()),
            MathEx.Mix(range.Min.Y, range.Max.Y, (float)Random.Shared.NextDouble()));
    }

    private static Color GetRandom(VfxColorRange range)
    {
        return Color.Mix(range.Min, range.Max, (float)Random.Shared.NextDouble());
    }
}
