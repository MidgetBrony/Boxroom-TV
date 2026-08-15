using Boxroom_TV.Videos;
using HarmonyLib;
using MelonLoader;
using SteamShelf;
using SteamShelf.Placeables;
using System.Collections.Generic;
using System.Linq;

namespace Boxroom_TV.Patches
{
    [HarmonyPatch(typeof(UnplacedGamesBox), "KnownGamesSorted")]
    internal static class MovieBoxFilterPatch
    {
        static void Postfix(UnplacedGamesBox __instance, ref List<SteamGameData> __result)
        {
            bool isMovieBox = __instance.GetComponent<MovieBoxMarker>() != null;
            int rawRangoCount = __result.Count(g => g.Name == "Rango");
            MelonLogger.Msg($"[Boxroom-TV] KnownGamesSorted called. isMovieBox={isMovieBox}, raw Rango count={rawRangoCount}, KnownVideoGameAppIds has {VideoLibrarySystem.KnownVideoGameAppIds.Count} entries.");

            __result = isMovieBox
                ? __result.Where(g => VideoLibrarySystem.KnownVideoGameAppIds.Contains(g.AppId)).ToList()
                : __result.Where(g => !VideoLibrarySystem.KnownVideoGameAppIds.Contains(g.AppId) && !VideoLibrarySystem.IsOrphanedVideoDuplicate(g)).ToList();

            int filteredRangoCount = __result.Count(g => g.Name == "Rango");
            MelonLogger.Msg($"[Boxroom-TV] After filter: Rango count={filteredRangoCount}");
        }
    }
}