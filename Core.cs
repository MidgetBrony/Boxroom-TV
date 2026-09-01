using BR_MediaAPI;
using Boxroom_TV.TV;
using Boxroom_TV.Videos;
using MelonLoader;
using ModsPanel;
using SteamShelf;
using SteamShelf.Input;
using SteamShelf.Placeables;
using SteamShelf.PlayerTools;
using UnityEngine;
using System;
using System.Collections.Concurrent;

[assembly: MelonInfo(typeof(Boxroom_TV.Core), "Boxroom-TV", "3.3.1", "MidgetBrony")]
[assembly: MelonGame("NestedLoop", "BOXROOM")]
[assembly: MelonAdditionalDependencies("BR_MediaAPI", "ModsPanel")]

namespace Boxroom_TV;

public sealed class Core : MelonMod
{
    internal const int MovieMediaTypeId = 1200;
    internal const string OwnerId = "com.midgetbrony.boxroom-tv";

    internal static MelonPreferences_Entry<float> DefaultVolume;
    internal static MelonPreferences_Entry<bool> AmbientGlow;
    internal static MelonPreferences_Entry<bool> ResumePlayback;
    private static readonly ConcurrentQueue<Action> MainThreadActions = new();

    private PlayerInteractionTool interactionTool;
    private bool primaryWasPressed;

    public override void OnInitializeMelon()
    {
        MelonPreferences_Category preferences = MelonPreferences.CreateCategory("Boxroom-TV");
        DefaultVolume = preferences.CreateEntry("DefaultVolume", 0.8f, "Default volume");
        AmbientGlow = preferences.CreateEntry("AmbientGlow", true, "Ambient screen glow");
        ResumePlayback = preferences.CreateEntry("ResumePlayback", true, "Resume playback");

        var definition = new MediaTypeDefinition
        {
            Id = MovieMediaTypeId,
            Key = OwnerId + ".movies",
            DisplayName = "Movies",
            ModelType = typeof(MovieItem),
            Library = MovieLibrary.Instance,
            AllowOnShelves = true,
            CreateUnplacedMediaBox = true,
            UnplacedMediaBoxName = "Movies Box",
            UnplacedMediaBoxDescription = "All movies and shows that are not currently placed",
            OnOpen = item => ShowMovieHelp((MovieItem)item),
            Inspect = new MediaInspectDefinition
            {
                PrimaryActionLabel = "Play on TV",
                OnPrimaryAction = context => ShowMovieHelp((MovieItem)context.Item)
            },
            LibraryFolder = new MediaLibraryFolderOptions
            {
                DefaultPath = MovieLibrary.DefaultLibraryRoot,
                Label = "Movie Library Location",
                PanelTitle = "Boxroom-TV",
                Reload = MovieLibrary.Instance.Reload,
                GetStatus = MovieLibrary.Instance.GetStatus
            }
        };

        SharedMediaCasePrefabs.Configure(definition);
        MediaApi.Register(definition);
        RegisterSettings();
        LoggerInstance.Msg("Boxroom-TV 3 initialized with BR-MediaAPI and ModsPanel.");
    }

    public override void OnUpdate()
    {
        while (MainThreadActions.TryDequeue(out Action action))
            try { action(); } catch (Exception exception) { LoggerInstance.Error(exception.ToString()); }
        if (interactionTool == null)
            interactionTool = UnityEngine.Object.FindFirstObjectByType<PlayerInteractionTool>();

        PlayerInputContext input = Singleton<InputManager>.Instance?.CurrentPlayerInputContext;
        bool primaryPressed = input != null && input.PrimaryPressedThisFrame;
        if (primaryPressed && !primaryWasPressed && !ModsUi.IsMenuOpen)
            TryUseTelevision();
        primaryWasPressed = primaryPressed;

        if (Input.GetKeyDown(KeyCode.T) && !ModsUi.IsMenuOpen)
            TryOpenLookedAtTelevision();
    }

    internal static void PostToMainThread(Action action)
    {
        if (action != null) MainThreadActions.Enqueue(action);
    }

    private void TryUseTelevision()
    {
        if (!TryGetLookedAtTelevision(out GameImagePainter painter, out PlacementTag tag)) return;
        TVController television = TVController.For(painter, tag);
        if (television == null) return;

        if (interactionTool?.CurrentHeldMediaItem is MovieItem movie)
            television.Play(movie);
        else if (interactionTool != null && !interactionTool.IsHoldingProp)
            television.ShowRemote();
    }

    private void TryOpenLookedAtTelevision()
    {
        if (!TryGetLookedAtTelevision(out GameImagePainter painter, out PlacementTag tag)) return;
        TVController.For(painter, tag)?.ShowRemote();
    }

    private bool TryGetLookedAtTelevision(out GameImagePainter painter, out PlacementTag tag)
    {
        painter = null;
        tag = interactionTool?.LookingAtPlaceableTag;
        if (tag != null) painter = tag.GetComponent<GameImagePainter>();

        if (painter == null)
        {
            Camera camera = Camera.main;
            if (camera == null || !Physics.Raycast(camera.transform.position, camera.transform.forward, out RaycastHit hit, 4f))
                return false;
            painter = hit.collider.GetComponentInParent<GameImagePainter>();
            tag = painter?.GetComponent<PlacementTag>();
        }
        return TVDisplay.TryGet(painter, tag, out _);
    }

    private static void RegisterSettings()
    {
        ModsPanelApi.RegisterSection(OwnerId, "Boxroom-TV", 120).Clear()
            .AddSlider("volume", "Default TV volume", () => DefaultVolume.Value,
                value => { DefaultVolume.Value = Mathf.Clamp01(value); MelonPreferences.Save(); }, 0f, 1f, false, "0%")
            .AddToggle("glow", "Ambient screen glow", () => AmbientGlow.Value,
                value => { AmbientGlow.Value = value; MelonPreferences.Save(); TVController.RefreshAllSettings(); })
            .AddToggle("resume", "Resume saved playback", () => ResumePlayback.Value,
                value => { ResumePlayback.Value = value; MelonPreferences.Save(); })
            .AddLabel("vlc-status", "Playback is powered by the open-source VLC, LibVLCSharp, and VLC for Unity projects. Original media plays directly without conversion.")
            .AddLabel("credits", "Video playback credits: VideoLAN contributors, LibVLCSharp contributors, and VLC for Unity contributors. Licensed under LGPL 2.1 or later.")
            .AddLabel("help", "Put each movie or TV season in its own folder. Cases can contain multiple video files as episodes. Hold a case and use it on a supported TV.");
    }

    private static void ShowMovieHelp(MovieItem movie)
    {
        string detail = movie == null ? "Movie" : $"{movie.DisplayName} ({movie.VideoPaths.Count} file{(movie.VideoPaths.Count == 1 ? "" : "s")})";
        ModsUi.ShowToast($"{detail}: hold the case and use it on a TV.", 5f);
    }
}
