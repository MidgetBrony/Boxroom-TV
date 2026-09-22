using HarmonyLib;
using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using SteamShelf.Save;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace Boxroom_TV.Videos;

/// <summary>
/// Converts Boxroom-TV 3 / BR-MediaAPI type 1200 references to BOXROOM's native
/// Video type 2. The operation is idempotent and never guesses an unmatched ID.
/// </summary>
internal static class LegacyMovieMigration
{
    private const int LegacyType = 1200;
    private const int NativeType = 2;
    private const string LegacyCaseId = "BR_MediaAPI_Case_1200";
    private const string LegacySourceBoxId = "BR_MediaAPI_UnplacedBox_1200";
    private const string NativeCaseId = "Placeable_VideoBoxProp";
    private const string NativeSourceBoxId = "Placeable_UnplacedVideoBox";

    internal static void ApplyCurrentRoom(string sourcePath = null)
    {
        if (!MovieLibrary.IsScanComplete) return;
        if (!Singleton<SaveManager>.HasInstance()) return;
        SaveManager manager = Singleton<SaveManager>.Instance;
        if (manager.RoomState?.PlacedObjects == null) return;

        var unmatched = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int references = 0;
        int looseCases = 0;
        int sourceBoxes = 0;

        foreach (PlaceableSaveState state in manager.RoomState.PlacedObjects)
        {
            if (state == null) continue;
            if (string.Equals(state.ID, LegacySourceBoxId, StringComparison.OrdinalIgnoreCase))
            {
                state.ID = NativeSourceBoxId;
                sourceBoxes++;
                continue;
            }

            bool isLooseLegacyCase = string.Equals(state.ID, LegacyCaseId, StringComparison.OrdinalIgnoreCase);
            if (string.IsNullOrWhiteSpace(state.CustomData)) continue;
            try
            {
                JToken root = JToken.Parse(state.CustomData);
                int changed = RewriteToken(root, unmatched);
                if (changed == 0) continue;
                references += changed;
                if (isLooseLegacyCase)
                {
                    state.ID = NativeCaseId;
                    looseCases++;
                    JObject top = root as JObject;
                    JProperty id = Find(top, "mediaId");
                    root = new JObject
                    {
                        ["mediaType"] = NativeType,
                        ["mediaId"] = id?.Value?.ToString() ?? string.Empty
                    };
                }
                state.CustomData = root.ToString(Formatting.None);
            }
            catch (Exception exception)
            {
                MelonLogger.Warning($"[Boxroom-TV] Could not inspect legacy movie data on '{state.ID}': {exception.Message}");
            }
        }

        if (references == 0 && sourceBoxes == 0 && unmatched.Count == 0) return;
        string backupPath = CreateBackup(manager, sourcePath);
        WriteReport(references, looseCases, sourceBoxes, unmatched, backupPath);
        if (references > 0 || sourceBoxes > 0)
        {
            MelonLogger.Msg($"[Boxroom-TV] Migrated {references} type-1200 reference(s), {looseCases} loose case(s), and {sourceBoxes} Movies Box(es) to native Video type 2.");
        }
        if (unmatched.Count > 0)
        {
            MelonLogger.Warning($"[Boxroom-TV] Left {unmatched.Count} unmatched type-1200 movie ID(s) unchanged. See UserData/Boxroom-TV/MigrationReport.json.");
        }
    }

    private static int RewriteToken(JToken token, ISet<string> unmatched)
    {
        int changed = 0;
        if (token is JObject obj)
        {
            JProperty type = Find(obj, "mediaType");
            JProperty id = Find(obj, "mediaId");
            if (type != null && id != null && TryInt(type.Value, out int typeValue) && typeValue == LegacyType)
            {
                string legacyId = id.Value?.ToString();
                if (MovieLibrary.TryResolveLegacyId(legacyId, out string nativeId))
                {
                    type.Value = NativeType;
                    id.Value = nativeId;
                    changed++;
                }
                else if (!string.IsNullOrWhiteSpace(legacyId))
                {
                    unmatched.Add(legacyId);
                }
            }
            foreach (JProperty property in obj.Properties().ToArray()) changed += RewriteToken(property.Value, unmatched);
        }
        else if (token is JArray array)
        {
            foreach (JToken child in array) changed += RewriteToken(child, unmatched);
        }
        return changed;
    }

    private static JProperty Find(JObject obj, string name) => obj?.Properties()
        .FirstOrDefault(property => string.Equals(property.Name, name, StringComparison.OrdinalIgnoreCase));

    private static bool TryInt(JToken token, out int value) =>
        int.TryParse(token?.ToString(), out value);

    private static string CreateBackup(SaveManager manager, string explicitPath)
    {
        string source = explicitPath;
        if (string.IsNullOrWhiteSpace(source) && manager.CurrentSlot >= 0)
        {
            string directory = manager.GetSlotDirectory(manager.CurrentSlot);
            if (!string.IsNullOrWhiteSpace(directory)) source = Path.Combine(directory, "RoomState.json");
        }
        if (string.IsNullOrWhiteSpace(source) || !File.Exists(source)) return null;

        string backup = source + ".boxroom-tv-type1200.bak";
        try
        {
            if (!File.Exists(backup)) File.Copy(source, backup, overwrite: false);
            return backup;
        }
        catch (Exception exception)
        {
            MelonLogger.Warning("[Boxroom-TV] Could not create the migration backup: " + exception.Message);
            return null;
        }
    }

    private static void WriteReport(int references, int looseCases, int sourceBoxes,
        IEnumerable<string> unmatched, string backupPath)
    {
        try
        {
            string directory = Path.Combine(MelonEnvironment.UserDataDirectory, "Boxroom-TV");
            Directory.CreateDirectory(directory);
            var report = new JObject
            {
                ["schemaVersion"] = 1,
                ["utc"] = DateTime.UtcNow.ToString("O"),
                ["legacyMediaType"] = LegacyType,
                ["nativeMediaType"] = NativeType,
                ["migratedReferences"] = references,
                ["migratedLooseCases"] = looseCases,
                ["migratedSourceBoxes"] = sourceBoxes,
                ["backupPath"] = backupPath,
                ["unmatchedLegacyIds"] = new JArray(unmatched.OrderBy(value => value, StringComparer.OrdinalIgnoreCase))
            };
            File.WriteAllText(Path.Combine(directory, "MigrationReport.json"), report.ToString(Formatting.Indented));
        }
        catch (Exception exception)
        {
            MelonLogger.Warning("[Boxroom-TV] Could not write MigrationReport.json: " + exception.Message);
        }
    }
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadAllData))]
internal static class LegacyMovieSaveLoadPatch
{
    private static void Postfix() => LegacyMovieMigration.ApplyCurrentRoom();
}

[HarmonyPatch(typeof(SaveManager), nameof(SaveManager.LoadRoomFromPath))]
internal static class LegacyMovieRoomLoadPatch
{
    private static void Postfix(string path) => LegacyMovieMigration.ApplyCurrentRoom(path);
}

[HarmonyPatch(typeof(RoomDataManager), "SpawnSavedPlaceableList")]
internal static class LegacyMovieBeforeSpawnPatch
{
    private static void Prefix()
    {
        if (!MovieLibrary.IsScanComplete) MovieLibrary.ScanAsync().GetAwaiter().GetResult();
        LegacyMovieMigration.ApplyCurrentRoom();
    }
}
