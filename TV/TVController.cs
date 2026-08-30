using Boxroom_TV.Videos;
using MelonLoader;
using ModsPanel;
using System;
using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using UnityEngine;

namespace Boxroom_TV.TV;

public sealed class TVController : MonoBehaviour
{
    private static readonly List<TVController> All = new();
    private const float MinimumAudioDistance = 0.5f;
    private const float MaximumAudioDistance = 8f;

    private Renderer targetRenderer;
    private int materialIndex;
    private Material screenMaterial;
    private Texture idleTexture;
    private Vector2 idleTextureScale;
    private Vector2 idleTextureOffset;
    private VlcPlayerBackend player;
    private Light glow;
    private RenderTexture glowSample;
    private Texture2D glowPixels;
    private List<string> videos = new();
    private int currentIndex;
    private float volume;
    private float brightness = 1f;
    private bool powered = true;
    private bool loop = true;
    private float saveTimer;
    private float glowTimer;
    private string stateKey;
    private string originalPath;
    private int loadGeneration;
    private bool showAdvancedRemote;
    private bool showUrlEntry;
    private string networkUrl = string.Empty;

    public static TVController For(GameImagePainter painter, SteamShelf.Placeables.PlacementTag tag)
    {
        if (!TVDisplay.TryGet(painter, tag, out TVDisplay display)) return null;
        TVController controller = painter.GetComponent<TVController>() ?? painter.gameObject.AddComponent<TVController>();
        controller.Setup(display);
        return controller;
    }

    internal static void RefreshAllSettings()
    {
        foreach (TVController television in All.ToArray()) television.ApplyGlow();
    }

    private void Setup(TVDisplay display)
    {
        if (player != null) return;
        targetRenderer = display.Renderer;
        materialIndex = display.MaterialIndex;
        Material[] materials = targetRenderer.materials;
        if (display.OverrideMaterial != null) materials[materialIndex] = new Material(display.OverrideMaterial);
        else materials[materialIndex] = new Material(materials[materialIndex]);
        targetRenderer.materials = materials;
        screenMaterial = targetRenderer.materials[materialIndex];
        idleTexture = screenMaterial.mainTexture;
        idleTextureScale = screenMaterial.mainTextureScale;
        idleTextureOffset = screenMaterial.mainTextureOffset;
        stateKey = TVStateStore.CreateKey(transform, tagId: GetComponent<SteamShelf.Placeables.PlacementTag>()?.PlaceableData?.ID);
        volume = Mathf.Clamp01(Core.DefaultVolume.Value);

        player = gameObject.AddComponent<VlcPlayerBackend>();
        player.playOnAwake = false;
        player.isLooping = loop;
        player.errorReceived += OnPlayerError;
        player.loopPointReached += OnPlaybackReachedEnd;
        player.prepareCompleted += prepared => { if (powered) prepared.Play(); };

        var glowObject = new GameObject("Boxroom-TV Glow");
        glowObject.transform.SetParent(transform, false);
        glowObject.transform.position = targetRenderer.bounds.center;
        glow = glowObject.AddComponent<Light>();
        glow.type = LightType.Point;
        glow.range = 3f;
        glow.shadows = LightShadows.None;

        All.Add(this);
        RestoreState();
        ApplyGlow();
    }

    public void Play(MovieItem movie)
    {
        if (movie == null || movie.VideoPaths.Count == 0) return;
        List<string> requested = movie.VideoPaths.Where(File.Exists).ToList();
        if (videos.SequenceEqual(requested) && player != null && player.isPrepared)
        {
            ShowRemote();
            return;
        }
        videos = requested;
        if (videos.Count == 0) { ModsUi.ShowToast($"No playable files found for {movie.DisplayName}."); return; }
        currentIndex = 0;
        powered = true;
        LoadCurrent();
        ModsUi.ShowToast($"Playing {movie.DisplayName}");
    }

