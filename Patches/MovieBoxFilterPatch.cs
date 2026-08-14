using HarmonyLib;
using SteamShelf;
using SteamShelf.Placeables;
using System.Collections.Generic;
using System.Linq;
using Boxroom_TV.Videos;

namespace Boxroom_TV.Patches
{
    [HarmonyPatch(typeof(UnplacedGamesBox), "KnownGamesSorted")]
    internal static class MovieBoxFilterPatch
    {
        static void Postfix(UnplacedGamesBox __instance, ref List<SteamGameData> __result)
        {
            bool isMovieBox = __instance.GetComponent<MovieBoxMarker>() != null;

            __result = isMovieBox
                ? __result.Where(g => VideoLibrarySystem.KnownVideoGameAppIds.Contains(g.AppId)).ToList()
                : __result.Where(g => !VideoLibrarySystem.KnownVideoGameAppIds.Contains(g.AppId)).ToList();
        }
    }
}