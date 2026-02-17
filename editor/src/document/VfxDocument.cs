//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Numerics;
using System.Text;

namespace NoZ.Editor;

public class VfxDocument : Document
{
    public override bool CanSave => true;

    private const int MaxEmittersPerVfx = 32;

    private Vfx? _vfx;
    private VfxHandle _handle = VfxHandle.Invalid;
    private bool _playing;
    private VfxEmitterDef[] _emitterDefs = [];
    private string[] _emitterNames = [];
    private VfxRange _duration;
    private bool _loop;

    public int EmitterCount => _emitterDefs.Length;
    public int SelectedEmitterIndex { get; set; }
    public ref VfxRange Duration => ref _duration;
    public ref bool Loop => ref _loop;

    public ref VfxEmitterDef GetEmitterDef(int index) => ref _emitterDefs[index];
    public string GetEmitterName(int index) => _emitterNames[index];

    public void AddEmitter(string name)
    {
        if (_emitterDefs.Length >= MaxEmittersPerVfx)
            return;

        var emitter = new VfxEmitterDef
        {
            Rate = new VfxIntRange(10, 10),
            Duration = VfxRange.One,
            Angle = new VfxRange(0, 360),
            Particle = new VfxParticleDef
            {
                Duration = new VfxRange(0.5f, 1.0f),
                Size = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = new VfxRange(0.5f, 0.5f), End = new VfxRange(0f, 0.1f) },
                Speed = new VfxFloatCurve { Type = VfxCurveType.Linear, Start = new VfxRange(20, 40), End = new VfxRange(5, 10) },
                Color = VfxColorCurve.White,
                Opacity = new VfxFloatCurve { Type = VfxCurveType.EaseOut, Start = VfxRange.One, End = VfxRange.Zero },
            }
        };