    public void ShowRemote()
    {
        string title = videos.Count == 0 ? "No video loaded" : DisplayTitle(videos[currentIndex]);
        var menu = ModsUi.CreateMenu(Core.OwnerId + ".remote", "Boxroom-TV Remote", title);
        menu.Eyebrow = "TV REMOTE";
        float duration = Mathf.Max(1f, (float)(player?.length ?? 0));
        float durationMinutes = duration / 60f;
        menu.AddSlider("Timeline", () => (float)(player?.time ?? 0) / 60f,
                minutes => SetPlaybackTime(minutes * 60f), 0f, durationMinutes, true,
                minutes => $"{Format(minutes * 60f)} / {Format(duration)}")
            .AddButton(player != null && player.isPlaying ? "Pause" : "Play", TogglePlayPause);
        if (videos.Count > 1)
            menu.AddButton("Previous episode", Previous).AddButton("Next episode", Next);

        menu.AddSlider("Volume", () => volume, value => { volume = value; SaveState(); },
                0f, 1f, false, value => $"{Mathf.RoundToInt(value * 100)}%")
            .AddDropdown("Audio language", () => player?.AudioTrackOptions ?? new[] { "Default" },
                () => player?.SelectedAudioTrackIndex ?? 0, index => player?.SelectAudioTrack(index))
            .AddDropdown("Subtitles", () => player?.SubtitleTrackOptions ?? new[] { "Off" },
                () => player?.SelectedSubtitleTrackIndex ?? 0, index => player?.SelectSubtitleTrack(index))
            .AddButton(showUrlEntry ? "Hide online player" : "Play an online video", () => { showUrlEntry = !showUrlEntry; ShowRemote(); });

        if (showUrlEntry)
            menu.AddTextInput("Video, YouTube, or Twitch URL", () => networkUrl, value => networkUrl = value,
                    "https://www.youtube.com/... or https://www.twitch.tv/...")
                .AddButton("Play online video", PlayNetworkUrl, "Resolve supported pages, then open with VLC");

        menu.AddButton(showAdvancedRemote ? "Hide advanced controls" : "Advanced controls",
            () => { showAdvancedRemote = !showAdvancedRemote; ShowRemote(); });
        if (showAdvancedRemote)
            menu.AddHeading("Advanced")
                .AddSlider("Screen brightness", () => brightness,
                    value => { brightness = value; ApplyScreen(); ApplyGlow(); SaveState(); },
                    0f, 3f, false, value => value.ToString("0.0"))
                .AddSlider("Playback speed", () => player?.rate ?? 1f,
                    value => { if (player != null) player.rate = value; },
                    0.5f, 2f, false, value => value.ToString("0.00") + "x")
                .AddToggle("Loop current video", () => loop,
                    value => { loop = value; if (player != null) player.isLooping = value; SaveState(); })
                .AddButton(powered ? "Power off" : "Power on", TogglePower)
                .AddButton("Stop and clear", Stop);
        menu.Show();
    }

    private void PlayNetworkUrl()
    {
        string requested = (networkUrl ?? string.Empty).Trim();
        if (!Uri.TryCreate(requested, UriKind.Absolute, out Uri uri) ||
            (uri.Scheme != Uri.UriSchemeHttp && uri.Scheme != Uri.UriSchemeHttps))
        {
            ModsUi.ShowToast("Enter a valid HTTP or HTTPS video URL.");
            return;
        }
        ModsUi.CloseMenu();
        StartCoroutine(ResolveAndPlayNetworkUrl(requested, 0));
    }

