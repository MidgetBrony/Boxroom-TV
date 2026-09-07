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
    private readonly float[] ring = new float[SampleRate * Channels * 4];
    private readonly MediaPlayer player;
    private int read, write, count;
    private Sound sound;
    private Channel channel;
    private ChannelGroup musicGroup;
    private bool disposed, loggedInput, loggedOutput;
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
        channel.setVolume(Mathf.Clamp01(volume) * falloff);
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
