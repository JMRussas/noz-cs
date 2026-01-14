//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz;

public class Sound : Asset
{
    internal const byte Version = 1;

    internal nint PlatformHandle { get; private set; }
    public int SampleRate { get; private set; }
    public int Channels { get; private set; }
    public int BitsPerSample { get; private set; }
    public int DataSize { get; private set; }

    private Sound(string name) : base(AssetType.Sound, name)
    {
    }

    private static Asset Load(Stream stream, string name)
    {
        using var reader = new BinaryReader(stream);

        var sound = new Sound(name)
        {
            SampleRate = reader.ReadInt32(),
            Channels = reader.ReadInt32(),
            BitsPerSample = reader.ReadInt32(),
            DataSize = reader.ReadInt32()
        };

        var pcmData = reader.ReadBytes(sound.DataSize);
        sound.PlatformHandle = Audio.Backend.CreateSound(pcmData, sound.SampleRate, sound.Channels, sound.BitsPerSample);

        return sound;
    }

    public override void Dispose()
    {
        if (PlatformHandle != 0)
        {
            Audio.Backend.DestroySound(PlatformHandle);
            PlatformHandle = 0;
        }
        base.Dispose();
    }

    internal static void RegisterDef()
    {
        RegisterDef(new AssetDef(AssetType.Sound, typeof(Sound), Load));
    }
}