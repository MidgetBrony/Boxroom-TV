using Boxroom_TV.Videos;
using HarmonyLib;
using SteamShelf.UI;
using TMPro;
using UnityEngine;

namespace Boxroom_TV.UI;

/// <summary>
/// Keeps BOXROOM's native Video catalogue page, but replaces the vanilla-only
/// format warning and workflow copy while Boxroom-TV is installed.
/// </summary>
internal static class NativeVideoGuide
{
    internal static void Apply(UI_VideoLibraryPathDisplay page)
    {
        if (page == null) return;

        Transform pageRoot = page.transform.parent ?? page.transform;
        foreach (TMP_Text label in pageRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            string current = label.text?.Trim() ?? string.Empty;

            if (current.StartsWith("HOW TO CREATE VIDEO BOXES", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label, "HOW TO USE YOUR VIDEO LIBRARY!", 42f);
            }
            else if (current.StartsWith("Choose a folder for your video collection", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label,
                    "Choose the top-level folder for your video library. Boxroom-TV scans all subfolders. " +
                    "Keep each movie or TV season in its own folder, with optional cover.jpg and NFO metadata.",
                    24f);
            }
            else if (current.StartsWith("If the status above is showing", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label,
                    "When the status shows Video cases, place the Video Container in your room. " +
                    "It holds one native case for each movie or season.\n\n" +
                    "Hold a Video case and use it on a TV or screen. Then use an empty hand on the screen—or " +
                    "press T while looking at it—to open the remote for audio and subtitles.",
                    20f);
            }
            else if (current.StartsWith("*Files must be", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label,
                    "WITH BOXROOM-TV INSTALLED, VLC SUPPORTS MP4, M4V, MOV, WEBM, MKV, AVI, " +
                    "AV1, HEVC/H.265, VP9, AND OTHER COMMON VIDEO FORMATS.",
                    20f);
            }
            else if (current.StartsWith("Set the folder path below", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label, "Choose the top-level library folder below. All subfolders are scanned automatically.", 24f);
            }
            else if (current.StartsWith("If the path looks correct", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label, "If the path is correct, select Apply:", 24f);
            }
            else if (current.StartsWith("If you add more videos later", System.StringComparison.OrdinalIgnoreCase))
            {
                SetText(label, "Added or changed videos? Select Refresh:", 24f);
            }
        }
    }

    internal static void UpdateStatus(UI_VideoLibraryPathDisplay page)
    {
        if (page == null || !MovieLibrary.IsScanComplete) return;

        Transform pageRoot = page.transform.parent ?? page.transform;
        foreach (TMP_Text label in pageRoot.GetComponentsInChildren<TMP_Text>(true))
        {
            string current = label.text?.Trim() ?? string.Empty;
            if (current.EndsWith("videos found", System.StringComparison.OrdinalIgnoreCase) ||
                current.Contains("native Video cases", System.StringComparison.OrdinalIgnoreCase))
            {
                label.text = MovieLibrary.Status;
                break;
            }
        }
    }

    private static void SetText(TMP_Text label, string text, float minimumSize)
    {
        float originalSize = label.fontSize;
        label.text = text;
        label.enableAutoSizing = true;
        label.fontSizeMin = Mathf.Min(minimumSize, originalSize);
        label.fontSizeMax = originalSize;
    }
}

[HarmonyPatch(typeof(UI_VideoLibraryPathDisplay), "OnEnable")]
internal static class NativeVideoGuideEnablePatch
{
    private static void Postfix(UI_VideoLibraryPathDisplay __instance)
    {
        NativeVideoGuide.Apply(__instance);
        NativeVideoGuide.UpdateStatus(__instance);
    }
}

[HarmonyPatch(typeof(UI_VideoLibraryPathDisplay), "UpdateStatusLabel")]
internal static class NativeVideoGuideStatusPatch
{
    private static void Postfix(UI_VideoLibraryPathDisplay __instance) => NativeVideoGuide.UpdateStatus(__instance);
}
