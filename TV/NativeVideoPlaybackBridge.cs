using HarmonyLib;
using MelonLoader;
using SteamShelf.Media.Videos;
using SteamShelf.Placeables;
using System;
using System.Linq;
using UnityEngine;

namespace Boxroom_TV.TV;

/// <summary>Routes every native BOXROOM Video play action through the VLC controller.</summary>
[HarmonyPatch(typeof(Interactable_TV), nameof(Interactable_TV.TryPlayVideo))]
internal static class NativeVideoPlaybackBridge
{
    private static bool Prefix(VideoData video, Interactable_TV preferredTv, ref bool __result)
    {
        __result = TryPlay(video, preferredTv);
        return false;
    }

    private static bool TryPlay(VideoData video, Interactable_TV preferredTv)
    {
        if (video?.VideoPaths == null || video.VideoPaths.Count == 0)
        {
            MelonLogger.Warning("[Boxroom-TV] Native Video has no files.");
            return false;
        }

        TVController controller = preferredTv == null ? null : ControllerFor(preferredTv.gameObject);
        if (controller == null)
        {
            Camera camera = Camera.main;
            controller = TVController.DiscoverAll()
                .OrderBy(candidate => camera == null
                    ? 0f
                    : Vector3.SqrMagnitude(candidate.transform.position - camera.transform.position))
                .FirstOrDefault();
        }

        if (controller == null)
        {
            MelonLogger.Warning("[Boxroom-TV] No supported TV, CRT, or monitor is available.");
            return false;
        }

        controller.Play(video);
        return true;
    }

    internal static TVController ControllerFor(GameObject source)
    {
        GameImagePainter painter = source.GetComponent<GameImagePainter>()
                                   ?? source.GetComponentInParent<GameImagePainter>()
                                   ?? source.GetComponentInChildren<GameImagePainter>(true);
        if (painter == null) return null;
        PlacementTag tag = painter.GetComponent<PlacementTag>()
                           ?? painter.GetComponentInParent<PlacementTag>();
        return TVController.For(painter, tag);
    }
}

/// <summary>
/// The native component never enters its private isPlaying state when VLC owns
/// playback. Prevent its fallback screenshot-cycle action from painting over an
/// active VLC surface.
/// </summary>
[HarmonyPatch(typeof(Interactable_TV), nameof(Interactable_TV.OnInteract))]
internal static class NativeTvInteractionPatch
{
    private static bool Prefix(Interactable_TV __instance)
    {
        TVController controller = NativeVideoPlaybackBridge.ControllerFor(__instance.gameObject);
        return controller == null || !controller.HasLoadedSource;
    }
}
