using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using SteamShelf;
using SteamShelf.Media;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;

namespace Boxroom_TV.Videos
{
    public static class VideoLibrarySystem
    {
        public static readonly HashSet<int> KnownVideoGameAppIds = new HashSet<int>();
        public static readonly Dictionary<int, List<string>> AppIdToVideoPaths = new Dictionary<int, List<string>>();

        private static Dictionary<string, int> folderToAppId;
        private static int nextAppId = -1000000; // dedicated ID range, far from the game's own custom-game counter

        private static string MapFilePath =>
            Path.Combine(MelonEnvironment.ModsDirectory, "Boxroom-TV", "VideoAppIds.json");

        private static FieldInfo registryField;
        public static readonly HashSet<int> OrphanedVideoAppIds = new HashSet<int>();

        public static void RefreshOrphanCache()
        {
            EnsureMapLoaded();
            OrphanedVideoAppIds.Clear();

            foreach (SteamGameData g in SteamLibrarySystem.GetKnownGames())
            {
                if (g.AppType != "custom") continue;
                if (string.IsNullOrEmpty(g.Name)) continue;
                if (!folderToAppId.TryGetValue(g.Name, out int canonicalId)) continue;
                if (g.AppId != canonicalId)
                    OrphanedVideoAppIds.Add(g.AppId);
            }
        }

        public static void ScanAndRegister()
        {
            EnsureMapLoaded();

            string videoRoot = Path.Combine(MelonEnvironment.ModsDirectory, "Boxroom-TV", "VideoLibrary");
            Directory.CreateDirectory(videoRoot);

            var registry = GetRegistry();
            if (registry == null)
            {
                MelonLogger.Error("[Boxroom-TV] Could not access SteamLibrarySystem's internal registry via reflection.");
                return;
            }

            foreach (string folder in Directory.GetDirectories(videoRoot))
            {
                string key = Path.GetFileName(folder);
                string[] mp4s = Directory.GetFiles(folder, "*.mp4").OrderBy(f => f).ToArray();
                if (mp4s.Length == 0) continue;

                int appId = GetOrAssignAppId(key);

                if (!registry.ContainsKey(appId))
                {
                    SteamGameData data = new SteamGameData(appId);
                    SetInternal(data, "Name", key);
                    SetInternal(data, "AppType", "custom");
                    SetInternal(data, "MetadataLoaded", true);

                    string coverPath = Directory.GetFiles(folder, "cover.*").FirstOrDefault();
                    if (coverPath != null)
                    {
                        SetInternal(data, "BoxArtBytes", File.ReadAllBytes(coverPath));
                        SetInternal(data, "BoxArtLoaded", true);
                        SteamLibrarySystem.ApplyUserBoxArt(data);
                    }

                    registry[appId] = data;
                    MelonLogger.Msg($"[Boxroom-TV] Registered video case: {key} (AppId {appId}, {mp4s.Length} file(s))");
                }

                KnownVideoGameAppIds.Add(appId);
                AppIdToVideoPaths[appId] = mp4s.ToList();
            }
        }

        private static int GetOrAssignAppId(string folderKey)
        {
            if (folderToAppId.TryGetValue(folderKey, out int existing))
                return existing;

            int assigned = nextAppId--;
            folderToAppId[folderKey] = assigned;
            SaveMap();
            return assigned;
        }

        private static void EnsureMapLoaded()
        {
            if (folderToAppId != null) return;

            if (File.Exists(MapFilePath))
            {
                try
                {
                    folderToAppId = JsonConvert.DeserializeObject<Dictionary<string, int>>(File.ReadAllText(MapFilePath))
                        ?? new Dictionary<string, int>();

                    if (folderToAppId.Count > 0)
                        nextAppId = folderToAppId.Values.Min() - 1;
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Boxroom-TV] Failed to read VideoAppIds.json: {ex.Message}");
                    folderToAppId = new Dictionary<string, int>();
                }
            }
            else
            {
                folderToAppId = new Dictionary<string, int>();
            }
        }

        private static void SaveMap()
        {
            try
            {
                File.WriteAllText(MapFilePath, JsonConvert.SerializeObject(folderToAppId, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Boxroom-TV] Failed to save VideoAppIds.json: {ex.Message}");
            }
        }

        private static ConcurrentDictionary<int, SteamGameData> GetRegistry()
        {
            if (registryField == null)
            {
                registryField = typeof(SteamLibrarySystem).GetField(
                    "knownGamesRegistry",
                    BindingFlags.NonPublic | BindingFlags.Static);
            }
            return registryField?.GetValue(null) as ConcurrentDictionary<int, SteamGameData>;
        }
        public static bool IsOrphanedVideoDuplicate(SteamGameData g) => OrphanedVideoAppIds.Contains(g.AppId);


        private static void SetInternal(SteamGameData data, string propertyName, object value)
        {
            PropertyInfo prop = typeof(SteamGameData).GetProperty(propertyName);
            MethodInfo setter = prop?.GetSetMethod(true);
            setter?.Invoke(data, new object[] { value });
        }
    }
}