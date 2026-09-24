using HarmonyLib;
using SteamShelf;
using SteamShelf.Placeables;
using System;
using System.Linq;
using System.Reflection;
using UnityEngine;

namespace Boxroom_TV.TV;

internal readonly struct TVDisplay
{
    private static readonly string[] LegacySupportedPrefixes =
    {
        "Placeable_televisions_",
        "Placeable_electronics_monitor",
        "Placeable_Modern_Tech_TV",
        "Placeable_Modern_Tech_Screen_",
        "Placeable_Office_Electronics_Laptop"
    };

    private static readonly FieldInfo RendererField = AccessTools.Field(typeof(GameImagePainter), "targetRenderer");
    private static readonly FieldInfo MaterialIndexField = AccessTools.Field(typeof(GameImagePainter), "materialIndex");
    private static readonly FieldInfo OverrideMaterialField = AccessTools.Field(typeof(GameImagePainter), "overrideMaterial");
    private static readonly FieldInfo NativeScreenRendererField = AccessTools.Field(typeof(Interactable_TV), "screenRenderer");
    private static readonly FieldInfo NativeScreenMaterialIndexField = AccessTools.Field(typeof(Interactable_TV), "screenMaterialIndex");

    private TVDisplay(Renderer renderer, int materialIndex, Material overrideMaterial)
    {
        Renderer = renderer;
        MaterialIndex = materialIndex;
        OverrideMaterial = overrideMaterial;
    }

    internal Renderer Renderer { get; }
    internal int MaterialIndex { get; }
    internal Material OverrideMaterial { get; }

    internal static bool TryGet(GameImagePainter painter, PlacementTag tag, out TVDisplay display)
    {
        display = default;
        if (painter == null) return false;

        // Native BOXROOM video-capable displays declare their own screen renderer
        // and material slot. Prefer that capability contract so newly added TVs,
        // arcade cabinets, and similar displays work without an ID allow-list.
        Interactable_TV nativeTv = painter.GetComponent<Interactable_TV>();
        if (nativeTv != null)
        {
            Renderer nativeRenderer = NativeScreenRendererField?.GetValue(nativeTv) as Renderer
                                      ?? nativeTv.GetComponent<Renderer>();
            int nativeIndex = NativeScreenMaterialIndexField?.GetValue(nativeTv) is int nativeValue ? nativeValue : 0;
            if (IsValid(nativeRenderer, nativeIndex))
            {
                display = new TVDisplay(nativeRenderer, nativeIndex, null);
                return true;
            }
        }

        // Keep support for older displays that Boxroom-TV handled before the
        // native Interactable_TV capability was introduced.
        if (tag == null) return false;
        string id = tag.PlaceableData?.ID ?? string.Empty;
        if (!LegacySupportedPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return false;

        Renderer renderer = RendererField?.GetValue(painter) as Renderer;
        int index = MaterialIndexField?.GetValue(painter) is int legacyValue ? legacyValue : 0;
        if (id.StartsWith("Placeable_Modern_Tech_TV", StringComparison.OrdinalIgnoreCase)) index = 0;
        if (!IsValid(renderer, index)) return false;
        display = new TVDisplay(renderer, index, OverrideMaterialField?.GetValue(painter) as Material);
        return true;
    }

    private static bool IsValid(Renderer renderer, int materialIndex) =>
        renderer != null && materialIndex >= 0 && materialIndex < renderer.sharedMaterials.Length;
}
