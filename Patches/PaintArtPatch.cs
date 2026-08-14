using System;
using HarmonyLib;
using SteamShelf;
using System.Reflection;
using UnityEngine;

namespace Boxroom_TV.Patches
{
    [HarmonyPatch(typeof(GameImagePainter), nameof(GameImagePainter.PaintArt), new Type[] { typeof(SteamGameData) })]
    internal static class PaintArtPatch
    {
        static void Postfix(GameImagePainter __instance, SteamGameData game)
        {
            TV.TVPlayer.TryPlay(__instance, game);
        }
    }

    internal static class PainterReflection
    {
        private static readonly FieldInfo RendererField =
            typeof(GameImagePainter).GetField("targetRenderer",
                BindingFlags.Instance | BindingFlags.NonPublic);

        private static readonly FieldInfo OverrideMaterialField =
    typeof(GameImagePainter).GetField("overrideMaterial",
        BindingFlags.Instance | BindingFlags.NonPublic);

        public static Material GetOverrideMaterial(GameImagePainter painter)
            => (Material)OverrideMaterialField.GetValue(painter);

        private static readonly FieldInfo MaterialIndexField =
            typeof(GameImagePainter).GetField("materialIndex",
                BindingFlags.Instance | BindingFlags.NonPublic);

        public static Renderer GetRenderer(GameImagePainter painter)
            => (Renderer)RendererField.GetValue(painter);

        public static int GetMaterialIndex(GameImagePainter painter)
            => (int)MaterialIndexField.GetValue(painter);
    }
}