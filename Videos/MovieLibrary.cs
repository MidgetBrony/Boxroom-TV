using HarmonyLib;
using MelonLoader;
using SteamShelf.Media.Videos;
using SteamShelf.Save;
using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;

namespace Boxroom_TV.Videos;

/// <summary>
/// Enhances BOXROOM's native Video library in-place. Native VideoData objects,
/// cases, shelves, source container, art editing, and saves remain authoritative.
/// </summary>
internal static class MovieLibrary
{
    private static readonly string[] VideoExtensions =
    {
        ".mp4", ".m4v", ".mov", ".webm", ".avi", ".mkv", ".mpg", ".mpeg",
        ".ts", ".m2ts", ".mts", ".vob", ".wmv", ".flv", ".ogv", ".3gp"
    };

    private static readonly string[] CoverNames =
    {
        "cover.jpg", "cover.jpeg", "cover.png", "folder.jpg", "folder.png",
        "poster.jpg", "poster.png", "movie.jpg", "movie.png"
    };

    private static readonly Regex SeasonFolderPattern = new(
        @"^(?:season|series|s)\s*[._-]*\s*(\d{1,4})\b",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EpisodePattern = new(
        @"(?<![a-z0-9])s(?<season>\d{1,4})[ ._-]*e(?<episode>\d{1,4})",
        RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);

    private static readonly FieldInfo RegistryField = AccessTools.Field(typeof(VideoLibrarySystem), "knownVideosRegistry");
    private static readonly FieldInfo VideoReadyField = AccessTools.Field(typeof(VideoLibrarySystem), "OnVideoReady");
    private static readonly FieldInfo LibraryReadyField = AccessTools.Field(typeof(VideoLibrarySystem), "OnLibraryReady");
    private static readonly PropertyInfo FolderPathProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.FolderPath));
    private static readonly PropertyInfo DisplayNameProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.DisplayName));
    private static readonly PropertyInfo VideoPathsProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.VideoPaths));
    private static readonly PropertyInfo CoverBytesProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.CoverArtBytes));
    private static readonly PropertyInfo CoverLoadedProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.CoverArtLoaded));
    private static readonly PropertyInfo DiskBytesProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.DiskArtBytes));
    private static readonly PropertyInfo DiskLoadedProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.DiskArtLoaded));
    private static readonly PropertyInfo ExtraBytesProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.ExtraArtBytes));
    private static readonly PropertyInfo MetadataLoadedProperty = AccessTools.Property(typeof(VideoData), nameof(VideoData.MetadataLoaded));

    private static readonly Dictionary<string, string> LegacyIdToNativeId = new(StringComparer.OrdinalIgnoreCase);
    private static int caseCount;
    private static int fileCount;
    private static bool scanCompleted;

    internal static string DefaultLibraryRoot => Path.Combine(Application.persistentDataPath, "Boxroom-TV", "Movies");
    internal static string Status => $"{caseCount} native Video cases, {fileCount} video files";
    internal static bool IsScanComplete => scanCompleted;
    internal static bool TryResolveLegacyId(string legacyId, out string nativeId) =>
        LegacyIdToNativeId.TryGetValue(legacyId ?? string.Empty, out nativeId);

    internal static Task ScanAsync()
    {
        try { Scan(); }
        catch (Exception exception)
        {
            scanCompleted = true;
            MelonLogger.Error("[Boxroom-TV] Native Video library scan failed: " + exception);
            RaiseLibraryReady(Array.Empty<VideoData>());
        }
        return Task.CompletedTask;
    }

    private static void Scan()
    {
        ConcurrentDictionary<string, VideoData> registry = GetRegistry();
        var states = registry.ToDictionary(pair => pair.Key,
            pair => (pair.Value.IsSpawned, pair.Value.IsInHand), StringComparer.OrdinalIgnoreCase);
        registry.Clear();
        LegacyIdToNativeId.Clear();
        caseCount = 0;
        fileCount = 0;
        scanCompleted = false;

        string root = GetLibraryRoot();
        if (string.IsNullOrWhiteSpace(root) || !Directory.Exists(root))
        {
            MelonLogger.Warning("[Boxroom-TV] No native Video Library Location is configured.");
            scanCompleted = true;
            RaiseLibraryReady(Array.Empty<VideoData>());
            LegacyMovieMigration.ApplyCurrentRoom();
            return;
        }

        foreach (string folder in EnumerateFolders(root))
        {
            try { LoadFolder(root, folder, registry, states); }
            catch (Exception exception)
            {
                MelonLogger.Warning($"[Boxroom-TV] Skipping video folder '{folder}': {exception.Message}");
            }
        }

        List<VideoData> videos = registry.Values
            .OrderBy(video => video.DisplayName, StringComparer.OrdinalIgnoreCase).ToList();
        caseCount = videos.Count;
        fileCount = videos.Sum(video => video.VideoPaths.Count);
        foreach (VideoData video in videos) RaiseVideoReady(video);
        RaiseLibraryReady(videos);
        scanCompleted = true;
        LegacyMovieMigration.ApplyCurrentRoom();
        MelonLogger.Msg($"[Boxroom-TV] Loaded {caseCount} native Video case(s), {fileCount} file(s), recursively from '{root}'.");
    }

    private static void LoadFolder(
        string root,
        string folder,
        ConcurrentDictionary<string, VideoData> registry,
        IReadOnlyDictionary<string, (bool IsSpawned, bool IsInHand)> states)
    {
        List<string> paths = ResolveVideos(folder).ToList();
        if (paths.Count == 0) return;

        string relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
        if (relative == ".") relative = "root";
        Match seasonFolder = SeasonFolderPattern.Match(Path.GetFileName(folder));
        if (seasonFolder.Success && int.TryParse(seasonFolder.Groups[1].Value, out int folderSeason))
        {
            string showFolder = Directory.GetParent(folder)?.FullName ?? folder;
            AddSeason(relative, showFolder, folder, folderSeason, paths, registry, states, virtualId: false);
            return;
        }

        var episodeGroups = paths
            .Select(path => new { Path = path, Match = EpisodePattern.Match(Path.GetFileNameWithoutExtension(path)) })
            .Where(value => value.Match.Success && int.TryParse(value.Match.Groups["season"].Value, out _))
            .GroupBy(value => int.Parse(value.Match.Groups["season"].Value))
            .OrderBy(group => group.Key)
            .ToArray();

        if (episodeGroups.Length > 0 && episodeGroups.Sum(group => group.Count()) == paths.Count)
        {
            foreach (var group in episodeGroups)
            {
                AddSeason(relative + "/S" + group.Key.ToString("00"), folder, folder, group.Key,
                    group.Select(value => value.Path).ToList(), registry, states, virtualId: true);
            }
            return;
        }

        NfoInfo movie = ReadMovieNfo(folder, paths);
        string nativeId = VideoLibrarySystem.NormalizePath(folder);
        AddVideo(nativeId, movie?.Title ?? FolderTitle(folder), folder, paths, FindFolderCover(folder),
            movie?.StableId ?? StableId(relative), registry, states);
    }

    private static void AddSeason(
        string relative,
        string showFolder,
        string mediaFolder,
        int season,
        List<string> paths,
        ConcurrentDictionary<string, VideoData> registry,
        IReadOnlyDictionary<string, (bool IsSpawned, bool IsInHand)> states,
        bool virtualId)
    {
        NfoInfo show = ReadNfoInfo(Path.Combine(showFolder, "tvshow.nfo"), "title", "showtitle");
        string showName = show?.Title ?? FolderTitle(showFolder);
        string legacyId = show?.StableId == null
            ? StableId(relative)
            : show.StableId + "-s" + season.ToString("00");
        string nativeId = virtualId
            ? VideoLibrarySystem.NormalizePath(Path.Combine(mediaFolder, $".boxroom-tv-season-{season:0000}"))
            : VideoLibrarySystem.NormalizePath(mediaFolder);
        AddVideo(nativeId, $"{showName}: S{season:00}", mediaFolder, paths,
            FindSeasonCover(showFolder, mediaFolder, season), legacyId, registry, states);
    }

    private static void AddVideo(
        string nativeId,
        string title,
        string folder,
        List<string> paths,
        string coverPath,
        string legacyId,
        ConcurrentDictionary<string, VideoData> registry,
        IReadOnlyDictionary<string, (bool IsSpawned, bool IsInHand)> states)
    {
        var video = new VideoData(nativeId);
        Set(FolderPathProperty, video, folder);
        Set(DisplayNameProperty, video, string.IsNullOrWhiteSpace(title) ? FolderTitle(folder) : title);
        Set(VideoPathsProperty, video, paths);
        Set(MetadataLoadedProperty, video, true);

        byte[] coverBytes = ReadBytes(coverPath);
        Set(CoverBytesProperty, video, coverBytes);
        Set(CoverLoadedProperty, video, coverBytes?.Length > 0);
        byte[] diskBytes = ReadBytes(FindDiskArt(folder));
        Set(DiskBytesProperty, video, diskBytes);
        Set(DiskLoadedProperty, video, diskBytes?.Length > 0);
        Set(ExtraBytesProperty, video, LoadExtraArt(folder));

        if (states.TryGetValue(nativeId, out var state))
        {
            video.IsSpawned = state.IsSpawned;
            video.IsInHand = state.IsInHand;
        }
        registry[nativeId] = video;
        if (!string.IsNullOrWhiteSpace(legacyId)) LegacyIdToNativeId[legacyId] = nativeId;
    }

    private static string GetLibraryRoot()
    {
        if (!Singleton<SaveManager>.HasInstance()) return DefaultLibraryRoot;
        SettingsSaveData settings = Singleton<SaveManager>.Instance.Settings;
        string native = settings.GetValue(VideoLibrarySystem.SettingId, string.Empty);
        if (!string.IsNullOrWhiteSpace(native)) return native;

        string legacy = settings.GetValue("BRMediaAPI.LibraryFolder.com.midgetbrony.boxroom-tv.movies", string.Empty);
        if (!string.IsNullOrWhiteSpace(legacy))
        {
            settings.SetValue(VideoLibrarySystem.SettingId, legacy);
            settings.Save();
            MelonLogger.Msg("[Boxroom-TV] Imported the previous Movie Library Location into BOXROOM's native Video setting.");
            return legacy;
        }
        string fallback = DefaultLibraryRoot;
        Directory.CreateDirectory(fallback);
        settings.SetValue(VideoLibrarySystem.SettingId, fallback);
        settings.Save();
        return fallback;
    }

    private static IEnumerable<string> EnumerateFolders(string root)
    {
        yield return root;
        var pending = new Stack<string>();
        foreach (string child in SafeDirectories(root).Reverse()) pending.Push(child);
        while (pending.Count > 0)
        {
            string current = pending.Pop();
            yield return current;
            foreach (string child in SafeDirectories(current).Reverse()) pending.Push(child);
        }
    }

    private static IEnumerable<string> SafeDirectories(string folder)
    {
        try { return Directory.GetDirectories(folder).OrderBy(path => path, StringComparer.OrdinalIgnoreCase).ToArray(); }
        catch (Exception exception)
        {
            MelonLogger.Warning($"[Boxroom-TV] Cannot enter '{folder}': {exception.Message}");
            return Array.Empty<string>();
        }
    }

    private static IEnumerable<string> ResolveVideos(string folder) =>
        Directory.EnumerateFiles(folder)
            .Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase))
            .OrderBy(NaturalSortKey, StringComparer.OrdinalIgnoreCase);

    private static string NaturalSortKey(string path)
    {
        string name = Path.GetFileNameWithoutExtension(path);
        var builder = new StringBuilder();
        for (int i = 0; i < name.Length;)
        {
            if (!char.IsDigit(name[i])) { builder.Append(char.ToUpperInvariant(name[i++])); continue; }
            int start = i;
            while (i < name.Length && char.IsDigit(name[i])) i++;
            builder.Append(name.Substring(start, i - start).PadLeft(12, '0'));
        }
        return builder.ToString();
    }

    private static NfoInfo ReadMovieNfo(string folder, IReadOnlyList<string> videos)
    {
        string movieNfo = Path.Combine(folder, "movie.nfo");
        if (File.Exists(movieNfo)) return ReadNfoInfo(movieNfo, "title");
        foreach (string video in videos)
        {
            string alongside = Path.ChangeExtension(video, ".nfo");
            if (File.Exists(alongside)) return ReadNfoInfo(alongside, "title");
        }
        return null;
    }

    private static NfoInfo ReadNfoInfo(string path, params string[] titleElements)
    {
        if (!File.Exists(path)) return null;
        try
        {
            XDocument document = XDocument.Load(path);
            string title = titleElements.Select(name => document.Descendants()
                    .FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value)
                .FirstOrDefault(value => !string.IsNullOrWhiteSpace(value))?.Trim();
            XElement uniqueId = document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "uniqueid", StringComparison.OrdinalIgnoreCase)
                                  && !string.IsNullOrWhiteSpace(element.Value))
                .OrderByDescending(element => string.Equals((string)element.Attribute("default"), "true", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            string stableId = null;
            if (uniqueId != null)
            {
                string provider = ((string)uniqueId.Attribute("type") ?? "local").Trim().ToLowerInvariant();
                stableId = "nfo-" + SafeId(provider) + "-" + SafeId(uniqueId.Value.Trim());
            }
            return string.IsNullOrWhiteSpace(title) && stableId == null ? null : new NfoInfo(title, stableId);
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"[Boxroom-TV] Could not read NFO '{path}': {exception.Message}");
            return null;
        }
    }

    private static string FindFolderCover(string folder) =>
        CoverNames.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);

    private static string FindSeasonCover(string showFolder, string mediaFolder, int season)
    {
        string[] names =
        {
            $"season{season:00}-poster.jpg", $"season{season:00}-poster.png",
            $"season{season}-poster.jpg", $"season{season}-poster.png",
            $"season{season:00}.jpg", $"season{season:00}.png",
            $"season{season}.jpg", $"season{season}.png"
        };
        return names.Select(name => Path.Combine(showFolder, name)).FirstOrDefault(File.Exists)
               ?? FindFolderCover(mediaFolder) ?? FindFolderCover(showFolder);
    }

    private static string FindDiskArt(string folder)
    {
        string[] names = { "diskart.jpg", "diskart.png", "disc.jpg", "disc.png" };
        return names.Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
    }

    private static List<byte[]> LoadExtraArt(string folder)
    {
        var result = new List<byte[]>();
        for (int i = 0; i < 16; i++)
        {
            string path = new[] { $"extra_{i}.jpg", $"extra_{i}.png" }
                .Select(name => Path.Combine(folder, name)).FirstOrDefault(File.Exists);
            if (path == null) break;
            byte[] bytes = ReadBytes(path);
            if (bytes == null) break;
            result.Add(bytes);
        }
        return result;
    }

    private static byte[] ReadBytes(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
        try { return File.ReadAllBytes(path); }
        catch (Exception exception)
        {
            MelonLogger.Warning($"[Boxroom-TV] Could not read art '{path}': {exception.Message}");
            return null;
        }
    }

    private static string FolderTitle(string folder)
    {
        string title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(title) ? "Videos" : title;
    }

    private static string StableId(string relativePath)
    {
        using SHA256 hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return "movie-" + BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant();
    }

    private static string SafeId(string value)
    {
        string normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static ConcurrentDictionary<string, VideoData> GetRegistry() =>
        RegistryField?.GetValue(null) as ConcurrentDictionary<string, VideoData>
        ?? throw new MissingFieldException(typeof(VideoLibrarySystem).FullName, "knownVideosRegistry");
    private static void RaiseVideoReady(VideoData video) =>
        (VideoReadyField?.GetValue(null) as Action<VideoData>)?.Invoke(video);
    private static void RaiseLibraryReady(IReadOnlyList<VideoData> videos) =>
        (LibraryReadyField?.GetValue(null) as Action<IReadOnlyList<VideoData>>)?.Invoke(videos);
    private static void Set(PropertyInfo property, VideoData target, object value) => property?.SetValue(target, value);

    private sealed class NfoInfo
    {
        internal NfoInfo(string title, string stableId)
        {
            Title = title;
            StableId = stableId;
        }

        internal string Title { get; }
        internal string StableId { get; }
    }
}

[HarmonyPatch(typeof(VideoLibrarySystem), nameof(VideoLibrarySystem.ScanLibraryAsync))]
internal static class NativeVideoLibraryScanPatch
{
    private static bool Prefix(ref Task __result)
    {
        __result = MovieLibrary.ScanAsync();
        return false;
    }
}
