using System.Text.Json;

namespace AiToolsMonitor.Projects;

public static class RecentProjectProvider
{
    public static IReadOnlyList<string> GetRecentProjects(string? projectsRoot = null)
    {
        try
        {
            string root = projectsRoot ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".claude",
                "projects");
            if (!Directory.Exists(root))
                return [];

            return Directory.EnumerateDirectories(root)
                .OrderByDescending(Directory.GetLastWriteTimeUtc)
                .Take(5)
                .Select(GetProjectPath)
                .ToList();
        }
        catch
        {
            return [];
        }
    }

    private static string GetProjectPath(string projectFolder)
    {
        try
        {
            string? transcript = Directory.EnumerateFiles(
                    projectFolder,
                    "*.jsonl",
                    SearchOption.TopDirectoryOnly)
                .OrderByDescending(File.GetLastWriteTimeUtc)
                .FirstOrDefault();

            if (transcript is not null)
            {
                foreach (string line in File.ReadLines(transcript))
                {
                    try
                    {
                        using var document = JsonDocument.Parse(line);
                        if (document.RootElement.TryGetProperty("cwd", out var cwdElement))
                        {
                            string? cwd = cwdElement.GetString();
                            if (!string.IsNullOrWhiteSpace(cwd))
                                return cwd;
                        }
                    }
                    catch (JsonException)
                    {
                        // Ignore malformed transcript lines.
                    }
                }
            }
        }
        catch
        {
            // Fall through to the lossy folder-name decoder.
        }

        return DecodeFolderName(Path.GetFileName(projectFolder));
    }

    private static string DecodeFolderName(string folderName)
    {
        if (folderName.Length >= 3 &&
            char.IsLetter(folderName[0]) &&
            folderName[1] == '-' &&
            folderName[2] == '-')
        {
            return $"{char.ToUpperInvariant(folderName[0])}:\\" +
                folderName[3..].Replace('-', '\\');
        }

        return folderName.Replace('-', '\\');
    }
}
