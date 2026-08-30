using LibVLCSharp;
using MelonLoader;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using UnityEngine;

namespace Boxroom_TV.TV;

internal sealed class VlcPlayerBackend : MonoBehaviour
{
    private const string UnityPlugin = "VLCUnityPlugin";
    private static readonly object InitializeLock = new();
    private static LibVLC libVlc;

    private readonly ConcurrentQueue<Action> mainThread = new();
    private MediaPlayer mediaPlayer;
    private Media media;
    private Texture2D videoTexture;
    private RenderTexture outputTexture;
    private bool stoppingIntentionally;
    private bool errorReported;

    internal event Action<VlcPlayerBackend> prepareCompleted;
    internal event Action<VlcPlayerBackend, string> errorReceived;
    internal event Action<VlcPlayerBackend> loopPointReached;

    internal string url { get; set; }
    internal string audioSlaveUrl { get; set; }
    internal bool playOnAwake { get; set; }
    internal bool isLooping { get; set; }
    internal bool isPrepared { get; private set; }
    internal bool isPlaying => mediaPlayer?.IsPlaying == true;
    internal Texture texture => outputTexture;
    internal double time
    {
        get => Math.Max(0, (mediaPlayer?.Time ?? 0) / 1000d);
        set { mediaPlayer?.SetTime((long)(Math.Max(0, value) * 1000), true); }
    }
    internal double length => Math.Max(0, (mediaPlayer?.Length ?? 0) / 1000d);
    internal float rate
    {
        get => mediaPlayer?.Rate ?? 1f;
        set { if (mediaPlayer != null) mediaPlayer.SetRate(Mathf.Clamp(value, 0.5f, 2f)); }
    }
    internal float volume
    {
        get => (mediaPlayer?.Volume ?? 0) / 100f;
        set { mediaPlayer?.SetVolume(Mathf.RoundToInt(Mathf.Clamp01(value) * 100)); }
    }

    [DllImport(UnityPlugin, CallingConvention = CallingConvention.Cdecl, EntryPoint = "libvlc_unity_set_color_space")]
    private static extern void SetColorSpace(int colorSpace);

    private void Awake()
    {
        try
        {
            EnsureInitialized();
            mediaPlayer = new MediaPlayer(libVlc);
            mediaPlayer.Playing += (_, _) => mainThread.Enqueue(OnPlaying);
            mediaPlayer.EncounteredError += (_, _) => mainThread.Enqueue(() => OnError("VLC could not decode or play this media."));
            mediaPlayer.Stopping += (_, _) => mainThread.Enqueue(OnStopping);
        }
        catch (Exception exception)
        {
            MelonLogger.Error("[Boxroom-TV] VLC initialization failed: " + exception);
            mainThread.Enqueue(() => OnError("VLC failed to initialize. See MelonLoader/Latest.log."));
        }
    }

    private static void EnsureInitialized()
    {
        lock (InitializeLock)
        {
            if (libVlc != null) return;
            string nativeRoot = Path.Combine(Application.dataPath, "Plugins", "x86_64");
            if (!File.Exists(Path.Combine(nativeRoot, "VLCUnityPlugin.dll")) ||
                !File.Exists(Path.Combine(nativeRoot, "libvlc.dll")))
                throw new FileNotFoundException("The Boxroom-TV VLC native runtime is not installed in " + nativeRoot);

            LibVLCSharp.Core.Initialize(nativeRoot);
            SetColorSpace(QualitySettings.activeColorSpace == UnityEngine.ColorSpace.Linear ? 1 : 0);
            libVlc = new LibVLC("--no-video-title-show", "--no-snapshot-preview", "--quiet");
            MelonLogger.Msg("[Boxroom-TV] VLC runtime initialized from " + nativeRoot);
        }
    }

