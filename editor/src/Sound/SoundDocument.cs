//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;

namespace NoZ.Editor;

public class SoundLayer : IWaveformSource
{
    public DocumentRef<SoundDocument> SoundRef;
    public float TrimStart { get; set; }
    public float TrimEnd { get; set; }
    public float FadeIn { get; set; }
    public float FadeOut { get; set; }
    public float Offset { get; set; }
    public float Volume = 1f;

    // IWaveformSource
    public float[]? GetMonoSamples() => SoundDocument.GetMonoSamples(SoundRef.Value);
    public int SampleRate => SoundRef.Value?.SampleRate ?? 44100;
    public float Duration => SoundRef.Value?.Duration ?? 0f;
    public bool OffsetEnabled => true;
    public float NormalizeScale => 1f;
}

public class SoundDocument : Document, IWaveformSource
{
    public const string Extension = ".sound";
    private SoundHandle _playHandle;

    // Preview sound built from in-memory samples + current modifiers
    private nint _previewHandle;
    private int _previewVersion = -1;
    private volatile bool _previewBuilding;
    private byte[]? _pendingPreviewPcm;
    private readonly object _previewLock = new();

    // Raw PCM samples as normalized floats (-1..1), loaded from WAV
    internal float[]? Samples { get; private set; }
    internal int SampleRate { get; private set; }
    internal int ChannelCount { get; private set; }
    internal int BitsPerSample { get; private set; }

    // Computed
    public float Duration => IsComposite ? ComputeCompositeDuration() :
        Samples != null && SampleRate > 0 && ChannelCount > 0
        ? (float)Samples.Length / ChannelCount / SampleRate
        : 0f;

    // Non-destructive modifier properties (stored in .meta for .wav, in file for .sound)
    public float VolumeMin { get; set; } = 1f;
    public float VolumeMax { get; set; } = 1f;
    public float PitchMin { get; set; } = 1f;
    public float PitchMax { get; set; } = 1f;
    public float FadeIn { get; set; }
    public float FadeOut { get; set; }
    public float TrimStart { get; set; }
    public float TrimEnd { get; set; }
    public float NormalizeTarget { get; set; }

    // IWaveformSource implementation
    float IWaveformSource.Offset { get => 0f; set { } }
    bool IWaveformSource.OffsetEnabled => false;
    int IWaveformSource.SampleRate => SampleRate;

    public float NormalizeScale
    {
        get
        {
            if (NormalizeTarget <= 0f) return 1f;
            var peak = 0f;
            if (Samples != null)
                for (var i = 0; i < Samples.Length; i++)
                    peak = MathF.Max(peak, MathF.Abs(Samples[i]));
            return peak > 0f ? NormalizeTarget / peak : 1f;
        }
    }

    public float[]? GetMonoSamples() => GetMonoSamples(this);

    // Composite sound (.sound file)
    public bool IsComposite => Path.EndsWith(".sound", StringComparison.OrdinalIgnoreCase);
    public List<SoundLayer> Layers { get; } = [];

    public override bool CanSave => IsComposite;

    public static void RegisterDef()
    {
        DocumentDef<SoundDocument>.Register(new DocumentDef
        {
            Type = AssetType.Sound,
            Name = "Sound",
            Extensions = [Extension, ".wav"],
            Factory = _ => new SoundDocument(),
            EditorFactory = doc =>
            {
                var soundDoc = (SoundDocument)doc;
                return soundDoc.IsComposite
                    ? new CompositeSoundEditor(soundDoc)
                    : new SoundEditor(soundDoc);
            },
            CanEdit = _ => true,
            Icon = () => EditorAssets.Sprites.AssetIconSound,
        });
    }

    public static Document? CreateNew(string? name = null, System.Numerics.Vector2? position = null)
    {
        return Project.New(AssetType.Sound, Extension, name, writer =>
        {
            writer.WriteLine("volume 1 1");
            writer.WriteLine("pitch 1 1");
        }, position);
    }

    public override void Load()
    {
        if (IsComposite)
            LoadSoundFile();
        else
            LoadWavSamples();
    }

    public override void PostLoad()
    {
        if (!IsComposite) return;

        foreach (var layer in Layers)
            layer.SoundRef.Resolve();
    }

    public override void GetReferences(List<Document> references)
    {
        if (!IsComposite) return;
        foreach (var layer in Layers)
            if (layer.SoundRef.IsResolved)
                references.Add(layer.SoundRef.Value!);
    }

