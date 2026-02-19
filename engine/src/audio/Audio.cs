//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using NoZ.Platform;

namespace NoZ;

public static class Audio
{
    public static IAudioDriver Driver { get; private set; } = null!;

    private static Dictionary<StringId, int> _playFrame = new(128);

    internal static void Init(IAudioDriver driver)
    {
        Driver = driver;
        Driver.Init();
    }

    internal static void Shutdown()
    {
        StopMusic();
        Driver.Shutdown();
    }

    public static SoundHandle Play(Sound sound, float volume = 1f, float pitch = 1f, bool loop = false, bool debounce=true)
    {
        if (sound == null || volume <= 0f || sound.PlatformHandle == 0)
            return default;

        if (debounce)
        {
           if (_playFrame.TryGetValue(sound.Id, out var frame) && frame == Time.FrameCount)
                return default;

            _playFrame[sound.Id] = Time.FrameCount;
        }

        return new(Driver.Play(sound.PlatformHandle, volume, pitch, loop));
    }

    public static SoundHandle PlayRandomPitch(Sound sound, float volume, float minPitch, float maxPitch, bool loop = false, bool debounce = true)
    {
        var pitch = MathEx.RandomRange(minPitch, maxPitch);
        return Play(sound, volume, pitch, loop, debounce);
    }

    public static SoundHandle PlayRandom(Sound[] sounds, float volume = 1f, float pitch = 1f, bool loop = false)
    {
        if (sounds.Length == 0) return default;
        var sound = sounds[Random.Shared.Next(sounds.Length)];
        return Play(sound, volume, pitch, loop);
    }

    public static void Stop(SoundHandle handle) => Driver.Stop(handle.Value);

    public static bool IsPlaying(SoundHandle handle) => Driver.IsPlaying(handle.Value);

    // Per-instance control
    public static void SetVolume(SoundHandle handle, float volume) => Driver.SetVolume(handle.Value, volume);
    public static void SetPitch(SoundHandle handle, float pitch) => Driver.SetPitch(handle.Value, pitch);
    public static float GetVolume(SoundHandle handle) => Driver.GetVolume(handle.Value);
    public static float GetPitch(SoundHandle handle) => Driver.GetPitch(handle.Value);

    // Music
    public static void PlayMusic(Sound sound)
    {
        if (IsMusicPlaying())
            StopMusic();
        Driver.PlayMusic(sound.PlatformHandle);
    }

    public static void StopMusic() => Driver.StopMusic();

    public static bool IsMusicPlaying() => Driver.IsMusicPlaying();

    // Volume hierarchy
    public static float MasterVolume
    {
        get => Driver.MasterVolume;
        set => Driver.MasterVolume = value;
    }

    public static float SoundVolume
    {
        get => Driver.SoundVolume;
        set => Driver.SoundVolume = value;
    }

    public static float MusicVolume
    {
        get => Driver.MusicVolume;
        set => Driver.MusicVolume = value;
    }
}