    internal void Prepare()
    {
        if (mediaPlayer == null || string.IsNullOrWhiteSpace(url))
        {
            OnError("No VLC media path was supplied.");
            return;
        }

        string requestedAudioSlave = audioSlaveUrl;
        Stop();
        audioSlaveUrl = requestedAudioSlave;
        isPrepared = false;
        stoppingIntentionally = false;
        errorReported = false;
        bool networkSource = Uri.TryCreate(url, UriKind.Absolute, out Uri sourceUri) &&
            (sourceUri.Scheme == Uri.UriSchemeHttp || sourceUri.Scheme == Uri.UriSchemeHttps);
        media = new Media(url, networkSource ? FromType.FromLocation : FromType.FromPath, ":avcodec-hw=any");
        if (!string.IsNullOrWhiteSpace(audioSlaveUrl)) media.AddOption(":input-slave=" + audioSlaveUrl);
        mediaPlayer.Media = media;
        if (!mediaPlayer.Play()) OnError("VLC rejected the media before playback started.");
    }

    internal void Play()
    {
        if (mediaPlayer == null) return;
        if (isPrepared) mediaPlayer.SetPause(false);
        else if (media != null) mediaPlayer.Play();
    }

    internal void Pause() => mediaPlayer?.SetPause(true);

    internal string SelectedAudioTrack => GetSelectedTrackName(TrackType.Audio, "Default");
    internal string SelectedSubtitleTrack => GetSelectedTrackName(TrackType.Text, "Off");
    internal int AudioTrackCount => GetTrackCount(TrackType.Audio);
    internal int SubtitleTrackCount => GetTrackCount(TrackType.Text);

    internal void CycleAudioTrack() => CycleTrack(TrackType.Audio, allowOff: false);
    internal void CycleSubtitleTrack() => CycleTrack(TrackType.Text, allowOff: true);
    internal IReadOnlyList<string> AudioTrackOptions => GetTrackOptions(TrackType.Audio, includeOff: false);
    internal IReadOnlyList<string> SubtitleTrackOptions => GetTrackOptions(TrackType.Text, includeOff: true);
    internal int SelectedAudioTrackIndex => GetSelectedTrackIndex(TrackType.Audio, includeOff: false);
    internal int SelectedSubtitleTrackIndex => GetSelectedTrackIndex(TrackType.Text, includeOff: true);
    internal void SelectAudioTrack(int index) => SelectTrack(TrackType.Audio, index, includeOff: false);
    internal void SelectSubtitleTrack(int index) => SelectTrack(TrackType.Text, index, includeOff: true);

    internal void Stop()
    {
        stoppingIntentionally = true;
        try { mediaPlayer?.Stop(); } catch { }
        Media current = mediaPlayer?.Media;
        current?.Dispose();
        if (mediaPlayer != null) mediaPlayer.Media = null;
        media?.Dispose();
        media = null;
        audioSlaveUrl = null;
        isPrepared = false;
    }

    private void Update()
    {
        while (mainThread.TryDequeue(out Action action)) action();
        if (mediaPlayer == null || !isPrepared) return;

        uint width = 0, height = 0;
        mediaPlayer.Size(0, ref width, ref height);
        if (width == 0 || height == 0) return;

        IntPtr pointer = mediaPlayer.GetTexture(width, height, out bool updated);
        if (pointer == IntPtr.Zero) return;
        if (videoTexture == null || videoTexture.width != width || videoTexture.height != height)
        {
            DestroyVideoTextures();
            videoTexture = Texture2D.CreateExternalTexture((int)width, (int)height, TextureFormat.RGBA32, false, false, pointer);
            outputTexture = new RenderTexture((int)width, (int)height, 0, RenderTextureFormat.ARGB32);
            outputTexture.Create();
            updated = true;
        }

        if (!updated) return;
        videoTexture.UpdateExternalTexture(pointer);

        // Never expose VLC's pointer-swapped native texture directly to a game
        // material. Copy each completed frame into a stable Unity texture and
        // correct the native bridge's 180-degree orientation during the copy.
        Graphics.Blit(videoTexture, outputTexture, new Vector2(-1f, -1f), new Vector2(1f, 1f));
    }

    private void OnPlaying()
    {
        if (!isPrepared)
        {
            isPrepared = true;
            prepareCompleted?.Invoke(this);
        }
    }

    private void OnStopping()
    {
        if (stoppingIntentionally) { stoppingIntentionally = false; return; }
        if (isLooping && mediaPlayer != null)
        {
            mediaPlayer.SetTime(0, true);
            mediaPlayer.Play();
            return;
        }
        loopPointReached?.Invoke(this);
    }

