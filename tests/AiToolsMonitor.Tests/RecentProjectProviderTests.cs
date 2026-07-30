using AiToolsMonitor.Projects;

namespace AiToolsMonitor.Tests;

public class RecentProjectProviderTests
{
    [Fact]
    public void GetRecentProjects_UsesLatestTranscriptCwdAndLimitsByFolderModificationTime()
    {
        using var temp = new TempDirectory();
        var baseTime = new DateTime(2026, 7, 30, 8, 0, 0, DateTimeKind.Utc);

        for (int index = 0; index < 6; index++)
        {
            string folder = Directory.CreateDirectory(
                Path.Combine(temp.Path, $"C--fallback-project-{index}")).FullName;
            string older = Path.Combine(folder, "older.jsonl");
            string latest = Path.Combine(folder, "latest.jsonl");
            File.WriteAllText(older, $$"""{"cwd":"C:\\older\\project-{{index}}"}""");
            File.WriteAllText(latest, $$"""{"type":"user","cwd":"C:\\real\\project-{{index}}"}""");
            File.SetLastWriteTimeUtc(older, baseTime.AddMinutes(index - 10));
            File.SetLastWriteTimeUtc(latest, baseTime.AddMinutes(index));
            Directory.SetLastWriteTimeUtc(folder, baseTime.AddMinutes(index));
        }

        IReadOnlyList<string> projects = RecentProjectProvider.GetRecentProjects(temp.Path);

        Assert.Equal(5, projects.Count);
        Assert.Equal(@"C:\real\project-5", projects[0]);
        Assert.Equal(@"C:\real\project-1", projects[4]);
    }

    [Fact]
    public void GetRecentProjects_DecodesFolderNameWhenLatestTranscriptHasNoCwd()
    {
        using var temp = new TempDirectory();
        string folder = Directory.CreateDirectory(
            Path.Combine(temp.Path, "D--work-fallback-project")).FullName;
        File.WriteAllText(Path.Combine(folder, "session.jsonl"), """{"type":"user"}""");

        IReadOnlyList<string> projects = RecentProjectProvider.GetRecentProjects(temp.Path);

        Assert.Equal([@"D:\work\fallback\project"], projects);
    }

    private sealed class TempDirectory : IDisposable
    {
        public TempDirectory()
        {
            Path = System.IO.Path.Combine(
                System.IO.Path.GetTempPath(),
                $"AiToolsMonitor.Tests-{Guid.NewGuid():N}");
            Directory.CreateDirectory(Path);
        }

        public string Path { get; }

        public void Dispose()
        {
            Directory.Delete(Path, recursive: true);
        }
    }
}
