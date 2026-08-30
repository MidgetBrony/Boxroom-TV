using BR_MediaAPI;
using MelonLoader;
using SteamShelf.Media;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Xml.Linq;
using UnityEngine;

namespace Boxroom_TV.Videos;

public sealed class MovieItem : MediaItemBase
{
    internal MovieItem(string id) : base((eMediaType)Core.MovieMediaTypeId, id) { }
    public string Title { get; internal set; } = string.Empty;
    public string FolderPath { get; internal set; } = string.Empty;
    public IReadOnlyList<string> VideoPaths { get; internal set; } = Array.Empty<string>();
    public override string DisplayName => Title;
    internal void SetCover(byte[] bytes) => CoverArtBytes = bytes;
}

public sealed class MovieLibrary : IMediaLibrary
{
    private static readonly string[] VideoExtensions = { ".mp4", ".m4v", ".mov", ".webm", ".avi", ".mkv" };
    private static readonly string[] CoverNames = { "cover.jpg", "cover.jpeg", "cover.png", "folder.jpg", "poster.jpg", "poster.png" };
    private static readonly Regex SeasonFolderPattern = new(@"^(?:season|series|s)\s*[._-]*\s*(\d{1,4})\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private static readonly Regex EpisodePattern = new(@"(?<![a-z0-9])s(?<season>\d{1,4})[ ._-]*e(?<episode>\d{1,4})", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant);
    private readonly Dictionary<string, MovieItem> items = new(StringComparer.OrdinalIgnoreCase);

    public static MovieLibrary Instance { get; } = new();
    public static string DefaultLibraryRoot => Path.Combine(Application.persistentDataPath, "Boxroom-TV", "Movies");
    public eMediaType HandledType => (eMediaType)Core.MovieMediaTypeId;
    public event Action<IMediaItem> OnItemReady;
    public event Action<IReadOnlyList<IMediaItem>> OnLibraryReady;

    public IReadOnlyList<IMediaItem> GetKnownItems() => items.Values.OrderBy(item => item.Title, StringComparer.OrdinalIgnoreCase).Cast<IMediaItem>().ToArray();

    public IMediaItem GetItemSync(MediaRef mediaRef)
    {
        if (mediaRef.Type != HandledType) return null;
        items.TryGetValue(mediaRef.Id, out MovieItem item);
        return item;
    }

    public Task<IMediaItem> GetItemAsync(MediaRef mediaRef) => Task.FromResult(GetItemSync(mediaRef));

    public void Reload()
    {
        items.Clear();
        string root = MediaApi.GetLibraryFolder(HandledType);
        if (string.IsNullOrWhiteSpace(root)) root = DefaultLibraryRoot;
        Directory.CreateDirectory(root);

        foreach (string folder in Directory.GetDirectories(root, "*", SearchOption.AllDirectories).Prepend(root))
            TryLoadFolder(root, folder);

        OnLibraryReady?.Invoke(GetKnownItems());
        MelonLogger.Msg($"[Boxroom-TV] Loaded {items.Count} movie case(s) from '{root}'.");
    }

    public string GetStatus()
    {
        int files = items.Values.Sum(item => item.VideoPaths.Count);
        return $"{items.Count} cases, {files} video files";
    }

    private void TryLoadFolder(string root, string folder)
    {
        try
        {
            List<string> paths = ResolveVideos(folder).ToList();
            if (paths.Count == 0) return;

            string relative = Path.GetRelativePath(root, folder).Replace('\\', '/');
            if (relative == ".") relative = "root";

            Match seasonFolder = SeasonFolderPattern.Match(Path.GetFileName(folder));
            if (seasonFolder.Success && int.TryParse(seasonFolder.Groups[1].Value, out int folderSeason))
            {
                string showFolder = Directory.GetParent(folder)?.FullName ?? folder;
                AddSeason(relative, showFolder, folder, folderSeason, paths);
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
                    AddSeason(relative + "/S" + group.Key.ToString("00"), folder, folder, group.Key, group.Select(value => value.Path).ToList());
                return;
            }

            NfoInfo movie = ReadMovieNfo(folder, paths);
            AddItem(movie?.StableId ?? StableId(relative), movie?.Title ?? FolderTitle(folder), folder, paths, FindFolderCover(folder));
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"[Boxroom-TV] Skipping movie folder '{folder}': {exception.Message}");
        }
    }