    private void OnError(string message)
    {
        if (errorReported) return;
        errorReported = true;
        isPrepared = false;
        stoppingIntentionally = true;
        try { mediaPlayer?.Stop(); } catch { }
        errorReceived?.Invoke(this, message);
    }

    private int GetTrackCount(TrackType type)
    {
        using MediaTrackList tracks = mediaPlayer?.Tracks(type);
        return tracks == null ? 0 : (int)tracks.Count;
    }

    private string GetSelectedTrackName(TrackType type, string fallback)
    {
        using MediaTrack selected = mediaPlayer?.SelectedTrack(type);
        return selected == null ? fallback : TrackName(selected);
    }

    private void CycleTrack(TrackType type, bool allowOff)
    {
        if (mediaPlayer == null) return;
        using MediaTrackList tracks = mediaPlayer.Tracks(type);
        if (tracks == null || tracks.Count == 0) return;
        using MediaTrack selected = mediaPlayer.SelectedTrack(type);
        string selectedId = selected?.Id ?? string.Empty;
        var ids = new List<string>();
        for (uint index = 0; index < tracks.Count; index++)
        {
            using MediaTrack track = tracks[index];
            if (track != null && !string.IsNullOrEmpty(track.Id)) ids.Add(track.Id);
        }
        if (ids.Count == 0) return;
        int current = ids.IndexOf(selectedId);
        if (allowOff && current == ids.Count - 1) mediaPlayer.Select(type, string.Empty);
        else mediaPlayer.Select(type, ids[(current + 1 + ids.Count) % ids.Count]);
    }

    private IReadOnlyList<string> GetTrackOptions(TrackType type, bool includeOff)
    {
        var options = new List<string>();
        if (includeOff) options.Add("Off");
        using MediaTrackList tracks = mediaPlayer?.Tracks(type);
        if (tracks == null) return options;
        for (uint index = 0; index < tracks.Count; index++)
        {
            using MediaTrack track = tracks[index];
            if (track != null) options.Add(TrackName(track));
        }
        if (options.Count == 0) options.Add("Default");
        return options;
    }

    private int GetSelectedTrackIndex(TrackType type, bool includeOff)
    {
        using MediaTrack selected = mediaPlayer?.SelectedTrack(type);
        if (selected == null) return 0;
        using MediaTrackList tracks = mediaPlayer?.Tracks(type);
        if (tracks == null) return 0;
        for (uint index = 0; index < tracks.Count; index++)
        {
            using MediaTrack track = tracks[index];
            if (track?.Id == selected.Id) return (int)index + (includeOff ? 1 : 0);
        }
        return 0;
    }

    private void SelectTrack(TrackType type, int index, bool includeOff)
    {
        if (mediaPlayer == null) return;
        if (includeOff && index <= 0)
        {
            mediaPlayer.Select(type, string.Empty);
            return;
        }
        int trackIndex = index - (includeOff ? 1 : 0);
        using MediaTrackList tracks = mediaPlayer.Tracks(type);
        if (tracks == null || trackIndex < 0 || (uint)trackIndex >= tracks.Count) return;
        using MediaTrack track = tracks[(uint)trackIndex];
        if (track != null) mediaPlayer.Select(track);
    }

    private static string TrackName(MediaTrack track)
    {
        string name = !string.IsNullOrWhiteSpace(track.Name) ? track.Name : track.Description;
        if (string.IsNullOrWhiteSpace(name)) name = track.Language;
        if (string.IsNullOrWhiteSpace(name)) name = track.Id;
        if (!string.IsNullOrWhiteSpace(track.Language) &&
            (name?.IndexOf(track.Language, StringComparison.OrdinalIgnoreCase) ?? -1) < 0)
            name += " (" + track.Language + ")";
        return string.IsNullOrWhiteSpace(name) ? "Unknown" : name;
    }

    private void OnDestroy()
    {
        Stop();
        mediaPlayer?.Dispose();
        mediaPlayer = null;
        DestroyVideoTextures();
    }

    private void DestroyVideoTextures()
    {
        if (outputTexture != null)
        {
            if (RenderTexture.active == outputTexture) RenderTexture.active = null;
            outputTexture.Release();
            Destroy(outputTexture);
            outputTexture = null;
        }
        if (videoTexture != null)
        {
            Destroy(videoTexture);
            videoTexture = null;
        }
    }
}
