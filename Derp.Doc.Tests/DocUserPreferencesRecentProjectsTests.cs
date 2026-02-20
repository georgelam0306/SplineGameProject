using Derp.Doc.Preferences;

namespace Derp.Doc.Tests;

public sealed class DocUserPreferencesRecentProjectsTests
{
    [Fact]
    public void AddRecentProjectPath_Normalizes_Dedupes_And_Moves_ToFront()
    {
        var preferences = new DocUserPreferences();
        string baseDirectory = Path.Combine(Path.GetTempPath(), "derpdoc_recent_projects_tests");
        string alphaPath = Path.Combine(baseDirectory, "Alpha");
        string betaPath = Path.Combine(baseDirectory, "Beta");
        string alphaPathVariant = Path.Combine(baseDirectory, "Alpha", "..", "Alpha");

        Assert.True(preferences.AddRecentProjectPath(alphaPath));
        Assert.True(preferences.AddRecentProjectPath(betaPath));
        Assert.True(preferences.AddRecentProjectPath(alphaPathVariant));

        Assert.Equal(2, preferences.RecentProjectPaths.Count);
        Assert.Equal(Path.GetFullPath(alphaPath), preferences.RecentProjectPaths[0]);
        Assert.Equal(Path.GetFullPath(betaPath), preferences.RecentProjectPaths[1]);
    }

    [Fact]
    public void AddRecentProjectPath_Trims_To_MaxRecentProjectCount()
    {
        var preferences = new DocUserPreferences();
        string baseDirectory = Path.Combine(Path.GetTempPath(), "derpdoc_recent_projects_tests_trim");
        int totalProjects = DocUserPreferences.MaxRecentProjectCount + 5;
        string expectedNewestPath = "";

        for (int projectIndex = 0; projectIndex < totalProjects; projectIndex++)
        {
            string projectPath = Path.Combine(baseDirectory, "Project_" + projectIndex.ToString());
            Assert.True(preferences.AddRecentProjectPath(projectPath));
            expectedNewestPath = Path.GetFullPath(projectPath);
        }

        Assert.Equal(DocUserPreferences.MaxRecentProjectCount, preferences.RecentProjectPaths.Count);
        Assert.Equal(expectedNewestPath, preferences.RecentProjectPaths[0]);
        Assert.DoesNotContain(
            preferences.RecentProjectPaths,
            path => path.EndsWith("Project_0", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Remove_And_Clear_RecentProjects_Work()
    {
        var preferences = new DocUserPreferences();
        string baseDirectory = Path.Combine(Path.GetTempPath(), "derpdoc_recent_projects_tests_remove");
        string alphaPath = Path.Combine(baseDirectory, "Alpha");
        string betaPath = Path.Combine(baseDirectory, "Beta");

        Assert.True(preferences.AddRecentProjectPath(alphaPath));
        Assert.True(preferences.AddRecentProjectPath(betaPath));
        Assert.Equal(2, preferences.RecentProjectPaths.Count);

        string alphaPathVariant = Path.Combine(baseDirectory, "Alpha", "..", "Alpha");
        Assert.True(preferences.RemoveRecentProjectPath(alphaPathVariant));
        Assert.Single(preferences.RecentProjectPaths);
        Assert.Equal(Path.GetFullPath(betaPath), preferences.RecentProjectPaths[0]);

        Assert.True(preferences.ClearRecentProjectPaths());
        Assert.Empty(preferences.RecentProjectPaths);
        Assert.False(preferences.ClearRecentProjectPaths());
    }
}