    private void AddSeason(string relative, string showFolder, string mediaFolder, int season, List<string> paths)
    {
        NfoInfo show = ReadNfoInfo(Path.Combine(showFolder, "tvshow.nfo"), "title", "showtitle");
        string showName = show?.Title ?? FolderTitle(showFolder);
        string title = $"{showName}: S{season:00}";
        string cover = FindSeasonCover(showFolder, mediaFolder, season);
        string id = show?.StableId == null ? StableId(relative) : show.StableId + "-s" + season.ToString("00");
        AddItem(id, title, mediaFolder, paths, cover);
    }

    private void AddItem(string id, string title, string folder, List<string> paths, string cover)
    {
        if (string.IsNullOrWhiteSpace(title)) title = "Movies";
        var item = new MovieItem(id) { Title = title, FolderPath = folder, VideoPaths = paths };
        if (cover != null) item.SetCover(File.ReadAllBytes(cover));
        items[id] = item;
        OnItemReady?.Invoke(item);
    }

    private sealed class NfoInfo
    {
        internal string Title { get; set; }
        internal string StableId { get; set; }
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
            string title = null;
            foreach (string name in titleElements)
            {
                string value = document.Descendants().FirstOrDefault(element => string.Equals(element.Name.LocalName, name, StringComparison.OrdinalIgnoreCase))?.Value;
                if (!string.IsNullOrWhiteSpace(value)) { title = value.Trim(); break; }
            }
            XElement uniqueId = document.Descendants()
                .Where(element => string.Equals(element.Name.LocalName, "uniqueid", StringComparison.OrdinalIgnoreCase) && !string.IsNullOrWhiteSpace(element.Value))
                .OrderByDescending(element => string.Equals((string)element.Attribute("default"), "true", StringComparison.OrdinalIgnoreCase))
                .FirstOrDefault();
            string stableId = null;
            if (uniqueId != null)
            {
                string provider = ((string)uniqueId.Attribute("type") ?? "local").Trim().ToLowerInvariant();
                stableId = "nfo-" + SafeId(provider) + "-" + SafeId(uniqueId.Value.Trim());
            }
            return string.IsNullOrWhiteSpace(title) && stableId == null ? null : new NfoInfo { Title = title, StableId = stableId };
        }
        catch (Exception exception)
        {
            MelonLogger.Warning($"[Boxroom-TV] Could not read NFO '{path}': {exception.Message}");
        }
        return null;
    }

    private static string SafeId(string value)
    {
        string normalized = Regex.Replace(value.ToLowerInvariant(), @"[^a-z0-9._-]+", "-").Trim('-');
        return string.IsNullOrWhiteSpace(normalized) ? "unknown" : normalized;
    }

    private static string FolderTitle(string folder)
    {
        string title = Path.GetFileName(folder.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar));
        return string.IsNullOrWhiteSpace(title) ? "Movies" : title;
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
            ?? FindFolderCover(mediaFolder)
            ?? FindFolderCover(showFolder);
    }

    private static IEnumerable<string> ResolveVideos(string folder)
    {
        return Directory.GetFiles(folder).Where(path => VideoExtensions.Contains(Path.GetExtension(path), StringComparer.OrdinalIgnoreCase)).OrderBy(NaturalSortKey);
    }

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

    private static string StableId(string relativePath)
    {
        using SHA256 hash = SHA256.Create();
        byte[] bytes = hash.ComputeHash(Encoding.UTF8.GetBytes(relativePath.ToLowerInvariant()));
        return "movie-" + BitConverter.ToString(bytes, 0, 12).Replace("-", "").ToLowerInvariant();
    }
}
