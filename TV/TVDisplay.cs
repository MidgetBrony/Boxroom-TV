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
    private static readonly string[] SupportedPrefixes =
    {
        "Placeable_televisions_",
        "Placeable_electronics_monitor",
        "Placeable_Modern_Tech_TV"
    };

    private static readonly FieldInfo RendererField = AccessTools.Field(typeof(GameImagePainter), "targetRenderer");
    private static readonly FieldInfo MaterialIndexField = AccessTools.Field(typeof(GameImagePainter), "materialIndex");
    private static readonly FieldInfo OverrideMaterialField = AccessTools.Field(typeof(GameImagePainter), "overrideMaterial");

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
        if (painter == null || tag == null) return false;
        string id = tag.PlaceableData?.ID ?? string.Empty;
        if (!SupportedPrefixes.Any(prefix => id.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))) return false;

        Renderer renderer = RendererField?.GetValue(painter) as Renderer;
        if (renderer == null) return false;
        int index = MaterialIndexField?.GetValue(painter) is int value ? value : 0;
        if (id.StartsWith("Placeable_Modern_Tech_TV", StringComparison.OrdinalIgnoreCase)) index = 0;
        if (index < 0 || index >= renderer.materials.Length) return false;
        display = new TVDisplay(renderer, index, OverrideMaterialField?.GetValue(painter) as Material);
        return true;
    }
}
