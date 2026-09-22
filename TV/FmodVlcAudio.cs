using FMOD;
using FMODUnity;
using LibVLCSharp;
using MelonLoader;
using System;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Boxroom_TV.TV;

/// <summary>Routes VLC's decoded PCM into BOXROOM's native positional FMOD mixer.</summary>
internal sealed class FmodVlcAudio : IDisposable
{
    private const int SampleRate = 48000;
    private const int Channels = 2;
    private readonly object gate = new();
    // VLC delivers PCM in irregular blocks. Keep enough headroom to absorb
    // decoder scheduling jitter; distance/disable transitions explicitly flush
    // this buffer so its capacity cannot become stale return audio.
    private readonly float[] ring = new float[SampleRate * Channels * 4];
    private readonly MediaPlayer player;
    private int read, write, count;
    private Sound sound;
    private Channel channel;
    private ChannelGroup musicGroup;
    private bool disposed, loggedInput, loggedOutput;
    private bool acceptDecodedAudio = true;
    private SOUND_PCMREAD_CALLBACK pcmReadCallback;

    internal FmodVlcAudio(MediaPlayer mediaPlayer)
    {
        player = mediaPlayer ?? throw new ArgumentNullException(nameof(mediaPlayer));
        player.SetAudioFormat("FL32", SampleRate, Channels);
        player.SetAudioCallbacks(OnVlcAudio, null, null, OnVlcFlush, null);
        StartFmod();
    }

    internal void SetVolume(float value)
    {
        if (channel.hasHandle()) channel.setVolume(Mathf.Clamp01(value));
    }

    internal void Suspend()
    {
        SetOutputActive(false);
        if (channel.hasHandle()) channel.setVolume(0f);
    }

    internal void Resume() => SetOutputActive(true);

    internal void Update(Vector3 position, float volume, float maximumDistance)
    {
        if (!channel.hasHandle()) return;
        VECTOR pos = new() { x = position.x, y = position.y, z = position.z };
        VECTOR velocity = default;
        channel.set3DAttributes(ref pos, ref velocity);
        float maxDistance = Mathf.Max(0.51f, maximumDistance);
        channel.set3DMinMaxDistance(0.5f, maxDistance);
        Camera listener = Camera.main;
        float falloff = listener == null ? 0f : Mathf.Clamp01(Mathf.InverseLerp(maxDistance, 0.5f,
            Vector3.Distance(listener.transform.position, position)));
        float effectiveVolume = Mathf.Clamp01(volume) * falloff;

        // FMOD may virtualize an inaudible 3D channel and stop requesting PCM,
        // while VLC continues decoding on its own thread. Never retain that old
        // audio: when the listener returns, begin with the current VLC blocks.
        bool becameAudible = SetOutputActive(effectiveVolume > 0.001f);
        if (becameAudible) ResynchronizeDecoder();
        channel.setVolume(effectiveVolume);
    }

    private void StartFmod()
    {
        try
        {
            RESULT result = RuntimeManager.CoreSystem.getMasterChannelGroup(out musicGroup);
            if (result != RESULT.OK || !musicGroup.hasHandle()) { MelonLogger.Error($"[Boxroom-TV] FMOD master channel unavailable: {result}"); DisposeFmod(); return; }

            pcmReadCallback = FillFmodPcm;
            CREATESOUNDEXINFO info = new();
            info.cbsize = Marshal.SizeOf(typeof(CREATESOUNDEXINFO));
            info.length = SampleRate * Channels * sizeof(float);
            info.numchannels = Channels;
            info.defaultfrequency = SampleRate;
            info.format = SOUND_FORMAT.PCMFLOAT;
            info.decodebuffersize = 1024;
            info.pcmreadcallback = pcmReadCallback;
            MODE mode = MODE.OPENUSER | MODE.CREATESTREAM | MODE.LOOP_NORMAL | MODE._3D | MODE._3D_WORLDRELATIVE | MODE._3D_LINEARROLLOFF;
            result = RuntimeManager.CoreSystem.createSound(IntPtr.Zero, mode, ref info, out sound);
            if (result != RESULT.OK || !sound.hasHandle()) { MelonLogger.Error($"[Boxroom-TV] FMOD VLC stream failed: {result}"); DisposeFmod(); return; }
            result = RuntimeManager.CoreSystem.playSound(sound, musicGroup, false, out channel);
            if (result != RESULT.OK || !channel.hasHandle()) { MelonLogger.Error($"[Boxroom-TV] FMOD VLC playback failed: {result}"); DisposeFmod(); return; }
            MelonLogger.Msg("[Boxroom-TV] VLC audio attached to BOXROOM FMOD.");
        }
        catch (Exception exception) { MelonLogger.Error("[Boxroom-TV] FMOD VLC setup failed: " + exception); DisposeFmod(); }
    }

    private unsafe void OnVlcAudio(IntPtr ignored, IntPtr samples, uint frameCount, long pts)
    {
        if (disposed || samples == IntPtr.Zero) return;
        if (!loggedInput) { loggedInput = true; MelonLogger.Msg($"[Boxroom-TV] VLC supplied its first decoded audio block ({frameCount} frames)."); }
        float* input = (float*)samples;
        int sampleCount = checked((int)frameCount * Channels);
        lock (gate)
        {
            if (!acceptDecodedAudio) return;
            for (int i = 0; i < sampleCount; i++) Enqueue(input[i]);
        }
    }

    private void OnVlcFlush(IntPtr ignored, long pts)
    {
        lock (gate) { read = write = count = 0; }
    }

    private unsafe RESULT FillFmodPcm(IntPtr ignoredSound, IntPtr data, uint byteCount)
    {
        float* output = (float*)data;
        int samples = checked((int)(byteCount / sizeof(float)));
        if (!loggedOutput) { loggedOutput = true; MelonLogger.Msg($"[Boxroom-TV] FMOD requested its first VLC PCM buffer ({samples} samples)."); }
        lock (gate)
        {
            int i = 0;
            for (; i < samples && count > 0; i++)
            {
                output[i] = ring[read];
                read = (read + 1) % ring.Length;
                count--;
            }
            for (; i < samples; i++) output[i] = 0f;
        }
        return RESULT.OK;
    }

    private void Enqueue(float value)
    {
        if (count == ring.Length) { read = (read + 1) % ring.Length; count--; }
        ring[write] = value;
        write = (write + 1) % ring.Length;
        count++;
    }

    private bool SetOutputActive(bool active)
    {
        lock (gate)
        {
            if (acceptDecodedAudio == active)
            {
                if (!active) read = write = count = 0;
                return false;
            }

            read = write = count = 0;
            acceptDecodedAudio = active;
            return active;
        }
    }

    private void ResynchronizeDecoder()
    {
        // Re-entering audible range after FMOD virtualization needs both sides
        // of VLC restarted from one clock position. Clearing PCM alone removes
        // the large backlog but can leave the freshly decoded audio a fraction
        // behind the video frame already being displayed.
        if (!player.IsPlaying || !player.IsSeekable) return;
        long currentTime = player.Time;
        if (currentTime <= 0) return;
        if (player.SetTime(currentTime, true))
            MelonLogger.Msg("[Boxroom-TV] Resynchronized VLC audio and video after returning to audible range.");
    }

    public void Dispose()
    {
        if (disposed) return;
        disposed = true;
        DisposeFmod();
        lock (gate) { read = write = count = 0; }
    }

    private void DisposeFmod()
    {
        if (channel.hasHandle()) { channel.stop(); channel.clearHandle(); }
        if (sound.hasHandle()) { sound.release(); sound.clearHandle(); }
    }
}
