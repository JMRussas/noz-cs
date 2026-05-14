//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;

namespace NoZ.Editor;

public enum VfxSelectionType { None, Vfx, Emitter, Particle }

public struct VfxDocFloatCurve
{
    public VfxRange Start;
    public VfxRange End;
    public VfxCurveType CurveType;
    public VfxEaseType EaseType;
    public float WindowBegin;
    public float WindowEnd;

    public static readonly VfxDocFloatCurve Zero = new() { Start = VfxRange.Zero, End = VfxRange.Zero, WindowEnd = 1f };
    public static readonly VfxDocFloatCurve One = new() { Start = VfxRange.One, End = VfxRange.One, WindowEnd = 1f };

    public VfxFloatCurve Bake()
    {
        var c = new VfxFloatCurve { Start = Start, End = End };
        for (var i = 0; i < VfxCurveLut.Samples; i++)
        {
            var t = i / (float)(VfxCurveLut.Samples - 1);
            c.Lut[i] = VfxDocument.SampleAuthored(CurveType, EaseType, WindowBegin, WindowEnd, t);
        }
        return c;
    }

    public static bool operator ==(VfxDocFloatCurve a, VfxDocFloatCurve b) =>
        a.Start == b.Start && a.End == b.End
        && a.CurveType == b.CurveType && a.EaseType == b.EaseType
        && a.WindowBegin == b.WindowBegin && a.WindowEnd == b.WindowEnd;
    public static bool operator !=(VfxDocFloatCurve a, VfxDocFloatCurve b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxDocFloatCurve c && this == c;
    public override int GetHashCode() => HashCode.Combine(Start, End, CurveType, EaseType, WindowBegin, WindowEnd);
}

public struct VfxDocColorCurve
{
    public VfxColorRange Start;
    public VfxColorRange End;
    public VfxCurveType CurveType;
    public VfxEaseType EaseType;
    public float WindowBegin;
    public float WindowEnd;

    public static readonly VfxDocColorCurve White = new() { Start = VfxColorRange.White, End = VfxColorRange.White, WindowEnd = 1f };

    public VfxColorCurve Bake()
    {
        var c = new VfxColorCurve { Start = Start, End = End };
        for (var i = 0; i < VfxCurveLut.Samples; i++)
        {
            var t = i / (float)(VfxCurveLut.Samples - 1);
            c.Lut[i] = VfxDocument.SampleAuthored(CurveType, EaseType, WindowBegin, WindowEnd, t);
        }
        return c;
    }

    public static bool operator ==(VfxDocColorCurve a, VfxDocColorCurve b) =>
        a.Start == b.Start && a.End == b.End
        && a.CurveType == b.CurveType && a.EaseType == b.EaseType
        && a.WindowBegin == b.WindowBegin && a.WindowEnd == b.WindowEnd;
    public static bool operator !=(VfxDocColorCurve a, VfxDocColorCurve b) => !(a == b);
    public override bool Equals(object? obj) => obj is VfxDocColorCurve c && this == c;
    public override int GetHashCode() => HashCode.Combine(Start, End, CurveType, EaseType, WindowBegin, WindowEnd);
}

public class VfxDocParticle
{
    public string Name = "";
    public VfxRange Duration;
    public VfxDocFloatCurve Size;
    public VfxDocFloatCurve Speed;
    public VfxDocColorCurve Color;
    public VfxDocFloatCurve Opacity;
    public VfxDocFloatCurve Gravity;
    public VfxRange Rotation;
    public VfxDocFloatCurve RotationSpeed;
    public bool AlignToDirection;
    public VfxFrameMode FrameMode;
    public ushort Sort;
    public DocumentRef<SpriteDocument> SpriteRef;

    public VfxParticleDef BakeDef() => new()
    {
        Gravity = Gravity.Bake(),
        Duration = Duration,
        Size = Size.Bake(),
        Speed = Speed.Bake(),
        Color = Color.Bake(),
        Opacity = Opacity.Bake(),
        Rotation = Rotation,
        RotationSpeed = RotationSpeed.Bake(),
        AlignToDirection = AlignToDirection,
        FrameMode = FrameMode,
        Sprite = SpriteRef.Value?.Sprite,
        Sort = Sort,
    };

    public static VfxDocParticle Clone(VfxDocParticle src, string? newName = null) => new()
    {
        Name = newName ?? src.Name,
        Duration = src.Duration, Size = src.Size, Speed = src.Speed, Color = src.Color,
        Opacity = src.Opacity, Gravity = src.Gravity, Rotation = src.Rotation,
        RotationSpeed = src.RotationSpeed, AlignToDirection = src.AlignToDirection,
        FrameMode = src.FrameMode, Sort = src.Sort,
        SpriteRef = src.SpriteRef,
    };
}

public class VfxDocEmitter
{
    public string Name = "";
    public VfxDocFloatCurve Rate;
    public VfxIntRange Burst;
    public VfxRange Duration;
    public VfxSpawnDef Spawn;
    public float Direction;
    public float Spread;
    public float Radial;
    public bool WorldSpace;
    public string ParticleRef = "";

    public VfxEmitterDef BakeDef(VfxParticleDef particle) => new()
    {
        Rate = Rate.Bake(),
        Burst = Burst,
        Duration = Duration,
        Spawn = Spawn,
        Direction = Direction,
        Spread = Spread,
        Radial = Radial,
        WorldSpace = WorldSpace,
        Particle = particle,
    };

    public static VfxDocEmitter Clone(VfxDocEmitter src, string? newName = null, string? newParticleRef = null) => new()
    {
        Name = newName ?? src.Name,
        Rate = src.Rate, Burst = src.Burst, Duration = src.Duration, Spawn = src.Spawn,
        Direction = src.Direction, Spread = src.Spread, Radial = src.Radial, WorldSpace = src.WorldSpace,
        ParticleRef = newParticleRef ?? src.ParticleRef,
    };
}

public class VfxDocument : Document
{
    public const string Extension = ".vfx";

    public override bool CanSave => true;

    private const int MaxEmitters = 32;
    private const int MaxParticles = 32;

    private Vfx? _vfx;
    private VfxHandle _handle = VfxHandle.Invalid;
    public Rect VfxBounds => _vfx?.Bounds ?? new Rect(-0.5f, -0.5f, 1f, 1f);
    private bool _playing;
    private VfxRange _duration;
    private bool _loop;
    private bool _editorLoop = true;
    private float _rotation;

    public readonly List<VfxDocEmitter> Emitters = [];
    public readonly List<VfxDocParticle> Particles = [];

    // --- Selection ---

    private readonly HashSet<int> _selectedEmitters = [];
    private readonly HashSet<int> _selectedParticles = [];

    public IReadOnlySet<int> SelectedEmitters => _selectedEmitters;
    public IReadOnlySet<int> SelectedParticles => _selectedParticles;
    public bool VfxRootSelected { get; private set; }
    public int SelectionCount => _selectedEmitters.Count + _selectedParticles.Count;
    public bool HasSelection => VfxRootSelected || _selectedEmitters.Count > 0 || _selectedParticles.Count > 0;
    public bool HasMultiSelection => SelectionCount > 1;

