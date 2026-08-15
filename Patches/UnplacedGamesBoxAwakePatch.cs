using HarmonyLib;
using SteamShelf.Placeables;
using Boxroom_TV.Videos;

namespace Boxroom_TV.Patches
{
    [HarmonyPatch(typeof(UnplacedGamesBox), "Awake")]
    internal static class UnplacedGamesBoxAwakePatch
    {
        static void Prefix()
        {
            VideoLibrarySystem.ScanAndRegister();
            VideoLibrarySystem.RefreshOrphanCache();
        }
    }
}
