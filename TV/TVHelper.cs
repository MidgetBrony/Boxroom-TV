using SteamShelf.Placeables;
using System;
using System.Collections.Generic;
using System.Linq;
using MelonLoader;

namespace Boxroom_TV.TV
{
    internal static class TVHelper
    {
        private static readonly string[] SupportedPrefixes = new string[]
        {
            "Placeable_televisions_",
            "Placeable_electronics_monitor",
            "Placeable_Modern_Tech_TV",
        };
        public static int GetMaterialIndexOverride(PlacementTag tag, int defaultIndex)
        {
            string id = tag.PlaceableData?.ID ?? "";

            if (id.StartsWith("Placeable_Modern_Tech_TV"))
                return 0;

            return defaultIndex;
        }
        public static bool IsSupportedDisplay(GameImagePainter painter, out PlacementTag tag)
        {
            tag = painter.GetComponent<PlacementTag>();

            if (tag == null)
                return false;

            string id = tag.PlaceableData?.ID ?? "";

            bool supported = SupportedPrefixes.Any(prefix => id.StartsWith(prefix));

            MelonLogger.Msg($"[Boxroom-TV] Placeable ID seen: '{id}' -> supported: {supported}");

            return supported;
        }
    }
}