using MelonLoader;
using MelonLoader.Utils;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IO;
using UnityEngine;

namespace Boxroom_TV.Videos
{
    public static class TVStateStore
    {
        private static Dictionary<string, TVSaveEntry> states;
        private static string FilePath =>
            Path.Combine(MelonEnvironment.UserDataDirectory, "Boxroom-TV", "TVState.json");

        public static string CreateKey(Transform transform, string tagId)
        {
            Vector3 position = transform.position;
            return $"{tagId ?? "TV"}|{position.x:F3}|{position.y:F3}|{position.z:F3}";
        }

        private static void EnsureLoaded()
        {
            if (states != null) return;

            if (File.Exists(FilePath))
            {
                try
                {
                    states = JsonConvert.DeserializeObject<Dictionary<string, TVSaveEntry>>(File.ReadAllText(FilePath))
                        ?? new Dictionary<string, TVSaveEntry>();
                }
                catch (Exception ex)
                {
                    MelonLogger.Error($"[Boxroom-TV] Failed to read TVState.json: {ex.Message}");
                    states = new Dictionary<string, TVSaveEntry>();
                }
            }
            else
            {
                states = new Dictionary<string, TVSaveEntry>();
            }
        }

        public static TVSaveEntry Load(string key)
        {
            EnsureLoaded();
            return states.TryGetValue(key, out var entry) ? entry : null;
        }

        public static void Save(string key, TVSaveEntry entry)
        {
            EnsureLoaded();
            states[key] = entry;
            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(FilePath));
                File.WriteAllText(FilePath, JsonConvert.SerializeObject(states, Formatting.Indented));
            }
            catch (Exception ex)
            {
                MelonLogger.Error($"[Boxroom-TV] Failed to save TVState.json: {ex.Message}");
            }
        }
    }
}
