//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

namespace noz.Platform;

public interface IAudio
{
    void Init();
    void Shutdown();

    // Sound resource management
    nint CreateSound(ReadOnlySpan<byte> pcmData, int sampleRate, int channels, int bitsPerSample);
    void DestroySound(nint handle);

    // Playback
    ulong Play(nint sound, float volume, float pitch, bool loop);
    void Stop(ulong handle);
    bool IsPlaying(ulong handle);

    // Per-instance control
    void SetVolume(ulong handle, float volume);
    void SetPitch(ulong handle, float pitch);
    float GetVolume(ulong handle);
    float GetPitch(ulong handle);

    // Music (dedicated channel)
    void PlayMusic(nint sound);
    void StopMusic();
    bool IsMusicPlaying();

    // Volume hierarchy
    float MasterVolume { get; set; }
    float SoundVolume { get; set; }
    float MusicVolume { get; set; }
}