    public VfxSelectionType SingleSelectedType
    {
        get
        {
            if (VfxRootSelected) return VfxSelectionType.Vfx;
            if (_selectedEmitters.Count == 1 && _selectedParticles.Count == 0) return VfxSelectionType.Emitter;
            if (_selectedParticles.Count == 1 && _selectedEmitters.Count == 0) return VfxSelectionType.Particle;
            return VfxSelectionType.None;
        }
    }

    public int SingleSelectedIndex
    {
        get
        {
            if (_selectedEmitters.Count == 1 && _selectedParticles.Count == 0)
            {
                foreach (var i in _selectedEmitters) return i;
            }
            if (_selectedParticles.Count == 1 && _selectedEmitters.Count == 0)
            {
                foreach (var i in _selectedParticles) return i;
            }
            return -1;
        }
    }

    public void ClearSelection()
    {
        _selectedEmitters.Clear();
        _selectedParticles.Clear();
        VfxRootSelected = false;
    }

    public void SelectVfxRoot()
    {
        _selectedEmitters.Clear();
        _selectedParticles.Clear();
        VfxRootSelected = true;
    }

    public void SelectEmitter(int index)
    {
        _selectedEmitters.Clear();
        _selectedParticles.Clear();
        VfxRootSelected = false;
        _selectedEmitters.Add(index);
    }

    public void SelectParticle(int index)
    {
        _selectedEmitters.Clear();
        _selectedParticles.Clear();
        VfxRootSelected = false;
        _selectedParticles.Add(index);
    }

    public void ToggleEmitter(int index)
    {
        VfxRootSelected = false;
        if (!_selectedEmitters.Remove(index))
            _selectedEmitters.Add(index);
    }

    public void ToggleParticle(int index)
    {
        VfxRootSelected = false;
        if (!_selectedParticles.Remove(index))
            _selectedParticles.Add(index);
    }

    public ref VfxRange Duration => ref _duration;
    public ref bool Loop => ref _loop;
    public bool EditorLoop { get => _editorLoop; set => _editorLoop = value; }

    public float Rotation
    {
        get => _rotation;
        set
        {
            _rotation = value;
            VfxSystem.SetTransform(_handle, PlayTransform);
        }
    }

    private Matrix3x2 PlayTransform =>
        Matrix3x2.CreateRotation(MathEx.Deg2Rad * _rotation) *
        Matrix3x2.CreateTranslation(Position);

    // --- Emitter management ---

    public void AddEmitter(string name)
    {
        if (Emitters.Count >= MaxEmitters) return;

        var emitter = new VfxDocEmitter
        {
            Name = name,
            Rate = new VfxDocFloatCurve { Start = new VfxRange(10, 10), End = new VfxRange(10, 10), WindowEnd = 1f },
            Duration = VfxRange.One,
            ParticleRef = Particles.Count > 0 ? Particles[0].Name : ""
        };

        Emitters.Add(emitter);
        SelectEmitter(Emitters.Count - 1);
        ApplyChanges();
    }

    public void RemoveEmitter(int index)
    {
        if (index < 0 || index >= Emitters.Count) return;

        Emitters.RemoveAt(index);
        FixUpSelectionAfterRemoval(_selectedEmitters, index);
        ApplyChanges();
    }

    public void RenameEmitter(int index, string newName)
    {
        if (index < 0 || index >= Emitters.Count) return;
        Emitters[index].Name = newName;
        ApplyChanges();
    }

    // --- Particle management ---

    public void AddParticle(string name)
    {
        if (Particles.Count >= MaxParticles) return;

        var particle = new VfxDocParticle
        {
            Name = name,
            Duration = new VfxRange(0.5f, 1.0f),
        };

        Particles.Add(particle);
        SelectParticle(Particles.Count - 1);
        ApplyChanges();
    }

    public void RemoveParticle(int index)
    {
        if (index < 0 || index >= Particles.Count) return;

        var removedName = Particles[index].Name;
        Particles.RemoveAt(index);

        // Update emitter references
        foreach (var e in Emitters)
        {
            if (e.ParticleRef == removedName)
                e.ParticleRef = "";
        }

        FixUpSelectionAfterRemoval(_selectedParticles, index);
        ApplyChanges();
    }

    public void RenameParticle(int index, string newName)
    {
        if (index < 0 || index >= Particles.Count) return;

        var oldName = Particles[index].Name;
        Particles[index].Name = newName;

        // Update emitter references
        foreach (var e in Emitters)
        {
            if (e.ParticleRef == oldName)
                e.ParticleRef = newName;
        }

        ApplyChanges();
    }

    private static void FixUpSelectionAfterRemoval(HashSet<int> selection, int removedIndex)
    {
        selection.Remove(removedIndex);
        // Decrement indices above the removed one
        var updated = new List<int>();
        foreach (var idx in selection)
            updated.Add(idx > removedIndex ? idx - 1 : idx);
        selection.Clear();
        foreach (var idx in updated)
            selection.Add(idx);
    }

    public VfxDocParticle? FindParticle(string name)
    {
        foreach (var p in Particles)
        {
            if (p.Name == name)
                return p;
        }
        return null;
    }

    // --- Build & Apply ---

    public void ApplyChanges()
    {
        IncrementVersion();
        BuildVfx();

        if (_playing && _vfx != null)
        {
            VfxSystem.Stop(_handle);
            _handle = VfxSystem.Play(_vfx, PlayTransform, layer: EditorLayer.Document);
        }
    }

    private void BuildVfx()
    {
        // Resolve sprite refs so BakeDef can pick them up.
        foreach (var p in Particles)
            if (p.SpriteRef.HasValue && !p.SpriteRef.IsResolved)
                p.SpriteRef.Resolve();

        var emitterDefs = new VfxEmitterDef[Emitters.Count];
        for (var i = 0; i < Emitters.Count; i++)
        {
            var particle = FindParticle(Emitters[i].ParticleRef);
            var particleDef = particle != null ? particle.BakeDef() : default;
            emitterDefs[i] = Emitters[i].BakeDef(particleDef);
        }

        _vfx = new Vfx(Name)
        {
            Duration = _duration,
            Loop = _loop,
            EmitterDefs = emitterDefs,
            Bounds = CalculateBounds(emitterDefs)
        };
    }

