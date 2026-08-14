using Boxroom_TV.Patches;
using SteamShelf;
using SteamShelf.Placeables;
using UnityEngine;

namespace Boxroom_TV.TV
{
    internal static class TVPlayer
    {
        public static void TryPlay(GameImagePainter painter, SteamGameData game)
        {
            if (!TVHelper.IsSupportedDisplay(painter, out PlacementTag tag))
                return;

            Renderer renderer = PainterReflection.GetRenderer(painter);
            int index = TVHelper.GetMaterialIndexOverride(tag, PainterReflection.GetMaterialIndex(painter));

            TVController controller = painter.GetComponent<TVController>();
            if (controller == null)
                controller = painter.gameObject.AddComponent<TVController>();

            controller.Setup(renderer, index, PainterReflection.GetOverrideMaterial(painter));
        }
    }
}