    private IEnumerator ResolveAndPlayNetworkUrl(string requested, double startTime)
    {
        string playbackUrl = requested;
        string audioUrl = null;
        bool isYouTube = IsYouTubeUrl(requested);
        bool isTwitch = IsTwitchUrl(requested);
        if (isYouTube || isTwitch)
        {
            string resolverName = Application.platform == RuntimePlatform.LinuxPlayer ? "yt-dlp" : "yt-dlp.exe";
            string resolver = Path.GetFullPath(Path.Combine(Application.dataPath, "..", "UserData", "Boxroom-TV", "Tools", resolverName));
            if (!File.Exists(resolver))
            {
                ModsUi.ShowToast("The YouTube resolver is not installed.", 7f);
                yield break;
            }
            string service = isTwitch ? "Twitch" : "YouTube";
            ModsUi.ShowToast("Resolving " + service + " video...");
            string format = isYouTube
                ? "bestvideo[height<=720][ext=mp4]+bestaudio[ext=m4a]/bestvideo[height<=720]+bestaudio"
                : "best[height<=720]/best";
            var start = new System.Diagnostics.ProcessStartInfo
            {
                FileName = resolver,
                Arguments = "--no-playlist --no-warnings --get-url -f \"" + format + "\" -- \"" + requested.Replace("\"", "\\\"") + "\"",
                UseShellExecute = false, CreateNoWindow = true,
                RedirectStandardOutput = true, RedirectStandardError = true
            };
            System.Diagnostics.Process process;
            try { process = System.Diagnostics.Process.Start(start); }
            catch (Exception exception)
            {
                MelonLogger.Error("[Boxroom-TV] Could not start yt-dlp: " + exception);
                ModsUi.ShowToast("Could not start the YouTube resolver. See Latest.log.", 7f);
                yield break;
            }
            float deadline = Time.realtimeSinceStartup + 45f;
            while (!process.HasExited && Time.realtimeSinceStartup < deadline) yield return null;
            if (!process.HasExited)
            {
                try { process.Kill(); } catch { }
                process.Dispose();
                ModsUi.ShowToast(service + " resolution timed out.", 7f);
                yield break;
            }
            string output = process.StandardOutput.ReadToEnd();
            string error = process.StandardError.ReadToEnd();
            int exitCode = process.ExitCode;
            process.Dispose();
            string[] resolvedUrls = output.Split(new[] { '\r', '\n' }, StringSplitOptions.RemoveEmptyEntries)
                .Where(line => Uri.TryCreate(line, UriKind.Absolute, out _)).ToArray();
            playbackUrl = resolvedUrls.FirstOrDefault();
            audioUrl = resolvedUrls.Skip(1).FirstOrDefault();
            bool missingAudio = isYouTube && !Uri.TryCreate(audioUrl, UriKind.Absolute, out _);
            if (exitCode != 0 || !Uri.TryCreate(playbackUrl, UriKind.Absolute, out _) || missingAudio)
            {
                MelonLogger.Error("[Boxroom-TV] yt-dlp failed: " + (string.IsNullOrWhiteSpace(error) ? "No playable URL returned." : error.Trim()));
                ModsUi.ShowToast(service + " could not be resolved. The stream may be offline or restricted.", 7f);
                yield break;
            }
        }
        videos = new List<string> { requested };
        currentIndex = 0;
        powered = true;
        originalPath = requested;
        int generation = ++loadGeneration;
        player.Stop();
        player.audioSlaveUrl = audioUrl;
        ApplyScreen();
        BeginPreparedPlayback(playbackUrl, startTime, generation);
        SaveState();
        ModsUi.ShowToast(isTwitch ? "Playing Twitch stream" : isYouTube ? "Playing YouTube video" : "Opening URL with VLC...");
    }