    private static Rect CalculateBounds(VfxEmitterDef[] emitterDefs)
    {
        if (emitterDefs.Length == 0)
            return new Rect(-0.05f, -0.05f, 0.1f, 0.1f);

        var minX = float.MaxValue;
        var minY = float.MaxValue;
        var maxX = float.MinValue;
        var maxY = float.MinValue;

        Span<Vector2> directions = stackalloc Vector2[16];

        for (var i = 0; i < emitterDefs.Length; i++)
        {
            ref var e = ref emitterDefs[i];
            ref var p = ref e.Particle;

            var halfSize = MathF.Max(
                MathF.Max(p.Size.Start.Min, p.Size.Start.Max),
                MathF.Max(p.Size.End.Min, p.Size.End.Max)) * 0.5f;

            GetSpawnBounds(ref e.Spawn, out var spawnMin, out var spawnMax);

            // Include spawn area itself
            minX = MathF.Min(minX, spawnMin.X - halfSize);
            minY = MathF.Min(minY, spawnMin.Y - halfSize);
            maxX = MathF.Max(maxX, spawnMax.X + halfSize);
            maxY = MathF.Max(maxY, spawnMax.Y + halfSize);

            var lifetime = p.Duration.Max;
            if (lifetime < 0.0001f)
                continue;

            var speedStart = p.Speed.Start.Max;
            var speedEnd = p.Speed.End.Max;

            var dirCount = BuildDirectionSamples(ref e, directions);

            const float dt = 1f / 60f;
            var steps = (int)MathF.Ceiling(lifetime / dt);

            // Simulate gravity extremes so bounds cover the widest range
            // (handles signed gravity, e.g. negative for upward drift).
            for (var d = 0; d < dirCount; d++)
            {
                SimulateTrajectory(
                    directions[d], speedStart, speedEnd, ref p.Speed.Lut,
                    p.Gravity.Start.Min, p.Gravity.End.Min, ref p.Gravity.Lut,
                    lifetime, dt, steps, halfSize,
                    ref spawnMin, ref spawnMax, ref minX, ref minY, ref maxX, ref maxY);
                SimulateTrajectory(
                    directions[d], speedStart, speedEnd, ref p.Speed.Lut,
                    p.Gravity.Start.Max, p.Gravity.End.Max, ref p.Gravity.Lut,
                    lifetime, dt, steps, halfSize,
                    ref spawnMin, ref spawnMax, ref minX, ref minY, ref maxX, ref maxY);
            }
        }

        // Safety padding (5%) for simulation discretization
        var w = maxX - minX;
        var h = maxY - minY;
        var pad = MathF.Max(w, h) * 0.05f;
        minX -= pad;
        minY -= pad;
        maxX += pad;
        maxY += pad;

        return new Rect(minX, minY, maxX - minX, maxY - minY);
    }

    private static void SimulateTrajectory(
        Vector2 dir, float speedStart, float speedEnd,
        ref VfxCurveLut speedLut,
        float gravityStart, float gravityEnd,
        ref VfxCurveLut gravityLut,
        float lifetime,
        float dt, int steps, float halfSize,
        ref Vector2 spawnMin, ref Vector2 spawnMax,
        ref float minX, ref float minY, ref float maxX, ref float maxY)
    {
        var pos = Vector2.Zero;
        var gravityVel = Vector2.Zero;
        var gravityDir = VfxSystem.GravityDirection;

        for (var step = 1; step <= steps; step++)
        {
            var elapsed = step * dt;
            var t = MathF.Min(elapsed / lifetime, 1f);

            var speedCurveT = VfxSystem.EvaluateCurve(ref speedLut, t);
            var gravityCurveT = VfxSystem.EvaluateCurve(ref gravityLut, t);
            var speedNow = MathEx.Mix(speedStart, speedEnd, speedCurveT);
            var gravityNow = MathEx.Mix(gravityStart, gravityEnd, gravityCurveT);

            gravityVel += gravityDir * gravityNow * dt;
            var totalVel = dir * speedNow + gravityVel;

            pos += totalVel * dt;

            // Track extremes offset by spawn area bounds
            minX = MathF.Min(minX, spawnMin.X + pos.X - halfSize);
            minY = MathF.Min(minY, spawnMin.Y + pos.Y - halfSize);
            maxX = MathF.Max(maxX, spawnMax.X + pos.X + halfSize);
            maxY = MathF.Max(maxY, spawnMax.Y + pos.Y + halfSize);
        }
    }

    private static int BuildDirectionSamples(ref VfxEmitterDef e, Span<Vector2> directions)
    {
        // When radial is high, particles can go in any direction from the spawn area
        if (e.Radial > 0.5f)
        {
            for (var i = 0; i < 16; i++)
            {
                var a = MathF.Tau * i / 16f;
                directions[i] = new Vector2(MathF.Cos(a), MathF.Sin(a));
            }
            return 16;
        }

        // Compute the full angle range: direction ± spread, expanded by radial
        var dir = MathEx.Radians(e.Direction);
        var spreadMax = MathEx.Radians(e.Spread);
        var radialExpand = MathEx.Radians(e.Radial * 180f);

        var angleMin = dir - spreadMax - radialExpand;
        var angleMax = dir + spreadMax + radialExpand;
        var arcSpan = angleMax - angleMin;

        if (arcSpan < 0.001f)
        {
            directions[0] = new Vector2(MathF.Cos(angleMin), MathF.Sin(angleMin));
            return 1;
        }

        var numSamples = Math.Clamp((int)MathF.Round(16f * arcSpan / MathF.Tau), 2, 16);

        for (var i = 0; i < numSamples; i++)
        {
            var angle = angleMin + arcSpan * i / (numSamples - 1);
            directions[i] = new Vector2(MathF.Cos(angle), MathF.Sin(angle));
        }

        return numSamples;
    }

    private static void GetSpawnBounds(ref VfxSpawnDef spawn, out Vector2 min, out Vector2 max)
    {
        switch (spawn.Shape)
        {
            case VfxSpawnShape.Circle:
                var r = spawn.Circle.Radius;
                min = spawn.Offset - new Vector2(r, r);
                max = spawn.Offset + new Vector2(r, r);
                break;
            case VfxSpawnShape.Box:
                var half = spawn.Box.Size * 0.5f;
                if (MathF.Abs(spawn.Box.Rotation) > 0.0001f)
                {
                    // AABB of rotated box
                    var rad = MathEx.Radians(spawn.Box.Rotation);
                    var cos = MathF.Abs(MathF.Cos(rad));
                    var sin = MathF.Abs(MathF.Sin(rad));
                    var aabbHalf = new Vector2(
                        half.X * cos + half.Y * sin,
                        half.X * sin + half.Y * cos);
                    min = spawn.Offset - aabbHalf;
                    max = spawn.Offset + aabbHalf;
                }
                else
                {
                    min = spawn.Offset - half;
                    max = spawn.Offset + half;
                }
                break;
            default:
                min = spawn.Offset;
                max = spawn.Offset;
                break;
        }
    }

    // --- Registration ---

    public static void RegisterDef()
    {
        DocumentDef<VfxDocument>.Register(new DocumentDef
        {
            Type = AssetType.Vfx,
            Name = "Vfx",
            Extensions = [Extension],
            Factory = _ => new VfxDocument(),
            EditorFactory = doc => new VfxEditor((VfxDocument)doc),
            Icon = () => EditorAssets.Sprites.AssetIconVfx
        });
    }

    public static Document? CreateNew(string? name = null, System.Numerics.Vector2? position = null)
    {
        return Project.New(AssetType.Vfx, Extension, name, writer =>
        {
            writer.WriteLine("duration 1");
            writer.WriteLine("loop false");
            writer.WriteLine();
            writer.WriteLine("particle \"default.particle\" {");
            writer.WriteLine("  duration [0.5, 1.0]");
            writer.WriteLine("  size 0.5=>[0.0, 0.1]:quadratic out");
            writer.WriteLine("  speed [20, 40]=>[5, 10]:linear");
            writer.WriteLine("  opacity 1.0=>0.0:quadratic out");
            writer.WriteLine("}");
            writer.WriteLine();
            writer.WriteLine("emitter \"default\" {");
            writer.WriteLine("  rate 10");
            writer.WriteLine("  burst 0");
            writer.WriteLine("  duration 1");
            writer.WriteLine("  particle \"default.particle\"");
            writer.WriteLine("  spread 180");
            writer.WriteLine("}");
        }, position);
    }

