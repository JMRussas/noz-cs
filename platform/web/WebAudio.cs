//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

using Microsoft.JSInterop;
using noz.Platform;

namespace noz;

public class WebAudio : IAudio
{
    private readonly IJSRuntime _js;
    private IJSObjectReference? _module;
    private int _nextSoundId = 1;

    private float _masterVolume = 1f;
    private float _soundVolume = 1f;
    private float _musicVolume = 1f;

    public float MasterVolume
    {
        get => _masterVolume;
        set
        {
            _masterVolume = Math.Clamp(value, 0f, 1f);
            _module?.InvokeVoidAsync("setMasterVolume", _masterVolume);
        }
    }

    public float SoundVolume
    {
        get => _soundVolume;
        set
        {
            _soundVolume = Math.Clamp(value, 0f, 1f);
            _module?.InvokeVoidAsync("setSoundVolume", _soundVolume);
        }
    }

    public float MusicVolume
    {
        get => _musicVolume;
        set
        {
            _musicVolume = Math.Clamp(value, 0f, 1f);
            _module?.InvokeVoidAsync("setMusicVolume", _musicVolume);
        }
    }

    public WebAudio(IJSRuntime js)
    {
        _js = js;
    }

    public void Init()
    {
        // JS module initialization happens async in InitAsync
    }

    public async Task InitAsync()
    {
        _module = await _js.InvokeAsync<IJSObjectReference>("import", "./js/noz/noz-audio.js");
        await _module.InvokeVoidAsync("init");
    }

    public void Shutdown()
    {
        _module?.InvokeVoidAsync("shutdown");
    }

    public nint CreateSound(ReadOnlySpan<byte> pcmData, int sampleRate, int channels, int bitsPerSample)
    {
        var soundId = _nextSoundId++;
        var data = pcmData.ToArray();
        _module?.InvokeVoidAsync("createSound", soundId, data, sampleRate, channels, bitsPerSample);
        return soundId;
    }

    public void DestroySound(nint handle)
    {
        _module?.InvokeVoidAsync("destroySound", (int)handle);
    }

    public ulong Play(nint sound, float volume, float pitch, bool loop)
    {
        if (_module == null) return 0;

        var handleId = (ulong)Random.Shared.NextInt64();
        _module.InvokeVoidAsync("play", (int)sound, handleId, volume, pitch, loop);
        return handleId;
    }

    public void Stop(ulong handle)
    {
        _module?.InvokeVoidAsync("stop", handle);
    }

    public bool IsPlaying(ulong handle)
    {
        // Web Audio doesn't easily support sync queries, return false
        // In practice, games track this themselves
        return false;
    }

    public void SetVolume(ulong handle, float volume)
    {
        _module?.InvokeVoidAsync("setVolume", handle, Math.Clamp(volume, 0f, 1f));
    }

    public void SetPitch(ulong handle, float pitch)
    {
        _module?.InvokeVoidAsync("setPitch", handle, Math.Clamp(pitch, 0.5f, 2f));
    }

    public float GetVolume(ulong handle) => 1f;
    public float GetPitch(ulong handle) => 1f;

    public void PlayMusic(nint sound)
    {
        _module?.InvokeVoidAsync("playMusic", (int)sound);
    }

    public void StopMusic()
    {
        _module?.InvokeVoidAsync("stopMusic");
    }

    public bool IsMusicPlaying()
    {
        // Sync query not practical with JS interop
        return false;
    }
}