    public override void GetDependencies(List<(AssetType Type, string Name)> dependencies)
    {
        if (!IsComposite) return;
        foreach (var layer in Layers)
            if (layer.SoundRef.HasValue)
                dependencies.Add((AssetType.Sound, layer.SoundRef.Name!));
    }

    public override void OnRenamed(Document doc, string oldName, string newName)
    {
        if (doc is not SoundDocument || !IsComposite) return;
        var changed = false;
        foreach (var layer in Layers)
        {
            if (layer.SoundRef.TryRename(oldName, newName))
                changed = true;
        }
        if (changed)
            IncrementVersion();
    }

    public override void Reload() { }

    public override void Dispose()
    {
        DestroyPreview();
        base.Dispose();
    }

    public override void Clone(Document source)
    {
        var src = (SoundDocument)source;

        VolumeMin = src.VolumeMin;
        VolumeMax = src.VolumeMax;
        PitchMin = src.PitchMin;
        PitchMax = src.PitchMax;
        FadeIn = src.FadeIn;
        FadeOut = src.FadeOut;
        TrimStart = src.TrimStart;
        TrimEnd = src.TrimEnd;
        NormalizeTarget = src.NormalizeTarget;

        Layers.Clear();
        foreach (var srcLayer in src.Layers)
        {
            Layers.Add(new SoundLayer
            {
                SoundRef = srcLayer.SoundRef,
                TrimStart = srcLayer.TrimStart,
                TrimEnd = srcLayer.TrimEnd,
                FadeIn = srcLayer.FadeIn,
                FadeOut = srcLayer.FadeOut,
                Offset = srcLayer.Offset,
                Volume = srcLayer.Volume
            });
        }
    }

    public override void OnUndoRedo()
    {
        if (IsComposite)
            RebuildCompositePreviewAsync();
        else
            RebuildPreviewAsync();
    }

    public override void LoadMetadata(PropertySet meta)
    {
        if (IsComposite) return;

        VolumeMin = meta.GetFloat("sound", "volumeMin", 1f);
        VolumeMax = meta.GetFloat("sound", "volumeMax", 1f);
        PitchMin = meta.GetFloat("sound", "pitchMin", 1f);
        PitchMax = meta.GetFloat("sound", "pitchMax", 1f);
        FadeIn = meta.GetFloat("sound", "fadeIn", 0f);
        FadeOut = meta.GetFloat("sound", "fadeOut", 0f);
        TrimStart = meta.GetFloat("sound", "trimStart", 0f);
        TrimEnd = meta.GetFloat("sound", "trimEnd", 0f);
        NormalizeTarget = meta.GetFloat("sound", "normalize", 0f);
    }

    public override void SaveMetadata(PropertySet meta)
    {
        if (IsComposite) return;

        if (VolumeMin != 1f) meta.SetFloat("sound", "volumeMin", VolumeMin);
        if (VolumeMax != 1f) meta.SetFloat("sound", "volumeMax", VolumeMax);
        if (PitchMin != 1f) meta.SetFloat("sound", "pitchMin", PitchMin);
        if (PitchMax != 1f) meta.SetFloat("sound", "pitchMax", PitchMax);
        if (FadeIn > 0f) meta.SetFloat("sound", "fadeIn", FadeIn);
        if (FadeOut > 0f) meta.SetFloat("sound", "fadeOut", FadeOut);
        if (TrimStart > 0f) meta.SetFloat("sound", "trimStart", TrimStart);
        if (TrimEnd > 0f) meta.SetFloat("sound", "trimEnd", TrimEnd);
        if (NormalizeTarget > 0f) meta.SetFloat("sound", "normalize", NormalizeTarget);
    }

    public override void Save(StreamWriter sw)
    {
        if (!IsComposite) return;
        SaveSoundFile(sw);
    }

    public void ApplyChanges()
    {
        IncrementVersion();

        if (IsComposite)
            RebuildCompositePreviewAsync();
        else
            RebuildPreviewAsync();
    }

    private float[]? ProcessSamples()
    {
        if (Samples == null) return null;

        return ProcessSamplesWithParams(
            Samples, SampleRate, ChannelCount,
            TrimStart, TrimEnd, NormalizeTarget, FadeIn, FadeOut);
    }