    private static bool IsYouTubeUrl(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri uri)) return false;
        string host = uri.Host.ToLowerInvariant();
        return host == "youtu.be" || host == "youtube.com" || host.EndsWith(".youtube.com");
    }

    private static bool IsTwitchUrl(string source)
    {
        if (!Uri.TryCreate(source, UriKind.Absolute, out Uri uri)) return false;
        string host = uri.Host.ToLowerInvariant();
        return host == "twitch.tv" || host.EndsWith(".twitch.tv");
    }

    private static bool NeedsWebResolver(string source) => IsYouTubeUrl(source) || IsTwitchUrl(source);

    private static string DisplayTitle(string source)
    {
        if (Uri.TryCreate(source, UriKind.Absolute, out Uri uri) && !uri.IsFile)
            return string.IsNullOrWhiteSpace(uri.Host) ? "Network video" : uri.Host;
        return Path.GetFileNameWithoutExtension(source);
    }

    private void SetPlaybackTime(float seconds)
    {
        if (player?.isPrepared != true) return;
        player.time = Math.Max(0, Math.Min(player.length, seconds));
        SaveState();
    }

    private void CycleAudioTrack()
    {
        player?.CycleAudioTrack();
        ShowRemote();
    }

    private void CycleSubtitleTrack()
    {
        player?.CycleSubtitleTrack();
        ShowRemote();
    }

    private void LoadCurrent(double startTime = 0)
    {
        if (player == null || videos.Count == 0) return;
        currentIndex = ((currentIndex % videos.Count) + videos.Count) % videos.Count;
        originalPath = videos[currentIndex];
        int generation = ++loadGeneration;
        player.Stop();
        ApplyScreen();
        BeginPreparedPlayback(originalPath, startTime, generation);
        SaveState();
    }

    private void BeginPreparedPlayback(string path, double startTime, int generation)
    {
        if (generation != loadGeneration || string.IsNullOrWhiteSpace(path)) return;
        player.url = path;
        Action<VlcPlayerBackend> seek = null;
        seek = prepared => { prepared.prepareCompleted -= seek; if (startTime > 0 && startTime < prepared.length) prepared.time = startTime; };
        player.prepareCompleted += seek;
        player.Prepare();
        ApplyScreen();
    }

    private void OnPlayerError(VlcPlayerBackend _, string error)
    {
        MelonLogger.Error("[Boxroom-TV] " + error);
        ModsUi.ShowToast(error + " See MelonLoader/Latest.log.", 7f);
    }

    private void OnPlaybackReachedEnd(VlcPlayerBackend _)
    {
        if (!loop && videos.Count > 1) Next();
    }

    private void TogglePlayPause()
    {
        if (player == null || videos.Count == 0) return;
        if (!powered) TogglePower();
        else if (player.isPlaying) player.Pause(); else player.Play();
        SaveState();
    }

    private void Previous() { if (videos.Count > 0) { currentIndex--; LoadCurrent(); } }
    private void Next() { if (videos.Count > 0) { currentIndex++; LoadCurrent(); } }
    private void Seek(double seconds) { if (player?.isPrepared == true) player.time = Math.Max(0, Math.Min(player.length, player.time + seconds)); SaveState(); }
    private void ChangeVolume(float delta) { volume = Mathf.Clamp01(volume + delta); SaveState(); ShowRemote(); }
    private void ChangeBrightness(float delta) { brightness = Mathf.Clamp(brightness + delta, 0f, 3f); ApplyScreen(); ApplyGlow(); SaveState(); ShowRemote(); }

    private void TogglePower()
    {
        powered = !powered;
        if (powered) { player.enabled = true; if (videos.Count > 0) player.Play(); }
        else { player.Pause(); player.enabled = false; }
        ApplyScreen();
        ApplyGlow();
        SaveState();
        ShowRemote();
    }

    private void Stop()
    {
        loadGeneration++;
        player?.Stop();
        videos.Clear();
        originalPath = null;
        currentIndex = 0;
        ApplyScreen();
        ApplyGlow();
        SaveState();
        ModsUi.CloseMenu();
    }

    private void Update()
    {
        if (player != null)
        {
            Camera camera = Camera.main;
            float falloff = camera == null ? 0f : Mathf.Clamp01(Mathf.InverseLerp(MaximumAudioDistance, MinimumAudioDistance, Vector3.Distance(camera.transform.position, transform.position)));
            player.volume = volume * falloff;

            // VLC creates its external Unity texture after playback has started.
            // Attach it as soon as the first frame becomes available instead of
            // leaving the material on the temporary idle texture.
            if (powered && videos.Count > 0 && player.texture != null && screenMaterial?.mainTexture != player.texture)
                ApplyScreen();
        }

        saveTimer += Time.deltaTime;
        if (videos.Count > 0 && saveTimer >= 5f) { saveTimer = 0; SaveState(); }
        if (!Core.AmbientGlow.Value || !powered || player?.texture == null) return;
        glowTimer += Time.deltaTime;
        if (glowTimer >= 0.12f) { glowTimer = 0; SampleGlow(); }
    }

    private void ApplyScreen()
    {
        if (screenMaterial == null) return;
        if (!powered || videos.Count == 0)
        {
            screenMaterial.mainTexture = Texture2D.blackTexture;
            RestoreIdleTextureMapping();
        }
        else if (player?.texture != null)
        {
            screenMaterial.mainTexture = player.texture;
            RestoreIdleTextureMapping();
        }
        else
        {
            screenMaterial.mainTexture = idleTexture;
            RestoreIdleTextureMapping();
        }
        if (screenMaterial.HasProperty("_EmissionStrength")) screenMaterial.SetFloat("_EmissionStrength", powered ? brightness : 0f);
    }

    private void RestoreIdleTextureMapping()
    {
        screenMaterial.mainTextureScale = idleTextureScale;
        screenMaterial.mainTextureOffset = idleTextureOffset;
    }

    private void ApplyGlow()
    {
        if (glow != null) glow.intensity = Core.AmbientGlow.Value && powered && videos.Count > 0 ? Mathf.Max(0.3f, brightness * 1.2f) : 0f;
    }

    private void SampleGlow()
    {
        glowSample ??= new RenderTexture(4, 4, 0, RenderTextureFormat.ARGB32);
        glowPixels ??= new Texture2D(4, 4, TextureFormat.RGB24, false);
        Graphics.Blit(player.texture, glowSample);
        RenderTexture previous = RenderTexture.active;
        RenderTexture.active = glowSample;
        glowPixels.ReadPixels(new Rect(0, 0, 4, 4), 0, 0);
        glowPixels.Apply(false);
        RenderTexture.active = previous;
        Color[] pixels = glowPixels.GetPixels();
        if (pixels.Length == 0) return;
        Color average = pixels.Aggregate(Color.black, (sum, pixel) => sum + pixel) / pixels.Length;
        glow.color = Color.Lerp(glow.color, average.maxColorComponent < 0.05f ? Color.white : average, 0.4f);
    }

    private string FormatTime() => player?.isPrepared == true ? $"{Format(player.time)} / {Format(player.length)}" : "Not ready";
    private static string Format(double seconds) => $"{Math.Floor(seconds / 60):0}:{Math.Floor(seconds % 60):00}";

    private void RestoreState()
    {
        if (!Core.ResumePlayback.Value) return;
        TVSaveEntry saved = TVStateStore.Load(stateKey);
        if (saved?.VideoFiles == null || saved.VideoFiles.Count == 0) return;
        videos = saved.VideoFiles.Where(IsPlayableSource).ToList();
        if (videos.Count == 0) return;
        currentIndex = Mathf.Clamp(saved.CurrentIndex, 0, videos.Count - 1);
        brightness = Mathf.Clamp(saved.Brightness, 0f, 3f);
        volume = Mathf.Clamp01(saved.Volume);
        powered = saved.IsOn;
        loop = saved.IsLooping;
        player.isLooping = loop;
        if (NeedsWebResolver(videos[currentIndex])) StartCoroutine(ResolveAndPlayNetworkUrl(videos[currentIndex], saved.PlaybackTime));
        else LoadCurrent(saved.PlaybackTime);
        if (!powered) player.prepareCompleted += prepared => prepared.Pause();
    }

    private static bool IsPlayableSource(string source) => File.Exists(source) ||
        (Uri.TryCreate(source, UriKind.Absolute, out Uri uri) &&
         (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps));

    private void SaveState()
    {
        if (string.IsNullOrWhiteSpace(stateKey)) return;
        TVStateStore.Save(stateKey, new TVSaveEntry
        {
            VideoFiles = new List<string>(videos), CurrentIndex = currentIndex,
            PlaybackTime = player?.isPrepared == true ? player.time : 0,
            Brightness = brightness, Volume = volume, IsOn = powered, IsLooping = loop
        });
    }

    private void OnDestroy()
    {
        SaveState();
        All.Remove(this);
        if (glowSample != null) { glowSample.Release(); Destroy(glowSample); }
        if (glowPixels != null) Destroy(glowPixels);
        if (screenMaterial != null) Destroy(screenMaterial);
    }
}
