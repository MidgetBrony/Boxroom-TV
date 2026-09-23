using Boxroom_TV.TV;
using MelonLoader;
using SteamShelf;
using SteamShelf.Placeables;
using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using UnityEngine;

namespace Boxroom_TV;

/// <summary>Public playback controls for other BOXROOM mods.</summary>
public static class BoxroomTvApi
{
    private static int sessionGeneration;
    private static List<TVPlaybackSessionEntry> activeSession;

    public static bool IsSynchronizedPlaybackActive => activeSession != null;

    /// <summary>
    /// Temporarily grants one supported native screen to another mod. The TV's
    /// existing Boxroom-TV playback is paused and restored when the lease ends.
    /// A screen already leased by another owner cannot be claimed again.
    /// </summary>
    public static bool TryClaimDisplay(GameImagePainter painter, PlacementTag tag,
        string ownerId, string displayName, out BoxroomTvDisplayLease lease)
    {
        lease = null;
        if (painter == null || tag == null || string.IsNullOrWhiteSpace(ownerId)) return false;

        TVController controller = TVController.For(painter, tag);
        if (controller == null || controller.IsExternallyClaimed) return false;

        StopSynchronizedPlayback();
        if (!controller.TryBeginExternalClaim(ownerId, displayName, out int token))
            return false;

        lease = new BoxroomTvDisplayLease(controller, token, ownerId);
        return true;
    }

    /// <summary>Temporarily plays one source on every supported TV, CRT, and monitor.</summary>
    public static int PlaySynchronized(string source, string title, float durationSeconds = 60f)
    {
        if (string.IsNullOrWhiteSpace(source)) return 0;
        StopSynchronizedPlayback();

        List<TVController> displays = TVController.DiscoverAll()
            .Where(display => !display.IsExternallyClaimed).ToList();
        if (displays.Count == 0) return 0;
        Camera camera = Camera.main;
        TVController audioDisplay = camera == null
            ? displays[0]
            : displays.OrderBy(display => Vector3.SqrMagnitude(display.transform.position - camera.transform.position)).First();

        activeSession = new List<TVPlaybackSessionEntry>(displays.Count);
        foreach (TVController display in displays)
            activeSession.Add(new TVPlaybackSessionEntry(display, display.CaptureSnapshot()));

        audioDisplay.PlayTemporarySource(source, title, true);
        foreach (TVController display in displays)
            if (display != audioDisplay) display.FollowTemporarySource(audioDisplay, source);

        int generation = ++sessionGeneration;
        MelonCoroutines.Start(RestoreAfter(Math.Max(1f, durationSeconds), generation));
        return displays.Count;
    }

    /// <summary>Ends the showcase and restores every display's earlier state.</summary>
    public static void StopSynchronizedPlayback()
    {
        sessionGeneration++;
        if (activeSession == null) return;
        foreach (TVPlaybackSessionEntry entry in activeSession)
            if (entry.Controller != null) entry.Controller.RestoreSnapshot(entry.Snapshot);
        activeSession = null;
    }

    private static IEnumerator RestoreAfter(float seconds, int generation)
    {
        yield return new UnityEngine.WaitForSecondsRealtime(seconds);
        if (generation == sessionGeneration) StopSynchronizedPlayback();
    }

    private sealed class TVPlaybackSessionEntry
    {
        internal TVPlaybackSessionEntry(TVController controller, TVPlaybackSnapshot snapshot)
        {
            Controller = controller;
            Snapshot = snapshot;
        }

        internal TVController Controller { get; }
        internal TVPlaybackSnapshot Snapshot { get; }
    }
}

/// <summary>A temporary, exclusive claim on one BOXROOM television or monitor.</summary>
public sealed class BoxroomTvDisplayLease : IDisposable
{
    private TVController controller;
    private readonly int token;

    internal BoxroomTvDisplayLease(TVController controller, int token, string ownerId)
    {
        this.controller = controller;
        this.token = token;
        OwnerId = ownerId;
    }

    public string OwnerId { get; }
    public bool IsActive => controller != null && controller.IsExternalClaimActive(token);
    public Bounds ScreenBounds => controller != null ? controller.ClaimedScreenBounds : default;
    public Vector3 ScreenCenter => ScreenBounds.center;
    public Vector3 ScreenForward => controller != null ? controller.ClaimedScreenForward : Vector3.forward;

    /// <summary>Updates the texture displayed by the current owner.</summary>
    public bool SetTexture(Texture texture) => controller != null && controller.SetExternalTexture(token, texture);

    public void Dispose()
    {
        TVController claimed = controller;
        controller = null;
        claimed?.ReleaseExternalClaim(token);
    }
}