    public override void Export(string outputPath, PropertySet meta)
    {
        if (IsComposite)
            ExportComposite(outputPath);
        else
            ExportWav(outputPath);
    }

    private void ExportWav(string outputPath)
    {
        var processed = ProcessSamples();
        if (processed == null)
            return;

        var pcmData = ConvertToPcm(processed, BitsPerSample);

        using var writer = new BinaryWriter(File.OpenWrite(outputPath));
        writer.WriteAssetHeader(AssetType.Sound, Sound.Version);
        writer.Write(SampleRate);
        writer.Write(ChannelCount);
        writer.Write(BitsPerSample);
        writer.Write(pcmData.Length);
        writer.Write(VolumeMin);
        writer.Write(VolumeMax);
        writer.Write(PitchMin);
        writer.Write(PitchMax);
        writer.Write(pcmData);
    }

    private void ExportComposite(string outputPath)
    {
        var mixed = MixLayers();
        if (mixed == null || mixed.Length == 0)
            return;

        var sampleRate = GetCompositeSampleRate();
        var pcmData = ConvertToPcm(mixed, 16);

        using var writer = new BinaryWriter(File.OpenWrite(outputPath));
        writer.WriteAssetHeader(AssetType.Sound, Sound.Version);
        writer.Write(sampleRate);
        writer.Write(1); // mono
        writer.Write(16); // 16-bit
        writer.Write(pcmData.Length);
        writer.Write(VolumeMin);
        writer.Write(VolumeMax);
        writer.Write(PitchMin);
        writer.Write(PitchMax);
        writer.Write(pcmData);
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetShader(EditorAssets.Shaders.Sprite);
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconSound);
        }
    }

    public override bool CanPlay => IsComposite ? Layers.Any(l => l.SoundRef.IsResolved) : Samples != null;
    public override bool IsPlaying => Audio.IsPlaying(_playHandle);

    public override void Play()
    {
        if (IsComposite)
        {
            InstallPendingPreview();
            if (_previewVersion != Version && !_previewBuilding)
                BuildCompositePreviewSync();
        }
        else
        {
            if (Samples == null)
                return;

            InstallPendingPreview();
            if (_previewVersion != Version && !_previewBuilding)
                BuildPreviewSync();
        }

        if (_previewHandle == 0)
            return;

        var volume = MathEx.RandomRange(VolumeMin, VolumeMax);
        var pitch = MathEx.RandomRange(PitchMin, PitchMax);
        _playHandle = new SoundHandle(Audio.Driver.Play(_previewHandle, volume, pitch, false));
    }

    public float PlaybackPosition => Audio.GetPlaybackPosition(_playHandle);

    public override void Stop()
    {
        Audio.Stop(_playHandle);
    }

    // --- .sound file parsing ---

    private void LoadSoundFile()
    {
        var text = File.ReadAllText(Path);
        var tokenizer = new Tokenizer(text);

        Layers.Clear();
        VolumeMin = 1f;
        VolumeMax = 1f;
        PitchMin = 1f;
        PitchMax = 1f;

        while (!tokenizer.IsEOF)
        {
            if (tokenizer.ExpectIdentifier(out var keyword))
            {
                switch (keyword)
                {
                    case "volume":
                        VolumeMin = tokenizer.ExpectFloat(1f);
                        VolumeMax = tokenizer.ExpectFloat(VolumeMin);
                        break;

                    case "pitch":
                        PitchMin = tokenizer.ExpectFloat(1f);
                        PitchMax = tokenizer.ExpectFloat(PitchMin);
                        break;

                    case "layer":
                        ParseLayer(ref tokenizer);
                        break;

                    default:
                        tokenizer.Skip();
                        break;
                }
            }
            else
            {
                tokenizer.Skip();
            }
        }
    }

    private void ParseLayer(ref Tokenizer tokenizer)
    {
        var name = tokenizer.ExpectQuotedString() ?? "";

        var layer = new SoundLayer
        {
            SoundRef = new DocumentRef<SoundDocument> { Name = name }
        };

        while (!tokenizer.IsEOF)
        {
            if (tokenizer.ExpectIdentifier(out var prop))
            {
                switch (prop)
                {
                    case "trim":
                        layer.TrimStart = tokenizer.ExpectFloat();
                        layer.TrimEnd = tokenizer.ExpectFloat();
                        break;
                    case "fade":
                        layer.FadeIn = tokenizer.ExpectFloat();
                        layer.FadeOut = tokenizer.ExpectFloat();
                        break;
                    case "offset":
                        layer.Offset = tokenizer.ExpectFloat();
                        break;
                    case "volume":
                        layer.Volume = tokenizer.ExpectFloat(1f);
                        break;
                    case "layer":
                        Layers.Add(layer);
                        ParseLayer(ref tokenizer);
                        return;
                    default:
                        Layers.Add(layer);
                        return;
                }
            }
            else
            {
                break;
            }
        }

        Layers.Add(layer);
    }

    private void SaveSoundFile(StreamWriter sw)
    {
        sw.WriteLine($"volume {FormatFloat(VolumeMin)} {FormatFloat(VolumeMax)}");
        sw.WriteLine($"pitch {FormatFloat(PitchMin)} {FormatFloat(PitchMax)}");

        foreach (var layer in Layers)
        {
            sw.WriteLine();
            sw.WriteLine($"layer \"{layer.SoundRef.Name}\"");
            sw.WriteLine($"  trim {FormatFloat(layer.TrimStart)} {FormatFloat(layer.TrimEnd)}");
            sw.WriteLine($"  fade {FormatFloat(layer.FadeIn)} {FormatFloat(layer.FadeOut)}");
            sw.WriteLine($"  offset {FormatFloat(layer.Offset)}");
            sw.WriteLine($"  volume {FormatFloat(layer.Volume)}");
        }
    }

    private static string FormatFloat(float v) =>
        v % 1 == 0 ? v.ToString("F0") : v.ToString("G6");

    // --- Composite mixing ---

    private int GetCompositeSampleRate()
    {
        foreach (var layer in Layers)
        {
            if (layer.SoundRef.Value is { Samples: not null })
                return layer.SoundRef.Value.SampleRate;
        }
        return 44100;
    }

    public float ComputeCompositeDuration()
    {
        var maxEnd = 0f;
        foreach (var layer in Layers)
        {
            var src = layer.SoundRef.Value;
            if (src?.Samples == null || src.SampleRate <= 0 || src.ChannelCount <= 0) continue;

            var srcDuration = (float)src.Samples.Length / src.ChannelCount / src.SampleRate;
            var trimStart = layer.TrimStart;
            var trimEnd = layer.TrimEnd > 0f ? layer.TrimEnd : 1f;
            var trimmedDuration = (trimEnd - trimStart) * srcDuration;
            var layerEnd = layer.Offset + trimmedDuration;
            if (layerEnd > maxEnd) maxEnd = layerEnd;
        }
        return maxEnd;
    }

    internal float[]? MixLayers()
    {
        var sampleRate = GetCompositeSampleRate();
        var totalDuration = ComputeCompositeDuration();
        if (totalDuration <= 0f) return null;

        var totalSamples = (int)(totalDuration * sampleRate);
        if (totalSamples <= 0) return null;

        var mix = new float[totalSamples];

        foreach (var layer in Layers)
        {
            var src = layer.SoundRef.Value;
            if (src?.Samples == null) continue;

            var srcSamples = GetMonoSamples(src);
            if (srcSamples == null) continue;

            var processed = ProcessSamplesWithParams(
                srcSamples, src.SampleRate, 1,
                layer.TrimStart, layer.TrimEnd, 0f, layer.FadeIn, layer.FadeOut);
            if (processed == null) continue;

            if (src.SampleRate != sampleRate)
                processed = Resample(processed, src.SampleRate, sampleRate);

            var offsetSamples = (int)(layer.Offset * sampleRate);
            var count = Math.Min(processed.Length, totalSamples - offsetSamples);
            for (var i = 0; i < count; i++)
            {
                var idx = offsetSamples + i;
                if (idx >= 0 && idx < totalSamples)
                    mix[idx] += processed[i] * layer.Volume;
            }
        }

        for (var i = 0; i < mix.Length; i++)
            mix[i] = Math.Clamp(mix[i], -1f, 1f);

        return mix;
    }

    internal static float[]? GetMonoSamples(SoundDocument? src)
    {
        if (src?.Samples == null) return null;
        if (src.ChannelCount == 1) return src.Samples;

        var monoCount = src.Samples.Length / src.ChannelCount;
        var mono = new float[monoCount];
        for (var i = 0; i < monoCount; i++)
        {
            var sum = 0f;
            for (var c = 0; c < src.ChannelCount; c++)
                sum += src.Samples[i * src.ChannelCount + c];
            mono[i] = sum / src.ChannelCount;
        }
        return mono;
    }

    private static float[] Resample(float[] samples, int fromRate, int toRate)
    {
        var ratio = (double)fromRate / toRate;
        var newLength = (int)(samples.Length / ratio);
        var result = new float[newLength];
        for (var i = 0; i < newLength; i++)
        {
            var srcPos = i * ratio;
            var idx = (int)srcPos;
            var frac = (float)(srcPos - idx);
            if (idx + 1 < samples.Length)
                result[i] = samples[idx] * (1f - frac) + samples[idx + 1] * frac;
            else if (idx < samples.Length)
                result[i] = samples[idx];
        }
        return result;
    }

    // --- Composite preview ---

    private void RebuildCompositePreviewAsync()
    {
        var capturedVersion = Version;

        var layerSnapshots = new List<(float[]? Samples, int SampleRate, int ChannelCount,
            float TrimStart, float TrimEnd, float FadeIn, float FadeOut, float Offset, float Volume)>();

        foreach (var layer in Layers)
        {
            var src = layer.SoundRef.Value;
            layerSnapshots.Add((
                src?.Samples, src?.SampleRate ?? 44100, src?.ChannelCount ?? 1,
                layer.TrimStart, layer.TrimEnd, layer.FadeIn, layer.FadeOut,
                layer.Offset, layer.Volume));
        }

        _previewBuilding = true;

        Task.Run(() =>
        {
            var mixed = MixLayersFromSnapshots(layerSnapshots);
            if (mixed == null)
            {
                _previewBuilding = false;
                return;
            }

            var sampleRate = GetCompositeSampleRate();
            var pcm = ConvertToPcm(mixed, 16);

            lock (_previewLock)
            {
                if (capturedVersion >= _previewVersion)
                    _pendingPreviewPcm = pcm;
            }

            _previewBuilding = false;
        });
    }

    private float[]? MixLayersFromSnapshots(
        List<(float[]? Samples, int SampleRate, int ChannelCount,
            float TrimStart, float TrimEnd, float FadeIn, float FadeOut,
            float Offset, float Volume)> snapshots)
    {
        var sampleRate = 44100;
        foreach (var s in snapshots)
        {
            if (s.Samples != null) { sampleRate = s.SampleRate; break; }
        }

        var maxEnd = 0f;
        foreach (var s in snapshots)
        {
            if (s.Samples == null) continue;
            var srcDuration = (float)s.Samples.Length / s.ChannelCount / s.SampleRate;
            var trimEnd = s.TrimEnd > 0f ? s.TrimEnd : 1f;
            var trimmedDuration = (trimEnd - s.TrimStart) * srcDuration;
            var end = s.Offset + trimmedDuration;
            if (end > maxEnd) maxEnd = end;
        }

        if (maxEnd <= 0f) return null;

        var totalSamples = (int)(maxEnd * sampleRate);
        if (totalSamples <= 0) return null;

        var mix = new float[totalSamples];

        foreach (var s in snapshots)
        {
            if (s.Samples == null) continue;

            float[]? mono;
            if (s.ChannelCount == 1)
                mono = s.Samples;
            else
            {
                var monoCount = s.Samples.Length / s.ChannelCount;
                mono = new float[monoCount];
                for (var i = 0; i < monoCount; i++)
                {
                    var sum = 0f;
                    for (var c = 0; c < s.ChannelCount; c++)
                        sum += s.Samples[i * s.ChannelCount + c];
                    mono[i] = sum / s.ChannelCount;
                }
            }

            var processed = ProcessSamplesWithParams(
                mono, s.SampleRate, 1,
                s.TrimStart, s.TrimEnd, 0f, s.FadeIn, s.FadeOut);
            if (processed == null) continue;

            if (s.SampleRate != sampleRate)
                processed = Resample(processed, s.SampleRate, sampleRate);

            var offsetSamples = (int)(s.Offset * sampleRate);
            var count = Math.Min(processed.Length, totalSamples - offsetSamples);
            for (var i = 0; i < count; i++)
            {
                var idx = offsetSamples + i;
                if (idx >= 0 && idx < totalSamples)
                    mix[idx] += processed[i] * s.Volume;
            }
        }

        for (var i = 0; i < mix.Length; i++)
            mix[i] = Math.Clamp(mix[i], -1f, 1f);

        return mix;
    }

    private void BuildCompositePreviewSync()
    {
        var mixed = MixLayers();
        if (mixed == null) return;

        var sampleRate = GetCompositeSampleRate();
        var pcm = ConvertToPcm(mixed, 16);

        if (_previewHandle != 0)
            Audio.Driver.DestroySound(_previewHandle);

        _previewHandle = Audio.Driver.CreateSound(pcm, sampleRate, 1, 16);
        _previewVersion = Version;
    }

    // --- Preview system (.wav) ---

    private void RebuildPreviewAsync()
    {
        if (Samples == null)
            return;

        var capturedVersion = Version;

        var trimStart = TrimStart;
        var trimEnd = TrimEnd;
        var normalizeTarget = NormalizeTarget;
        var fadeIn = FadeIn;
        var fadeOut = FadeOut;
        var sampleRate = SampleRate;
        var channelCount = ChannelCount;
        var bitsPerSample = BitsPerSample;
        var samples = Samples;

        _previewBuilding = true;

        Task.Run(() =>
        {
            var processed = ProcessSamplesWithParams(
                samples, sampleRate, channelCount,
                trimStart, trimEnd, normalizeTarget, fadeIn, fadeOut);

            if (processed == null)
            {
                _previewBuilding = false;
                return;
            }

            var pcm = ConvertToPcm(processed, bitsPerSample);

            lock (_previewLock)
            {
                if (capturedVersion >= _previewVersion)
                    _pendingPreviewPcm = pcm;
            }

            _previewBuilding = false;
        });
    }

    private void InstallPendingPreview()
    {
        byte[]? pcm;
        lock (_previewLock)
        {
            pcm = _pendingPreviewPcm;
            _pendingPreviewPcm = null;
        }

        if (pcm == null)
            return;

        if (_previewHandle != 0)
            Audio.Driver.DestroySound(_previewHandle);

        if (IsComposite)
        {
            var sampleRate = GetCompositeSampleRate();
            _previewHandle = Audio.Driver.CreateSound(pcm, sampleRate, 1, 16);
        }
        else
        {
            _previewHandle = Audio.Driver.CreateSound(pcm, SampleRate, ChannelCount, BitsPerSample);
        }
        _previewVersion = Version;
    }

    private void BuildPreviewSync()
    {
        var processed = ProcessSamples();
        if (processed == null)
            return;

        var pcm = ConvertToPcm(processed, BitsPerSample);

        if (_previewHandle != 0)
            Audio.Driver.DestroySound(_previewHandle);

        _previewHandle = Audio.Driver.CreateSound(pcm, SampleRate, ChannelCount, BitsPerSample);
        _previewVersion = Version;
    }

    private void DestroyPreview()
    {
        if (_previewHandle != 0)
        {
            Audio.Driver.DestroySound(_previewHandle);
            _previewHandle = 0;
        }
        _previewVersion = -1;
    }

    internal static float[]? ProcessSamplesWithParams(
        float[] samples, int sampleRate, int channelCount,
        float trimStart, float trimEnd, float normalizeTarget,
        float fadeInDuration, float fadeOutDuration)
    {
        var trimStartSample = (int)(trimStart * samples.Length);
        var trimEndSample = trimEnd > 0f
            ? (int)(trimEnd * samples.Length)
            : samples.Length;

        trimStartSample = Math.Clamp(trimStartSample, 0, samples.Length);
        trimEndSample = Math.Clamp(trimEndSample, trimStartSample, samples.Length);

        var trimmedLength = trimEndSample - trimStartSample;
        if (trimmedLength <= 0)
            return null;

        var processed = new float[trimmedLength];
        Array.Copy(samples, trimStartSample, processed, 0, trimmedLength);

        if (normalizeTarget > 0f)
        {
            var peak = 0f;
            for (var i = 0; i < processed.Length; i++)
                peak = MathF.Max(peak, MathF.Abs(processed[i]));

            if (peak > 0f)
            {
                var scale = normalizeTarget / peak;
                for (var i = 0; i < processed.Length; i++)
                    processed[i] *= scale;
            }
        }

        if (fadeInDuration > 0f)
        {
            var fadeSamples = (int)(fadeInDuration * samples.Length);
            fadeSamples = Math.Min(fadeSamples, processed.Length);
            for (var i = 0; i < fadeSamples; i++)
                processed[i] *= (float)i / fadeSamples;
        }

        if (fadeOutDuration > 0f)
        {
            var fadeSamples = (int)(fadeOutDuration * samples.Length);
            fadeSamples = Math.Min(fadeSamples, processed.Length);
            for (var i = 0; i < fadeSamples; i++)
                processed[processed.Length - 1 - i] *= (float)i / fadeSamples;
        }

        return processed;
    }

    private void LoadWavSamples()
    {
        using var fileStream = File.OpenRead(Path);
        using var reader = new BinaryReader(fileStream);

        var riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (riff != "RIFF")
            return;
        reader.ReadUInt32();
        var wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wave != "WAVE")
            return;

        var fmt = "";
        while (fileStream.Position < fileStream.Length)
        {
            fmt = Encoding.ASCII.GetString(reader.ReadBytes(4));
            if (fmt == "fmt ")
                break;

            var junkSize = reader.ReadUInt32();
            fileStream.Seek(junkSize, SeekOrigin.Current);
        }

        if (fmt != "fmt ")
            return;

        var fmtSize = reader.ReadUInt32();
        var audioFormat = reader.ReadUInt16();
        var numChannels = reader.ReadUInt16();
        var sampleRate = reader.ReadUInt32();
        reader.ReadUInt32();
        reader.ReadUInt16();
        var bitsPerSample = reader.ReadUInt16();

        if (audioFormat != 1) return;
        if (fmtSize > 16) reader.ReadBytes((int)(fmtSize - 16));

        uint dataSize = 0;
        while (fileStream.Position < fileStream.Length)
        {
            var chunkId = Encoding.ASCII.GetString(reader.ReadBytes(4));
            var chunkSize = reader.ReadUInt32();
            if (chunkId == "data")
            {
                dataSize = chunkSize;
                break;
            }
            reader.ReadBytes((int)chunkSize);
        }

        if (dataSize == 0) return;

        SampleRate = (int)sampleRate;
        ChannelCount = numChannels;
        BitsPerSample = bitsPerSample;

        var pcmData = reader.ReadBytes((int)dataSize);
        Samples = ConvertToFloat(pcmData, bitsPerSample);

        if (ChannelCount == 2)
        {
            ChannelCount = 1;
            var mono = new float[Samples.Length / 2];
            for (var i = 0; i < mono.Length; i++)
                mono[i] = Samples[i * 2];

            Samples = mono;
        }
    }

    private static float[] ConvertToFloat(byte[] pcm, int bitsPerSample)
    {
        if (bitsPerSample == 16)
        {
            var count = pcm.Length / 2;
            var samples = new float[count];
            for (var i = 0; i < count; i++)
            {
                var sample = (short)(pcm[i * 2] | (pcm[i * 2 + 1] << 8));
                samples[i] = sample / 32768f;
            }
            return samples;
        }
        else
        {
            var samples = new float[pcm.Length];
            for (var i = 0; i < pcm.Length; i++)
                samples[i] = (pcm[i] - 128) / 128f;
            return samples;
        }
    }

    internal static byte[] ConvertToPcm(float[] samples, int bitsPerSample)
    {
        if (bitsPerSample == 16)
        {
            var pcm = new byte[samples.Length * 2];
            for (var i = 0; i < samples.Length; i++)
            {
                var clamped = Math.Clamp(samples[i], -1f, 1f);
                var value = (short)(clamped * 32767f);
                pcm[i * 2] = (byte)(value & 0xFF);
                pcm[i * 2 + 1] = (byte)((value >> 8) & 0xFF);
            }
            return pcm;
        }
        else
        {
            var pcm = new byte[samples.Length];
            for (var i = 0; i < samples.Length; i++)
            {
                var clamped = Math.Clamp(samples[i], -1f, 1f);
                pcm[i] = (byte)(clamped * 128f + 128f);
            }
            return pcm;
        }
    }

    // --- Layer management ---

    public void AddLayer(SoundDocument source)
    {
        Layers.Add(new SoundLayer
        {
            SoundRef = source,
            Volume = 1f
        });
    }

    public void RemoveLayer(int index)
    {
        if (index >= 0 && index < Layers.Count)
            Layers.RemoveAt(index);
    }
}