    // --- Lifecycle ---

    public override void Load()
    {
        ParseVfxFile();
        BuildVfx();
    }

    public override void PostLoad()
    {
        ResolveSpriteRefs();
        BuildVfx();
    }

    public override void GetReferences(List<Document> references)
    {
        foreach (var p in Particles)
            if (p.SpriteRef.IsResolved)
                references.Add(p.SpriteRef.Value!);
    }

    public override void GetDependencies(List<(AssetType Type, string Name)> dependencies)
    {
        foreach (var p in Particles)
            if (p.SpriteRef.HasValue)
                dependencies.Add((AssetType.Sprite, p.SpriteRef.Name!));
    }

    public override void OnRenamed(Document doc, string oldName, string newName)
    {
        if (doc is not SpriteDocument) return;
        var changed = false;
        foreach (var p in Particles)
            if (p.SpriteRef.TryRename(oldName, newName))
                changed = true;
        if (changed)
            IncrementVersion();
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        ParseVfxFile();
        WriteBinary(outputPath);

        var wasPlaying = _playing;
        if (_playing)
        {
            VfxSystem.Stop(_handle);
            _handle = VfxHandle.Invalid;
        }

        BuildVfx();

        if (wasPlaying && _vfx != null)
        {
            _handle = VfxSystem.Play(_vfx, PlayTransform, layer: EditorLayer.Document);
            _playing = true;
        }
    }

    public override void Draw()
    {
        if (!_playing || Emitters.Count == 0)
        {
            if (!IsEditing)
            {
                using (Graphics.PushState())
                {
                    Graphics.SetShader(EditorAssets.Shaders.Sprite);
                    Graphics.SetLayer(EditorLayer.Document);
                    Graphics.SetColor(Color.White);
                    Graphics.Draw(EditorAssets.Sprites.AssetIconVfx);
                }
            }
            return;
        }

        if (!VfxSystem.IsPlaying(_handle) && _vfx != null)
        {
            if (_editorLoop)
                _handle = VfxSystem.Play(_vfx, PlayTransform, layer: EditorLayer.Document);
            else
                _playing = false;
        }
    }

    public override bool CanPlay => Emitters.Count > 0;
    public override bool IsPlaying => _playing;

    public override void Play()
    {
        if (_vfx == null) return;
        _playing = true;
        _handle = VfxSystem.Play(_vfx, PlayTransform, layer: EditorLayer.Document);
    }

    public override void Stop()
    {
        VfxSystem.Kill(_handle);
        _handle = VfxHandle.Invalid;
        _playing = false;
    }

    public override void LoadMetadata(PropertySet meta)
    {
        _rotation = meta.GetFloat("editor", "rotation", 0f);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (_rotation != 0f)
            meta.SetFloat("editor", "rotation", _rotation);
    }

    protected override void OnNotifyChange()
    {
        // ApplyChanges includes IncrementVersion, which is what base does
        ApplyChanges();
    }

    public override void Dispose()
    {
        if (_playing) Stop();
        base.Dispose();
    }

    public override void Clone(Document source)
    {
        var src = (VfxDocument)source;
        _rotation = src._rotation;
        _duration = src._duration;
        _loop = src._loop;
        ClearSelection();
        VfxRootSelected = src.VfxRootSelected;
        foreach (var i in src.SelectedEmitters) _selectedEmitters.Add(i);
        foreach (var i in src.SelectedParticles) _selectedParticles.Add(i);

        Emitters.Clear();
        foreach (var e in src.Emitters)
            Emitters.Add(VfxDocEmitter.Clone(e));

        Particles.Clear();
        foreach (var p in src.Particles)
            Particles.Add(VfxDocParticle.Clone(p));
    }

    public override void OnUndoRedo()
    {
        ApplyChanges();
    }

    private void ParseVfxFile()
    {
        var content = File.ReadAllText(Path);
        var tk = new Tokenizer(content);
        Emitters.Clear();
        Particles.Clear();

        while (!tk.IsEOF)
        {
            if (tk.ExpectIdentifier("duration"))
            {
                if (tk.ExpectLine(out var line))
                    _duration = ParseFloat(line, new VfxRange(1, 1));
            }
            else if (tk.ExpectIdentifier("loop"))
            {
                _loop = tk.ExpectBool();
            }
            else if (tk.ExpectIdentifier("particle"))
            {
                ParseParticleBlock(ref tk);
            }
            else if (tk.ExpectIdentifier("emitter"))
            {
                ParseEmitterBlock(ref tk);
            }
            else if (tk.ExpectToken(out var badToken))
            {
                ReportError(badToken.Line, $"Unexpected token '{tk.GetString(badToken)}'");
                break;
            }
        }
    }

    private void ParseParticleBlock(ref Tokenizer tk)
    {
        var name = tk.ExpectQuotedString() ?? $"particle{Particles.Count}";
        tk.ExpectDelimiter('{');

        var p = new VfxDocParticle { Name = name, SpriteRef = new DocumentRef<SpriteDocument> { Name = "square" } };
        p.Duration = VfxRange.One;
        p.Size = VfxDocFloatCurve.One;
        p.Opacity = VfxDocFloatCurve.One;
        p.Color = VfxDocColorCurve.White;

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}')) break;
            else if (tk.ExpectIdentifier("duration")) { if (tk.ExpectLine(out var v)) p.Duration = ParseFloat(v, VfxRange.One); }
            else if (tk.ExpectIdentifier("size")) { if (tk.ExpectLine(out var v)) p.Size = ParseFloatCurve(v, VfxDocFloatCurve.One); }
            else if (tk.ExpectIdentifier("speed")) { if (tk.ExpectLine(out var v)) p.Speed = ParseFloatCurve(v, VfxDocFloatCurve.Zero); }
            else if (tk.ExpectIdentifier("color")) { if (tk.ExpectLine(out var v)) p.Color = ParseColorCurve(v, VfxDocColorCurve.White); }
            else if (tk.ExpectIdentifier("opacity")) { if (tk.ExpectLine(out var v)) p.Opacity = ParseFloatCurve(v, VfxDocFloatCurve.One); }
            else if (tk.ExpectIdentifier("gravity")) { if (tk.ExpectLine(out var v)) p.Gravity = ParseFloatCurve(v, VfxDocFloatCurve.Zero); }
            else if (tk.ExpectIdentifier("rotation")) { if (tk.ExpectLine(out var v)) p.Rotation = ParseFloat(v, VfxRange.Zero); }
            else if (tk.ExpectIdentifier("rotationSpeed")) { if (tk.ExpectLine(out var v)) p.RotationSpeed = ParseFloatCurve(v, VfxDocFloatCurve.Zero); }
            else if (tk.ExpectIdentifier("alignToDirection")) { p.AlignToDirection = tk.ExpectBool(); }
            else if (tk.ExpectIdentifier("frame"))
            {
                if (tk.ExpectIdentifier(out string mode))
                    p.FrameMode = mode.Equals("random", StringComparison.OrdinalIgnoreCase)
                        ? VfxFrameMode.Random : VfxFrameMode.Time;
            }
            else if (tk.ExpectIdentifier("sprite")) { p.SpriteRef = new DocumentRef<SpriteDocument> { Name = tk.ExpectQuotedString() ?? "" }; }
            else if (tk.ExpectIdentifier("sort")) { p.Sort = (ushort)tk.ExpectInt(); }
            else { tk.ExpectToken(out _); break; }
        }

