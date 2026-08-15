using Boxroom_TV.Videos;
using HarmonyLib;
using MelonLoader;
using SteamShelf;

namespace Boxroom_TV.Patches
{
    [HarmonyPatch(typeof(SteamLibrarySystem), "Configure")]
    internal static class SteamLibraryConfigurePatch
    {
        static void Postfix()
        {
            VideoLibrarySystem.ScanAndRegister();
            VideoLibrarySystem.RefreshOrphanCache();
            MelonLogger.Msg("[Boxroom-TV] Configure() fired — re-scanning videos."); 
        }
    }
}

