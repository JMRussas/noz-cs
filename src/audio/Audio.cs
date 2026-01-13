//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using noz.Platform;

namespace noz;

public static class Audio
{
    public static IAudio Backend { get; private set; } = null!;

    internal static void Init(IAudio backend)
    {
        Backend = backend;
        Backend.Init();
    }

    internal static void Shutdown()
    {
        StopMusic();
        Backend.Shutdown();
    }

    // Playback
    public static SoundHandle Play(Sound sound, float volume = 1f, float pitch = 1f, bool loop = false)
        => new(Backend.Play(sound.PlatformHandle, volume, pitch, loop));

    public static SoundHandle PlayRandom(Sound[] sounds, float volume = 1f, float pitch = 1f, bool loop = false)
    {
        if (sounds.Length == 0) return default;
        var sound = sounds[Random.Shared.Next(sounds.Length)];
        return Play(sound, volume, pitch, loop);
    }

    public static void Stop(SoundHandle handle) => Backend.Stop(handle.Value);

    public static bool IsPlaying(SoundHandle handle) => Backend.IsPlaying(handle.Value);

    // Per-instance control
    public static void SetVolume(SoundHandle handle, float volume) => Backend.SetVolume(handle.Value, volume);
    public static void SetPitch(SoundHandle handle, float pitch) => Backend.SetPitch(handle.Value, pitch);
    public static float GetVolume(SoundHandle handle) => Backend.GetVolume(handle.Value);
    public static float GetPitch(SoundHandle handle) => Backend.GetPitch(handle.Value);

    // Music
    public static void PlayMusic(Sound sound)
    {
        if (IsMusicPlaying())
            StopMusic();
        Backend.PlayMusic(sound.PlatformHandle);
    }

    public static void StopMusic() => Backend.StopMusic();

    public static bool IsMusicPlaying() => Backend.IsMusicPlaying();

    // Volume hierarchy
    public static float MasterVolume
    {
        get => Backend.MasterVolume;
        set => Backend.MasterVolume = value;
    }

    public static float SoundVolume
    {
        get => Backend.SoundVolume;
        set => Backend.SoundVolume = value;
    }

    public static float MusicVolume
    {
        get => Backend.MusicVolume;
        set => Backend.MusicVolume = value;
    }
}