        Particles.Add(p);
    }

    private void ParseEmitterBlock(ref Tokenizer tk)
    {
        var name = tk.ExpectQuotedString() ?? $"emitter{Emitters.Count}";
        tk.ExpectDelimiter('{');

        var e = new VfxDocEmitter { Name = name };
        e.Duration = VfxRange.One;
        e.WorldSpace = true;

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}')) break;
            else if (tk.ExpectIdentifier("rate")) { if (tk.ExpectLine(out var v)) e.Rate = ParseFloatCurve(v, new VfxDocFloatCurve { Start = new VfxRange(10, 10), End = new VfxRange(10, 10), WindowEnd = 1f }); }
            else if (tk.ExpectIdentifier("burst")) { if (tk.ExpectLine(out var v)) e.Burst = ParseInt(v, VfxIntRange.Zero); }
            else if (tk.ExpectIdentifier("duration")) { if (tk.ExpectLine(out var v)) e.Duration = ParseFloat(v, VfxRange.One); }
            else if (tk.ExpectIdentifier("spread")) { tk.ExpectFloat(out e.Spread); }
            else if (tk.ExpectIdentifier("radial")) { tk.ExpectFloat(out var v); e.Radial = v; }
            else if (tk.ExpectIdentifier("spawn")) { e.Spawn = ParseSpawnDef(ref tk); }
            else if (tk.ExpectIdentifier("direction")) { tk.ExpectFloat(out e.Direction); }
            else if (tk.ExpectIdentifier("worldSpace")) { e.WorldSpace = tk.ExpectBool(); }
            else if (tk.ExpectIdentifier("particle")) { e.ParticleRef = tk.ExpectQuotedString() ?? ""; }
            else { tk.ExpectToken(out _); break; }
        }

        Emitters.Add(e);
    }

    private void ResolveSpriteRefs()
    {
        foreach (var particle in Particles)
            particle.SpriteRef.Resolve();
    }

    // --- Text file serialization ---

    public override void Save(StreamWriter sw)
    {
        sw.WriteLine($"duration {FormatRange(_duration)}");
        sw.WriteLine($"loop {_loop.ToString().ToLowerInvariant()}");

        // Particle blocks
        var written = new HashSet<string>();
        foreach (var p in Particles)
        {
            if (!written.Add(p.Name)) continue;

            sw.WriteLine();
            sw.WriteLine($"particle \"{p.Name}\" {{");
            sw.WriteLine($"  duration {FormatRange(p.Duration)}");

            if (p.Size != VfxDocFloatCurve.One)
                sw.WriteLine($"  size {FormatFloatCurve(p.Size)}");
            if (p.Speed != VfxDocFloatCurve.Zero)
                sw.WriteLine($"  speed {FormatFloatCurve(p.Speed)}");
            if (p.Color != VfxDocColorCurve.White)
                sw.WriteLine($"  color {FormatColorCurve(p.Color)}");
            if (p.Opacity != VfxDocFloatCurve.One)
                sw.WriteLine($"  opacity {FormatFloatCurve(p.Opacity)}");
            if (p.Gravity != VfxDocFloatCurve.Zero)
                sw.WriteLine($"  gravity {FormatFloatCurve(p.Gravity)}");
            if (p.Rotation != VfxRange.Zero)
                sw.WriteLine($"  rotation {FormatRange(p.Rotation)}");
            if (p.RotationSpeed != VfxDocFloatCurve.Zero)
                sw.WriteLine($"  rotationSpeed {FormatFloatCurve(p.RotationSpeed)}");
            if (p.AlignToDirection)
                sw.WriteLine($"  alignToDirection true");
            if (p.FrameMode != VfxFrameMode.Time)
                sw.WriteLine($"  frame random");
            sw.WriteLine($"  sprite \"{p.SpriteRef.Name}\"");
            if (p.Sort != 0)
                sw.WriteLine($"  sort {p.Sort}");

            sw.WriteLine("}");
        }

        // Emitter blocks
        foreach (var e in Emitters)
        {
            sw.WriteLine();
            sw.WriteLine($"emitter \"{e.Name}\" {{");
            sw.WriteLine($"  rate {FormatFloatCurve(e.Rate)}");
            sw.WriteLine($"  burst {FormatIntRange(e.Burst)}");
            sw.WriteLine($"  duration {FormatRange(e.Duration)}");
            sw.WriteLine($"  particle \"{e.ParticleRef}\"");

            if (e.Spawn.Shape != VfxSpawnShape.Point || e.Spawn.Offset != Vector2.Zero)
                FormatSpawnDef(sw, e.Spawn);
            if (e.Direction != 0)
                sw.WriteLine($"  direction {FormatFloat(e.Direction)}");
            if (e.Spread != 0)
                sw.WriteLine($"  spread {FormatFloat(e.Spread)}");
            if (e.Radial != 0)
                sw.WriteLine($"  radial {FormatFloat(e.Radial)}");
            if (!e.WorldSpace)
                sw.WriteLine("  worldSpace false");

            sw.WriteLine("}");
        }
    }

    // --- Binary serialization ---

    private void WriteBinary(string outputPath)
    {
        // Build the runtime emitter array first
        // BuildVfx resolves sprites, so just reuse the built asset's emitter defs
        var emitterDefs = _vfx?.EmitterDefs ?? [];

        using var writer = new BinaryWriter(File.OpenWrite(outputPath));
        writer.WriteAssetHeader(AssetType.Vfx, Vfx.Version);

        var bounds = CalculateBounds(emitterDefs);
        writer.Write(bounds.X);
        writer.Write(bounds.Y);
        writer.Write(bounds.Width);
        writer.Write(bounds.Height);

        writer.Write(_duration.Min);
        writer.Write(_duration.Max);
        writer.Write(_loop);

        writer.Write(emitterDefs.Length);

        for (var i = 0; i < emitterDefs.Length; i++)
        {
            ref var e = ref emitterDefs[i];

            WriteFloatCurve(writer, e.Rate);
            writer.Write(e.Burst.Min);
            writer.Write(e.Burst.Max);
            writer.Write(e.Duration.Min);
            writer.Write(e.Duration.Max);
            WriteSpawnDef(writer, ref e.Spawn);
            writer.Write(e.Direction);
            writer.Write(e.Spread);
            writer.Write(e.Radial);
            writer.Write(e.WorldSpace);

            ref var p = ref e.Particle;
            writer.Write(p.Duration.Min);
            writer.Write(p.Duration.Max);
            WriteFloatCurve(writer, p.Size);
            WriteFloatCurve(writer, p.Speed);
            WriteColorCurve(writer, p.Color);
            WriteFloatCurve(writer, p.Opacity);
            WriteFloatCurve(writer, p.Gravity);
            writer.Write(p.Rotation.Min);
            writer.Write(p.Rotation.Max);
            WriteFloatCurve(writer, p.RotationSpeed);
            writer.Write(p.AlignToDirection);
            writer.Write((byte)p.FrameMode);

            var particle = FindParticle(Emitters[i].ParticleRef);
            var spriteName = particle?.SpriteRef.Name ?? "";
            var spriteBytes = System.Text.Encoding.UTF8.GetBytes(spriteName);
            writer.Write(spriteBytes.Length);
            if (spriteBytes.Length > 0)
                writer.Write(spriteBytes);
            writer.Write(p.Sort);
        }
    }

    private static void WriteSpawnDef(BinaryWriter writer, ref VfxSpawnDef spawn)
    {
        writer.Write((byte)spawn.Shape);
        writer.Write(spawn.Offset.X);
        writer.Write(spawn.Offset.Y);
        switch (spawn.Shape)
        {
            case VfxSpawnShape.Circle:
                writer.Write(spawn.Circle.Radius);
                writer.Write(spawn.Circle.InnerRadius);
                break;
            case VfxSpawnShape.Box:
                writer.Write(spawn.Box.Size.X);
                writer.Write(spawn.Box.Size.Y);
                writer.Write(spawn.Box.InnerSize.X);
                writer.Write(spawn.Box.InnerSize.Y);
                writer.Write(spawn.Box.Rotation);
                break;
        }
    }

    private static void WriteFloatCurve(BinaryWriter writer, VfxFloatCurve curve)
    {
        writer.Write(curve.Start.Min);
        writer.Write(curve.Start.Max);
        writer.Write(curve.End.Min);
        writer.Write(curve.End.Max);
        for (var i = 0; i < VfxCurveLut.Samples; i++)
            writer.Write(curve.Lut[i]);
    }

    private static void WriteColorCurve(BinaryWriter writer, VfxColorCurve curve)
    {
        writer.Write(curve.Start.Min.R);
        writer.Write(curve.Start.Min.G);
        writer.Write(curve.Start.Min.B);
        writer.Write(curve.Start.Min.A);
        writer.Write(curve.Start.Max.R);
        writer.Write(curve.Start.Max.G);
        writer.Write(curve.Start.Max.B);
        writer.Write(curve.Start.Max.A);
        writer.Write(curve.End.Min.R);
        writer.Write(curve.End.Min.G);
        writer.Write(curve.End.Min.B);
        writer.Write(curve.End.Min.A);
        writer.Write(curve.End.Max.R);
        writer.Write(curve.End.Max.G);
        writer.Write(curve.End.Max.B);
        writer.Write(curve.End.Max.A);
        for (var i = 0; i < VfxCurveLut.Samples; i++)
            writer.Write(curve.Lut[i]);
    }

    internal static float SampleAuthored(
        VfxCurveType curveType, VfxEaseType easeType,
        float windowBegin, float windowEnd,
        float t)
    {
        if (easeType == VfxEaseType.None) return t;

        if (t <= windowBegin) return 0f;

        var span = windowEnd - windowBegin;
        if (span <= 0f) return 0f;

        if (t >= windowEnd) return 1f;

        var u = (t - windowBegin) / span;

        return easeType switch
        {
            VfxEaseType.In => EvaluateAlgebraic(curveType, u),
            VfxEaseType.Out => 1f - EvaluateAlgebraic(curveType, 1f - u),
            VfxEaseType.InOut => u < 0.5f
                ? EvaluateAlgebraic(curveType, u * 2f) * 0.5f
                : 0.5f + (1f - EvaluateAlgebraic(curveType, (1f - u) * 2f)) * 0.5f,
            _ => t,
        };
    }

    private const float BackOvershoot = 1.70158f;
    private const float ElasticPeriod = 0.3f;
    private const float ElasticAmplitude = 1f;

    internal static float EvaluateAlgebraic(VfxCurveType type, float t)
    {
        t = Math.Clamp(t, 0f, 1f);
        return type switch
        {
            VfxCurveType.Linear => t,
            VfxCurveType.Quadratic => t * t,
            VfxCurveType.Cubic => t * t * t,
            VfxCurveType.Quartic => t * t * t * t,
            VfxCurveType.Sine => 1f - MathF.Cos(t * MathF.PI * 0.5f),
            VfxCurveType.Back => t * t * ((BackOvershoot + 1f) * t - BackOvershoot),
            VfxCurveType.Elastic => EvaluateElastic(t),
            VfxCurveType.Bounce => 1f - EvaluateBounceOut(1f - t),
            VfxCurveType.Bell => MathF.Sin(t * MathF.PI),
            _ => t
        };
    }

    private static float EvaluateElastic(float t)
    {
        if (t <= 0f) return 0f;
        if (t >= 1f) return 1f;
        var s = ElasticPeriod / 4f;
        return -ElasticAmplitude * MathF.Pow(2f, 10f * (t - 1f)) * MathF.Sin((t - 1f - s) * (2f * MathF.PI) / ElasticPeriod);
    }

    // Bounce-out: t in [0,1], output in [0,1] with multi-bounce decay (classic Penner).
    private static float EvaluateBounceOut(float t)
    {
        const float n = 7.5625f;
        const float d = 2.75f;
        if (t < 1f / d) return n * t * t;
        if (t < 2f / d) { t -= 1.5f / d; return n * t * t + 0.75f; }
        if (t < 2.5f / d) { t -= 2.25f / d; return n * t * t + 0.9375f; }
        t -= 2.625f / d; return n * t * t + 0.984375f;
    }

    // --- Value parsers ---

    internal static VfxRange ParseFloat(string str, VfxRange defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);

        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectFloat(out float min)) return defaultValue;
            if (!tk.ExpectDelimiter(',')) return defaultValue;
            if (!tk.ExpectFloat(out float max)) return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxRange(MathF.Min(min, max), MathF.Max(min, max));
        }

        if (tk.ExpectFloat(out float value))
            return new VfxRange(value, value);

        return defaultValue;
    }

    internal static VfxIntRange ParseInt(string str, VfxIntRange defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);

        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectInt(out int min)) return defaultValue;
            if (!tk.ExpectDelimiter(',')) return defaultValue;
            if (!tk.ExpectInt(out int max)) return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxIntRange(Math.Min(min, max), Math.Max(min, max));
        }

        if (tk.ExpectInt(out int value))
            return new VfxIntRange(value, value);

        return defaultValue;
    }

    private static VfxVec2Range ParseVec2(string str, VfxVec2Range defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);

        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectVec2(out Vector2 min)) return defaultValue;
            if (!tk.ExpectDelimiter(',')) return defaultValue;
            if (!tk.ExpectVec2(out Vector2 max)) return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxVec2Range(
                new Vector2(MathF.Min(min.X, max.X), MathF.Min(min.Y, max.Y)),
                new Vector2(MathF.Max(min.X, max.X), MathF.Max(min.Y, max.Y)));
        }

        if (tk.ExpectVec2(out Vector2 value))
            return new VfxVec2Range(value, value);

        return defaultValue;
    }

    private static VfxSpawnDef ParseSpawnDef(ref Tokenizer tk)
    {
        var def = new VfxSpawnDef();

        if (tk.ExpectIdentifier("circle"))
            def.Shape = VfxSpawnShape.Circle;
        else if (tk.ExpectIdentifier("box"))
            def.Shape = VfxSpawnShape.Box;
        else if (tk.ExpectIdentifier("point"))
            def.Shape = VfxSpawnShape.Point;
        else
            return VfxSpawnDef.Default;

        if (!tk.ExpectDelimiter('{'))
            return def;

        while (!tk.IsEOF)
        {
            if (tk.ExpectDelimiter('}')) break;
            else if (tk.ExpectIdentifier("offset")) { tk.ExpectVec2(out var v); def.Offset = v; }
            else if (def.Shape == VfxSpawnShape.Circle && tk.ExpectIdentifier("radius")) { tk.ExpectFloat(out var v); def.Circle.Radius = v; }
            else if (def.Shape == VfxSpawnShape.Circle && tk.ExpectIdentifier("innerRadius")) { tk.ExpectFloat(out var v); def.Circle.InnerRadius = v; }
            else if (def.Shape == VfxSpawnShape.Box && tk.ExpectIdentifier("size")) { tk.ExpectVec2(out var v); def.Box.Size = v; }
            else if (def.Shape == VfxSpawnShape.Box && tk.ExpectIdentifier("innerSize")) { tk.ExpectVec2(out var v); def.Box.InnerSize = v; }
            else if (def.Shape == VfxSpawnShape.Box && tk.ExpectIdentifier("rotation")) { tk.ExpectFloat(out var v); def.Box.Rotation = v; }
            else { tk.ExpectToken(out _); break; }
        }

        return def;
    }

    private static VfxDocFloatCurve ParseFloatCurve(string str, VfxDocFloatCurve defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);
        var curve = new VfxDocFloatCurve { WindowBegin = 0f, WindowEnd = 1f };

        if (!ParseFloatValue(ref tk, out curve.Start)) return defaultValue;

        if (!tk.ExpectDelimiter('='))
        {
            curve.End = curve.Start;
            return curve;
        }

        if (!tk.ExpectDelimiter('>')) return defaultValue;
        if (!ParseFloatValue(ref tk, out curve.End)) return defaultValue;

        if (tk.ExpectDelimiter(':'))
            ParseCurveSpec(ref tk, ref curve.CurveType, ref curve.EaseType,
                ref curve.WindowBegin, ref curve.WindowEnd);

        return curve;
    }

    private static void ParseCurveSpec(
        ref Tokenizer tk,
        ref VfxCurveType curveType, ref VfxEaseType easeType,
        ref float windowBegin, ref float windowEnd)
    {
        if (tk.ExpectDelimiter(','))
        {
            if (!TryReadType(ref tk, out var outType))
                return;
            curveType = outType;
            easeType = VfxEaseType.Out;
            TryReadWindow(ref tk, ref windowBegin, ref windowEnd);
            return;
        }

        if (!TryReadIdentifier(ref tk, out var firstName))
            return;

        var firstAsDirection = ParseEaseDirection(firstName);
        if (firstAsDirection != null)
        {
            easeType = firstAsDirection.Value;
            if (TryReadIdentifier(ref tk, out var typeName))
                curveType = ParseCurveType(typeName);
            TryReadWindow(ref tk, ref windowBegin, ref windowEnd);
            return;
        }

        curveType = ParseCurveType(firstName);
        easeType = VfxEaseType.In;

        if (tk.ExpectDelimiter(','))
        {
            if (TryReadIdentifier(ref tk, out _))
                easeType = VfxEaseType.InOut;
        }
        else if (TryReadIdentifier(ref tk, out var directionName))
        {
            var dir = ParseEaseDirection(directionName);
            if (dir != null)
                easeType = dir.Value;
        }

        TryReadWindow(ref tk, ref windowBegin, ref windowEnd);
    }

    private static bool TryReadIdentifier(ref Tokenizer tk, out string name)
    {
        return tk.ExpectIdentifier(out name);
    }

    private static bool TryReadType(ref Tokenizer tk, out VfxCurveType type)
    {
        if (!tk.ExpectIdentifier(out string name)) { type = VfxCurveType.Linear; return false; }
        type = ParseCurveType(name);
        return true;
    }

    private static VfxEaseType? ParseEaseDirection(string name) => name.ToLowerInvariant() switch
    {
        "in" or "easein" => VfxEaseType.In,
        "out" or "easeout" => VfxEaseType.Out,
        "inout" or "easeinout" => VfxEaseType.InOut,
        "none" => VfxEaseType.None,
        _ => null,
    };

    private static bool TryReadWindow(ref Tokenizer tk, ref float windowBegin, ref float windowEnd)
    {
        if (!tk.ExpectDelimiter('@') || !tk.ExpectVec2(out Vector2 window))
            return false;
        windowBegin = Math.Clamp(window.X, 0f, 1f);
        windowEnd = Math.Clamp(window.Y, 0f, 1f);
        if (windowEnd < windowBegin) (windowBegin, windowEnd) = (windowEnd, windowBegin);
        return true;
    }

    private static bool ParseFloatValue(ref Tokenizer tk, out VfxRange value)
    {
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectFloat(out float min)) { value = default; return false; }
            if (!tk.ExpectDelimiter(',')) { value = default; return false; }
            if (!tk.ExpectFloat(out float max)) { value = default; return false; }
            tk.ExpectDelimiter(']');
            value = new VfxRange(MathF.Min(min, max), MathF.Max(min, max));
            return true;
        }

        if (tk.ExpectFloat(out float v))
        {
            value = new VfxRange(v, v);
            return true;
        }

        value = default;
        return false;
    }

    private static VfxDocColorCurve ParseColorCurve(string str, VfxDocColorCurve defaultValue)
    {
        if (string.IsNullOrEmpty(str)) return defaultValue;
        var tk = new Tokenizer(str);
        var curve = new VfxDocColorCurve { WindowBegin = 0f, WindowEnd = 1f };

        if (!ParseColorValue(ref tk, out curve.Start)) return defaultValue;

        if (!tk.ExpectDelimiter('='))
        {
            curve.End = curve.Start;
            return curve;
        }

        if (!tk.ExpectDelimiter('>')) return defaultValue;
        if (!ParseColorValue(ref tk, out curve.End)) return defaultValue;

        if (tk.ExpectDelimiter(':'))
            ParseCurveSpec(ref tk, ref curve.CurveType, ref curve.EaseType,
                ref curve.WindowBegin, ref curve.WindowEnd);

        return curve;
    }

    private static bool ParseColorValue(ref Tokenizer tk, out VfxColorRange value)
    {
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectColor(out Color min)) { value = default; return false; }
            if (!tk.ExpectDelimiter(',')) { value = default; return false; }
            if (!tk.ExpectColor(out Color max)) { value = default; return false; }
            tk.ExpectDelimiter(']');
            value = new VfxColorRange(min, max);
            return true;
        }

        if (tk.ExpectColor(out Color c))
        {
            value = new VfxColorRange(c, c);
            return true;
        }

        value = default;
        return false;
    }

    private static VfxCurveType ParseCurveType(string name) => name.ToLowerInvariant() switch
    {
        "linear" => VfxCurveType.Linear,
        "quadratic" => VfxCurveType.Quadratic,
        "cubic" => VfxCurveType.Cubic,
        "quartic" => VfxCurveType.Quartic,
        "sine" => VfxCurveType.Sine,
        "smoothstep" => VfxCurveType.Quadratic,
        "back" => VfxCurveType.Back,
        "elastic" => VfxCurveType.Elastic,
        "bounce" => VfxCurveType.Bounce,
        "bell" => VfxCurveType.Bell,
        _ => VfxCurveType.Linear
    };

    // --- Formatters ---

    internal static string FormatFloat(float v) =>
        v == (int)v ? ((int)v).ToString() : v.ToString("G");

    internal static string FormatRange(VfxRange r) =>
        r.Min == r.Max ? FormatFloat(r.Min) : $"[{FormatFloat(r.Min)}, {FormatFloat(r.Max)}]";

    internal static string FormatIntRange(VfxIntRange r) =>
        r.Min == r.Max ? r.Min.ToString() : $"[{r.Min}, {r.Max}]";

    internal static string FormatVec2Range(VfxVec2Range r)
    {
        if (r.Min == r.Max)
            return $"({FormatFloat(r.Min.X)}, {FormatFloat(r.Min.Y)})";
        return $"[({FormatFloat(r.Min.X)}, {FormatFloat(r.Min.Y)}), ({FormatFloat(r.Max.X)}, {FormatFloat(r.Max.Y)})]";
    }

    internal static string FormatFloatCurve(VfxDocFloatCurve c)
    {
        var start = FormatRange(c.Start);
        var end = FormatRange(c.End);

        if (c.Start.Min == c.End.Min && c.Start.Max == c.End.Max)
            return start;

        return $"{start}=>{end}:{FormatCurveSuffix(c.CurveType, c.EaseType, c.WindowBegin, c.WindowEnd)}";
    }

    internal static string FormatColorCurve(VfxDocColorCurve c)
    {
        var start = FormatColorRange(c.Start);
        var end = FormatColorRange(c.End);

        if (c.Start.Min == c.End.Min && c.Start.Max == c.End.Max)
            return start;

        return $"{start}=>{end}:{FormatCurveSuffix(c.CurveType, c.EaseType, c.WindowBegin, c.WindowEnd)}";
    }

    private static string FormatCurveSuffix(
        VfxCurveType curveType, VfxEaseType easeType,
        float windowBegin, float windowEnd)
    {
        string body = easeType switch
        {
            VfxEaseType.None => "none",
            VfxEaseType.In => FormatCurveType(curveType),
            VfxEaseType.Out => $"{FormatCurveType(curveType)} out",
            VfxEaseType.InOut => $"{FormatCurveType(curveType)} inout",
            _ => "linear",
        };

        if (windowBegin != 0f || windowEnd != 1f)
            return $"{body}@({FormatFloat(windowBegin)}, {FormatFloat(windowEnd)})";
        return body;
    }

    private static string FormatColorRange(VfxColorRange r)
    {
        if (r.Min == r.Max) return FormatColor(r.Min);
        return $"[{FormatColor(r.Min)}, {FormatColor(r.Max)}]";
    }

    internal static string FormatColor(Color c)
    {
        // HDR: if any component > 1, use float format
        if (c.R > 1f || c.G > 1f || c.B > 1f)
        {
            return c.A >= 1f
                ? $"hdr({FormatFloat(c.R)}, {FormatFloat(c.G)}, {FormatFloat(c.B)})"
                : $"hdra({FormatFloat(c.R)}, {FormatFloat(c.G)}, {FormatFloat(c.B)}, {FormatFloat(c.A)})";
        }

        var r = (byte)(Math.Clamp(c.R, 0, 1) * 255);
        var g = (byte)(Math.Clamp(c.G, 0, 1) * 255);
        var b = (byte)(Math.Clamp(c.B, 0, 1) * 255);
        var a = (byte)(Math.Clamp(c.A, 0, 1) * 255);
        return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    internal static string FormatCurveType(VfxCurveType type) => type switch
    {
        VfxCurveType.Linear => "linear",
        VfxCurveType.Quadratic => "quadratic",
        VfxCurveType.Cubic => "cubic",
        VfxCurveType.Quartic => "quartic",
        VfxCurveType.Sine => "sine",
        VfxCurveType.Back => "back",
        VfxCurveType.Elastic => "elastic",
        VfxCurveType.Bounce => "bounce",
        VfxCurveType.Bell => "bell",
        _ => "linear"
    };

    private static string FormatVec2(Vector2 v) => $"({FormatFloat(v.X)}, {FormatFloat(v.Y)})";

    private static void FormatSpawnDef(StreamWriter sw, VfxSpawnDef spawn)
    {
        var shapeName = spawn.Shape switch
        {
            VfxSpawnShape.Circle => "circle",
            VfxSpawnShape.Box => "box",
            _ => "point"
        };

        // Check if we need a block (any non-default properties)
        var needsBlock = spawn.Offset != Vector2.Zero;
        if (spawn.Shape == VfxSpawnShape.Circle)
            needsBlock = needsBlock || spawn.Circle.Radius != 0 || spawn.Circle.InnerRadius != 0;
        else if (spawn.Shape == VfxSpawnShape.Box)
            needsBlock = needsBlock || spawn.Box.Size != Vector2.Zero || spawn.Box.InnerSize != Vector2.Zero || spawn.Box.Rotation != 0;

        if (!needsBlock)
        {
            sw.WriteLine($"  spawn {shapeName}");
            return;
        }

        sw.WriteLine($"  spawn {shapeName} {{");
        if (spawn.Offset != Vector2.Zero)
            sw.WriteLine($"    offset {FormatVec2(spawn.Offset)}");

        if (spawn.Shape == VfxSpawnShape.Circle)
        {
            if (spawn.Circle.Radius != 0)
                sw.WriteLine($"    radius {FormatFloat(spawn.Circle.Radius)}");
            if (spawn.Circle.InnerRadius != 0)
                sw.WriteLine($"    innerRadius {FormatFloat(spawn.Circle.InnerRadius)}");
        }
        else if (spawn.Shape == VfxSpawnShape.Box)
        {
            if (spawn.Box.Size != Vector2.Zero)
                sw.WriteLine($"    size {FormatVec2(spawn.Box.Size)}");
            if (spawn.Box.InnerSize != Vector2.Zero)
                sw.WriteLine($"    innerSize {FormatVec2(spawn.Box.InnerSize)}");
            if (spawn.Box.Rotation != 0)
                sw.WriteLine($"    rotation {FormatFloat(spawn.Box.Rotation)}");
        }

        sw.WriteLine("  }");
    }
}
