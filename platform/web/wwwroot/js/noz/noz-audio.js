//
//  NoZ - Copyright(c) 2026 NoZ Games, LLC
//

let audioContext = null;
let masterGain = null;
let soundGain = null;
let musicGain = null;

const sounds = new Map();
const playingInstances = new Map();
let musicSource = null;
let currentMusicId = null;

export function init() {
    // Create audio context on first user interaction
    const resumeContext = () => {
        if (!audioContext) {
            audioContext = new AudioContext();

            masterGain = audioContext.createGain();
            masterGain.connect(audioContext.destination);

            soundGain = audioContext.createGain();
            soundGain.connect(masterGain);

            musicGain = audioContext.createGain();
            musicGain.connect(masterGain);
        }

        if (audioContext.state === 'suspended') {
            audioContext.resume();
        }
    };

    document.addEventListener('click', resumeContext, { once: false });
    document.addEventListener('keydown', resumeContext, { once: false });
    document.addEventListener('touchstart', resumeContext, { once: false });

    // Try to create immediately (may fail without user gesture)
    try {
        resumeContext();
    } catch (e) {
        console.log('Audio context will be created on user interaction');
    }
}

export function shutdown() {
    if (audioContext) {
        audioContext.close();
        audioContext = null;
    }
    sounds.clear();
    playingInstances.clear();
}

export function createSound(soundId, pcmData, sampleRate, channels, bitsPerSample) {
    if (!audioContext) return;

    // Convert PCM data to AudioBuffer
    const bytesPerSample = bitsPerSample / 8;
    const numSamples = pcmData.length / bytesPerSample;
    const audioBuffer = audioContext.createBuffer(1, numSamples, sampleRate);
    const channelData = audioBuffer.getChannelData(0);

    if (bitsPerSample === 16) {
        // Convert 16-bit signed PCM to float
        for (let i = 0; i < numSamples; i++) {
            const offset = i * 2;
            const sample = (pcmData[offset] | (pcmData[offset + 1] << 8));
            // Sign extend
            const signed = sample > 32767 ? sample - 65536 : sample;
            channelData[i] = signed / 32768.0;
        }
    } else if (bitsPerSample === 8) {
        // Convert 8-bit unsigned PCM to float
        for (let i = 0; i < numSamples; i++) {
            channelData[i] = (pcmData[i] - 128) / 128.0;
        }
    }

    sounds.set(soundId, audioBuffer);
}

export function destroySound(soundId) {
    sounds.delete(soundId);
}

export function play(soundId, handleId, volume, pitch, loop) {
    if (!audioContext || !sounds.has(soundId)) return;

    const buffer = sounds.get(soundId);
    const source = audioContext.createBufferSource();
    source.buffer = buffer;
    source.loop = loop;
    source.playbackRate.value = pitch;

    const gainNode = audioContext.createGain();
    gainNode.gain.value = volume;

    source.connect(gainNode);
    gainNode.connect(soundGain);

    source.start();

    playingInstances.set(handleId, { source, gainNode });

    source.onended = () => {
        playingInstances.delete(handleId);
    };
}

export function stop(handleId) {
    const instance = playingInstances.get(handleId);
    if (instance) {
        try {
            instance.source.stop();
        } catch (e) {
            // Already stopped
        }
        playingInstances.delete(handleId);
    }
}

export function setVolume(handleId, volume) {
    const instance = playingInstances.get(handleId);
    if (instance) {
        instance.gainNode.gain.value = volume;
    }
}

export function setPitch(handleId, pitch) {
    const instance = playingInstances.get(handleId);
    if (instance) {
        instance.source.playbackRate.value = pitch;
    }
}

export function playMusic(soundId) {
    stopMusic();

    if (!audioContext || !sounds.has(soundId)) return;

    const buffer = sounds.get(soundId);
    musicSource = audioContext.createBufferSource();
    musicSource.buffer = buffer;
    musicSource.loop = true;
    musicSource.connect(musicGain);
    musicSource.start();
    currentMusicId = soundId;
}

export function stopMusic() {
    if (musicSource) {
        try {
            musicSource.stop();
        } catch (e) {
            // Already stopped
        }
        musicSource = null;
        currentMusicId = null;
    }
}

export function setMasterVolume(volume) {
    if (masterGain) {
        masterGain.gain.value = volume;
    }
}

export function setSoundVolume(volume) {
    if (soundGain) {
        soundGain.gain.value = volume;
    }
}

export function setMusicVolume(volume) {
    if (musicGain) {
        musicGain.gain.value = volume;
    }
}
