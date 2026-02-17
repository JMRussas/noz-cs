//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using System.Text;

namespace NoZ.Editor;

public class SoundDocument : Document
{
    private Sound? _sound;
    private SoundHandle _handle;

    public static void RegisterDef()
    {
        DocumentManager.RegisterDef(new DocumentDef 
        {
            Type = AssetType.Sound,
            Extension = ".wav",
            Factory = () => new SoundDocument()
        });
    }

    public override void Import(string outputPath, PropertySet meta)
    {
        using var fileStream = File.OpenRead(Path);
        using var reader = new BinaryReader(fileStream);

        // Read and validate WAV header
        var riff = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (riff != "RIFF")
            throw new InvalidDataException($"Invalid WAV file: expected RIFF, got {riff}");

        reader.ReadUInt32(); // chunk size

        var wave = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (wave != "WAVE")
            throw new InvalidDataException($"Invalid WAV file: expected WAVE, got {wave}");

        // Read format chunk
        var fmt = Encoding.ASCII.GetString(reader.ReadBytes(4));
        if (fmt != "fmt ")
            throw new InvalidDataException($"Invalid WAV file: expected fmt , got {fmt}");

        var fmtSize = reader.ReadUInt32();
        var audioFormat = reader.ReadUInt16();
        var numChannels = reader.ReadUInt16();
        var sampleRate = reader.ReadUInt32();
        reader.ReadUInt32(); // byte rate
        reader.ReadUInt16(); // block align
        var bitsPerSample = reader.ReadUInt16();

        if (audioFormat != 1)
            throw new InvalidDataException($"Only PCM WAV files are supported (format={audioFormat})");

        if (numChannels != 1 && numChannels != 2)
            throw new InvalidDataException($"Only mono or stereo WAV files are supported (channels={numChannels})");

        if (bitsPerSample != 8 && bitsPerSample != 16)
            throw new InvalidDataException($"Only 8-bit or 16-bit WAV files are supported (bits={bitsPerSample})");

        // Skip extra format data if present
        if (fmtSize > 16)
            reader.ReadBytes((int)(fmtSize - 16));

        // Search for data chunk (may have other chunks like LIST, bext, etc.)
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

            // Skip this chunk
            reader.ReadBytes((int)chunkSize);
        }

        if (dataSize == 0)
            throw new InvalidDataException("No data chunk found in WAV file");

        var pcmData = reader.ReadBytes((int)dataSize);

        using var writer = new BinaryWriter(File.Create(outputPath));
        writer.WriteAssetHeader(AssetType.Sound, Sound.Version);
        writer.Write((int)sampleRate);
        writer.Write((int)numChannels);
        writer.Write((int)bitsPerSample);
        writer.Write((int)dataSize);
        writer.Write(pcmData);
    }

    public override void Draw()
    {
        using (Graphics.PushState())
        {
            Graphics.SetLayer(EditorLayer.Document);
            Graphics.SetColor(Color.White);
            Graphics.Draw(EditorAssets.Sprites.AssetIconSound);
        }
    }

    public override void PostLoad()
    {
        _sound = Asset.Load(AssetType.Sound, Name, useRegistry: false, libraryPath: EditorApplication.OutputPath) as Sound;
        base.PostLoad();
    }

    public override void Reload()
    {
        _sound?.Dispose();
        _sound = Asset.Load(AssetType.Sound, Name, useRegistry: false, libraryPath: EditorApplication.OutputPath) as Sound;
    }

    public override bool CanPlay => _sound != null;
    public override bool IsPlaying => Audio.IsPlaying(_handle); 

    public override void Play()
    {
        if (_sound == null)
            return;

        _handle = Audio.Play(_sound);
    }

    public override void Stop()
    {
        Audio.Stop(_handle);
    }
}
