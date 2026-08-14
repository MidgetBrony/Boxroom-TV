using HarmonyLib;
using SteamShelf;
using Boxroom_TV.Videos;

namespace Boxroom_TV.Patches
{
    [HarmonyPatch(typeof(SteamLibrarySystem), "Configure")]
    internal static class SteamLibraryConfigurePatch
    {
        static void Postfix()
        {
            VideoLibrarySystem.ScanAndRegister();
        }
    }
}