        _emitterDefs = [.. _emitterDefs, emitter];
        _emitterNames = [.. _emitterNames, name];
        SelectedEmitterIndex = _emitterDefs.Length - 1;
        ApplyChanges();
    }

    public void RemoveEmitter(int index)
    {
        if (index < 0 || index >= _emitterDefs.Length)
            return;

        var defs = _emitterDefs.ToList();
        var names = _emitterNames.ToList();
        defs.RemoveAt(index);
        names.RemoveAt(index);
        _emitterDefs = defs.ToArray();
        _emitterNames = names.ToArray();

        if (SelectedEmitterIndex >= _emitterDefs.Length)
            SelectedEmitterIndex = _emitterDefs.Length - 1;

        ApplyChanges();
    }

    public void ApplyChanges()
    {
        MarkModified();
        BuildVfx();

        if (_playing && _vfx != null)
        {
            VfxSystem.Stop(_handle);
            _handle = VfxSystem.Play(_vfx, Position);
        }
    }

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef
        {
            Type = AssetType.Vfx,
            Extension = ".vfx",
            Factory = () => new VfxDocument(),
            EditorFactory = doc => new VfxEditor((VfxDocument)doc),
            NewFile = writer =>
            {
                writer.WriteLine("[vfx]");
                writer.WriteLine("duration = 1");
                writer.WriteLine("loop = false");
                writer.WriteLine();
                writer.WriteLine("[emitters]");
                writer.WriteLine("default");
                writer.WriteLine();
                writer.WriteLine("[default]");
                writer.WriteLine("rate = 10");
                writer.WriteLine("burst = 0");
                writer.WriteLine("duration = 1");
                writer.WriteLine("angle = [0, 360]");
                writer.WriteLine();
                writer.WriteLine("[default.particle]");
                writer.WriteLine("duration = [0.5, 1.0]");
                writer.WriteLine("size = 0.5=>[0.0, 0.1]:easeout");
                writer.WriteLine("speed = [20, 40]=>[5, 10]:linear");
                writer.WriteLine("color = white");
                writer.WriteLine("opacity = 1.0=>0.0:easeout");
            },
            Icon = () => EditorAssets.Sprites.AssetIconVfx
        });
    }

    public override void Load()
    {
        ParseVfxFile();
        BuildVfx();
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        // Re-parse the text file (hot-reload entry point)
        ParseVfxFile();

        // Write binary output
        WriteBinary(outputPath);

        // Rebuild in-memory asset for editor preview
        var wasPlaying = _playing;
        if (_playing)
        {
            VfxSystem.Stop(_handle);
            _handle = VfxHandle.Invalid;
        }

        BuildVfx();

        // Restart if was playing
        if (wasPlaying && _vfx != null)
        {
            _handle = VfxSystem.Play(_vfx, Position);
            _playing = true;
        }
    }

    public override void Draw()
    {
        if (!_playing || _emitterDefs.Length == 0)
        {
            using (Graphics.PushState())
            {
                Graphics.SetLayer(EditorLayer.Document);
                Graphics.SetColor(Color.White);
                Graphics.Draw(EditorAssets.Sprites.AssetIconVfx);
            }
            return;
        }

        // Restart if the effect finished
        if (!VfxSystem.IsPlaying(_handle) && _vfx != null)
            _handle = VfxSystem.Play(_vfx, Position);
    }

    public override bool CanPlay => _emitterDefs.Length > 0;
    public override bool IsPlaying => _playing;

    public override void Play()
    {
        if (_vfx == null)
            return;

        _playing = true;
        _handle = VfxSystem.Play(_vfx, Position);
    }

    public override void Stop()
    {
        VfxSystem.Stop(_handle);
        _handle = VfxHandle.Invalid;
        _playing = false;
    }

    public override void Dispose()
    {
        if (_playing)
            Stop();

        base.Dispose();
    }

    // --- Text file parsing ---

    private void ParseVfxFile()
    {
        var content = File.ReadAllText(Path);
        var props = PropertySet.Load(content);
        if (props == null)
            return;

        _duration = ParseFloat(props.GetString("vfx", "duration", "1.0"), new VfxRange(1, 1));
        _loop = props.GetBool("vfx", "loop", false);

        var parsedNames = props.GetKeys("emitters").ToArray();
        var emitters = new List<VfxEmitterDef>();
        var names = new List<string>();

        foreach (var emitterName in parsedNames)
        {
            if (string.IsNullOrWhiteSpace(emitterName))
                continue;

            if (!props.HasGroup(emitterName))
                continue;

            var particleSection = props.GetString(emitterName, "particle", "");
            if (string.IsNullOrEmpty(particleSection))
                particleSection = emitterName + ".particle";

            if (!props.HasGroup(particleSection))
                continue;

            var emitter = new VfxEmitterDef();

            emitter.Rate = ParseInt(props.GetString(emitterName, "rate", "0"), VfxIntRange.Zero);
            emitter.Burst = ParseInt(props.GetString(emitterName, "burst", "0"), VfxIntRange.Zero);
            emitter.Duration = ParseFloat(props.GetString(emitterName, "duration", "1.0"), VfxRange.One);
            emitter.Angle = ParseFloat(props.GetString(emitterName, "angle", "0"), new VfxRange(0, 360));
            emitter.Spawn = ParseVec2(props.GetString(emitterName, "spawn", "(0, 0)"), VfxVec2Range.Zero);
            emitter.Direction = ParseVec2(props.GetString(emitterName, "direction", "(0, 0)"), VfxVec2Range.Zero);

            ref var p = ref emitter.Particle;
            p.Duration = ParseFloat(props.GetString(particleSection, "duration", "1.0"), VfxRange.One);
            p.Size = ParseFloatCurve(props.GetString(particleSection, "size", "1.0"), VfxFloatCurve.One);
            p.Speed = ParseFloatCurve(props.GetString(particleSection, "speed", "0"), VfxFloatCurve.Zero);
            p.Color = ParseColorCurve(props.GetString(particleSection, "color", "white"), VfxColorCurve.White);
            p.Opacity = ParseFloatCurve(props.GetString(particleSection, "opacity", "1.0"), VfxFloatCurve.One);
            p.Gravity = ParseVec2(props.GetString(particleSection, "gravity", "(0, 0)"), VfxVec2Range.Zero);
            p.Drag = ParseFloat(props.GetString(particleSection, "drag", "0"), VfxRange.Zero);
            p.Rotation = ParseFloatCurve(props.GetString(particleSection, "rotation", "0"), VfxFloatCurve.Zero);

            emitters.Add(emitter);
            names.Add(emitterName);

            if (emitters.Count >= MaxEmittersPerVfx)
                break;
        }

        _emitterDefs = emitters.ToArray();
        _emitterNames = names.ToArray();
    }

    private void BuildVfx()
    {
        _vfx = new Vfx(Name)
        {
            Duration = _duration,
            Loop = _loop,
            EmitterDefs = _emitterDefs,
            Bounds = CalculateBounds()
        };
    }

    private Rect CalculateBounds()
    {
        var bounds = new Rect(-0.5f, -0.5f, 1f, 1f);

        for (var i = 0; i < _emitterDefs.Length; i++)
        {
            ref var e = ref _emitterDefs[i];
            ref var p = ref e.Particle;

            var ssmax = MathF.Max(p.Size.Start.Min, p.Size.Start.Max);
            var semax = MathF.Max(p.Size.End.Min, p.Size.End.Max);
            var smax = MathF.Max(ssmax, semax);

            var speedMax = MathF.Max(p.Speed.Start.Max, p.Speed.End.Max);
            var durationMax = p.Duration.Max;
            var extent = speedMax * durationMax + smax;

            var minX = MathF.Min(e.Spawn.Min.X, e.Spawn.Max.X) - extent;
            var minY = MathF.Min(e.Spawn.Min.Y, e.Spawn.Max.Y) - extent;
            var maxX = MathF.Max(e.Spawn.Min.X, e.Spawn.Max.X) + extent;
            var maxY = MathF.Max(e.Spawn.Min.Y, e.Spawn.Max.Y) + extent;

            if (i == 0)
                bounds = new Rect(minX, minY, maxX - minX, maxY - minY);
            else
                bounds = Rect.Union(bounds, new Rect(minX, minY, maxX - minX, maxY - minY));
        }

        return bounds;
    }

    // --- Binary serialization ---

    private void WriteBinary(string outputPath)
    {
        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Vfx, Vfx.Version);

        // Bounds
        var bounds = CalculateBounds();
        writer.Write(bounds.X);
        writer.Write(bounds.Y);
        writer.Write(bounds.Width);
        writer.Write(bounds.Height);

        // Duration
        writer.Write(_duration.Min);
        writer.Write(_duration.Max);
        writer.Write(_loop);

        // Emitter count
        writer.Write(_emitterDefs.Length);

        for (var i = 0; i < _emitterDefs.Length; i++)
        {
            ref var e = ref _emitterDefs[i];

            writer.Write(e.Rate.Min);
            writer.Write(e.Rate.Max);
            writer.Write(e.Burst.Min);
            writer.Write(e.Burst.Max);
            writer.Write(e.Duration.Min);
            writer.Write(e.Duration.Max);
            writer.Write(e.Angle.Min);
            writer.Write(e.Angle.Max);
            writer.Write(e.Spawn.Min.X);
            writer.Write(e.Spawn.Min.Y);
            writer.Write(e.Spawn.Max.X);
            writer.Write(e.Spawn.Max.Y);
            writer.Write(e.Direction.Min.X);
            writer.Write(e.Direction.Min.Y);
            writer.Write(e.Direction.Max.X);
            writer.Write(e.Direction.Max.Y);

            ref var p = ref e.Particle;
            writer.Write(p.Duration.Min);
            writer.Write(p.Duration.Max);
            WriteFloatCurve(writer, p.Size);
            WriteFloatCurve(writer, p.Speed);
            WriteColorCurve(writer, p.Color);
            WriteFloatCurve(writer, p.Opacity);
            writer.Write(p.Gravity.Min.X);
            writer.Write(p.Gravity.Min.Y);
            writer.Write(p.Gravity.Max.X);
            writer.Write(p.Gravity.Max.Y);
            writer.Write(p.Drag.Min);
            writer.Write(p.Drag.Max);
            WriteFloatCurve(writer, p.Rotation);

            // mesh name (empty)
            writer.Write(0);
        }
    }

    private static void WriteFloatCurve(BinaryWriter writer, VfxFloatCurve curve)
    {
        writer.Write((byte)curve.Type);
        writer.Write(curve.Start.Min);
        writer.Write(curve.Start.Max);
        writer.Write(curve.End.Min);
        writer.Write(curve.End.Max);
    }

    private static void WriteColorCurve(BinaryWriter writer, VfxColorCurve curve)
    {
        writer.Write((byte)curve.Type);
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
    }

    // --- Value parsers ---

    private static VfxRange ParseFloat(string str, VfxRange defaultValue)
    {
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        // Try range: [min, max]
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectFloat(out float min))
                return defaultValue;
            if (!tk.ExpectDelimiter(','))
                return defaultValue;
            if (!tk.ExpectFloat(out float max))
                return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxRange(MathF.Min(min, max), MathF.Max(min, max));
        }

        // Single value
        if (tk.ExpectFloat(out float value))
            return new VfxRange(value, value);

        return defaultValue;
    }

    private static VfxIntRange ParseInt(string str, VfxIntRange defaultValue)
    {
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        // Try range: [min, max]
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectInt(out int min))
                return defaultValue;
            if (!tk.ExpectDelimiter(','))
                return defaultValue;
            if (!tk.ExpectInt(out int max))
                return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxIntRange(Math.Min(min, max), Math.Max(min, max));
        }

        // Single value
        if (tk.ExpectInt(out int value))
            return new VfxIntRange(value, value);

        return defaultValue;
    }

    private static VfxVec2Range ParseVec2(string str, VfxVec2Range defaultValue)
    {
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        // Try range: [(x1,y1), (x2,y2)]
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectVec2(out Vector2 min))
                return defaultValue;
            if (!tk.ExpectDelimiter(','))
                return defaultValue;
            if (!tk.ExpectVec2(out Vector2 max))
                return defaultValue;
            tk.ExpectDelimiter(']');
            return new VfxVec2Range(
                new Vector2(MathF.Min(min.X, max.X), MathF.Min(min.Y, max.Y)),
                new Vector2(MathF.Max(min.X, max.X), MathF.Max(min.Y, max.Y)));
        }

        // Single value
        if (tk.ExpectVec2(out Vector2 value))
            return new VfxVec2Range(value, value);

        return defaultValue;
    }

    private static VfxFloatCurve ParseFloatCurve(string str, VfxFloatCurve defaultValue)
    {
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        var curve = new VfxFloatCurve { Type = VfxCurveType.Linear };

        // Parse start value
        if (!ParseFloatValue(ref tk, out curve.Start))
            return defaultValue;

        // Check for =>
        if (!tk.ExpectDelimiter('='))
        {
            curve.End = curve.Start;
            return curve;
        }

        if (!tk.ExpectDelimiter('>'))
            return defaultValue;

        // Parse end value
        if (!ParseFloatValue(ref tk, out curve.End))
            return defaultValue;

        // Check for :curvetype
        if (tk.ExpectDelimiter(':'))
        {
            if (tk.ExpectIdentifier(out string curveType))
                curve.Type = ParseCurveType(curveType);
        }

        return curve;
    }

    private static bool ParseFloatValue(ref Tokenizer tk, out VfxRange value)
    {
        // Try range: [min, max]
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectFloat(out float min))
            {
                value = default;
                return false;
            }
            if (!tk.ExpectDelimiter(','))
            {
                value = default;
                return false;
            }
            if (!tk.ExpectFloat(out float max))
            {
                value = default;
                return false;
            }
            tk.ExpectDelimiter(']');
            value = new VfxRange(MathF.Min(min, max), MathF.Max(min, max));
            return true;
        }

        // Single value
        if (tk.ExpectFloat(out float v))
        {
            value = new VfxRange(v, v);
            return true;
        }

        value = default;
        return false;
    }

    private static VfxColorCurve ParseColorCurve(string str, VfxColorCurve defaultValue)
    {
        if (string.IsNullOrEmpty(str))
            return defaultValue;

        var tk = new Tokenizer(str);

        var curve = new VfxColorCurve { Type = VfxCurveType.Linear };

        // Parse start color
        if (!ParseColorValue(ref tk, out curve.Start))
            return defaultValue;

        // Check for =>
        if (!tk.ExpectDelimiter('='))
        {
            curve.End = curve.Start;
            return curve;
        }

        if (!tk.ExpectDelimiter('>'))
            return defaultValue;

        // Parse end color
        if (!ParseColorValue(ref tk, out curve.End))
            return defaultValue;

        // Check for :curvetype
        if (tk.ExpectDelimiter(':'))
        {
            if (tk.ExpectIdentifier(out string curveType))
                curve.Type = ParseCurveType(curveType);
        }

        return curve;
    }

    private static bool ParseColorValue(ref Tokenizer tk, out VfxColorRange value)
    {
        // Try range: [color1, color2]
        if (tk.ExpectDelimiter('['))
        {
            if (!tk.ExpectColor(out Color min))
            {
                value = default;
                return false;
            }
            if (!tk.ExpectDelimiter(','))
            {
                value = default;
                return false;
            }
            if (!tk.ExpectColor(out Color max))
            {
                value = default;
                return false;
            }
            tk.ExpectDelimiter(']');
            value = new VfxColorRange(min, max);
            return true;
        }

        // Single color
        if (tk.ExpectColor(out Color c))
        {
            value = new VfxColorRange(c, c);
            return true;
        }

        value = default;
        return false;
    }

    private static VfxCurveType ParseCurveType(string name)
    {
        return name.ToLowerInvariant() switch
        {
            "linear" => VfxCurveType.Linear,
            "easein" => VfxCurveType.EaseIn,
            "easeout" => VfxCurveType.EaseOut,
            "easeinout" => VfxCurveType.EaseInOut,
            "quadratic" => VfxCurveType.Quadratic,
            "cubic" => VfxCurveType.Cubic,
            "sine" => VfxCurveType.Sine,
            _ => VfxCurveType.Linear
        };
    }

    // --- Text file serialization ---

    public override void Save(StreamWriter sw)
    {
        sw.WriteLine("[vfx]");
        sw.WriteLine($"duration = {FormatRange(_duration)}");
        sw.WriteLine($"loop = {_loop.ToString().ToLowerInvariant()}");

        sw.WriteLine();
        sw.WriteLine("[emitters]");
        for (var i = 0; i < _emitterNames.Length; i++)
            sw.WriteLine(_emitterNames[i]);

        for (var i = 0; i < _emitterDefs.Length; i++)
        {
            ref var e = ref _emitterDefs[i];
            var name = _emitterNames[i];

            sw.WriteLine();
            sw.WriteLine($"[{name}]");
            sw.WriteLine($"rate = {FormatIntRange(e.Rate)}");
            sw.WriteLine($"burst = {FormatIntRange(e.Burst)}");
            sw.WriteLine($"duration = {FormatRange(e.Duration)}");
            sw.WriteLine($"angle = {FormatRange(e.Angle)}");
            sw.WriteLine($"spawn = {FormatVec2Range(e.Spawn)}");
            sw.WriteLine($"direction = {FormatVec2Range(e.Direction)}");

            sw.WriteLine();
            sw.WriteLine($"[{name}.particle]");
            sw.WriteLine($"duration = {FormatRange(e.Particle.Duration)}");
            sw.WriteLine($"size = {FormatFloatCurve(e.Particle.Size)}");
            sw.WriteLine($"speed = {FormatFloatCurve(e.Particle.Speed)}");
            sw.WriteLine($"color = {FormatColorCurve(e.Particle.Color)}");
            sw.WriteLine($"opacity = {FormatFloatCurve(e.Particle.Opacity)}");
            sw.WriteLine($"gravity = {FormatVec2Range(e.Particle.Gravity)}");
            sw.WriteLine($"drag = {FormatRange(e.Particle.Drag)}");
            sw.WriteLine($"rotation = {FormatFloatCurve(e.Particle.Rotation)}");
        }
    }

    private static string FormatFloat(float v) =>
        v == (int)v ? ((int)v).ToString() : v.ToString("G");

    private static string FormatRange(VfxRange r) =>
        r.Min == r.Max ? FormatFloat(r.Min) : $"[{FormatFloat(r.Min)}, {FormatFloat(r.Max)}]";

    private static string FormatIntRange(VfxIntRange r) =>
        r.Min == r.Max ? r.Min.ToString() : $"[{r.Min}, {r.Max}]";

    private static string FormatVec2Range(VfxVec2Range r)
    {
        if (r.Min == r.Max)
            return $"({FormatFloat(r.Min.X)}, {FormatFloat(r.Min.Y)})";
        return $"[({FormatFloat(r.Min.X)}, {FormatFloat(r.Min.Y)}), ({FormatFloat(r.Max.X)}, {FormatFloat(r.Max.Y)})]";
    }

    private static string FormatFloatCurve(VfxFloatCurve c)
    {
        var start = FormatRange(c.Start);
        var end = FormatRange(c.End);
        var type = FormatCurveType(c.Type);

        if (c.Start.Min == c.End.Min && c.Start.Max == c.End.Max)
            return start;

        return $"{start}=>{end}:{type}";
    }

    private static string FormatColorCurve(VfxColorCurve c)
    {
        var start = FormatColorRange(c.Start);
        var end = FormatColorRange(c.End);
        var type = FormatCurveType(c.Type);

        if (c.Start.Min == c.End.Min && c.Start.Max == c.End.Max)
            return start;

        return $"{start}=>{end}:{type}";
    }

    private static string FormatColorRange(VfxColorRange r)
    {
        if (r.Min == r.Max)
            return FormatColor(r.Min);
        return $"[{FormatColor(r.Min)}, {FormatColor(r.Max)}]";
    }

    private static string FormatColor(Color c)
    {
        var r = (byte)(Math.Clamp(c.R, 0, 1) * 255);
        var g = (byte)(Math.Clamp(c.G, 0, 1) * 255);
        var b = (byte)(Math.Clamp(c.B, 0, 1) * 255);
        var a = (byte)(Math.Clamp(c.A, 0, 1) * 255);
        return a == 255 ? $"#{r:X2}{g:X2}{b:X2}" : $"#{r:X2}{g:X2}{b:X2}{a:X2}";
    }

    private static string FormatCurveType(VfxCurveType type) => type switch
    {
        VfxCurveType.Linear => "linear",
        VfxCurveType.EaseIn => "easein",
        VfxCurveType.EaseOut => "easeout",
        VfxCurveType.EaseInOut => "easeinout",
        VfxCurveType.Quadratic => "quadratic",
        VfxCurveType.Cubic => "cubic",
        VfxCurveType.Sine => "sine",
        _ => "linear"
    };
}
