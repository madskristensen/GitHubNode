using System.IO;
using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class McpConfigServiceTests
{
    [TestMethod]
    public void GetDefaultContent_ContainsServersProperty()
    {
        string content = McpConfigService.GetDefaultContent();

        StringAssert.Contains(content, "\"servers\"");
    }

    [TestMethod]
    public void GetAllLocations_ReturnsGlobalLocation_WhenSolutionDirectoryIsNull()
    {
        var locations = McpConfigService.GetAllLocations(null);

        Assert.AreEqual(1, locations.Count);
    }

    [TestMethod]
    public void GetAllLocations_ReturnsFiveLocations_WhenSolutionDirectoryProvided()
    {
        var solutionDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(solutionDirectory);

            var locations = McpConfigService.GetAllLocations(solutionDirectory);

            Assert.AreEqual(5, locations.Count);
        }
        finally
        {
            if (Directory.Exists(solutionDirectory))
            {
                Directory.Delete(solutionDirectory, recursive: true);
            }
        }
    }

    [TestMethod]
    public void ParseServerNames_ReturnsEmptyList_WhenFileDoesNotExist()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.json");

        var names = McpConfigService.ParseServerNames(filePath);

        CollectionAssert.AreEqual(new string[0], names);
    }

    [TestMethod]
    public void ParseServerInfo_ReturnsEmptyDictionary_WhenFileDoesNotExist()
    {
        var filePath = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.json");

        var info = McpConfigService.ParseServerInfo(filePath);

        Assert.AreEqual(0, info.Count);
    }

    [TestMethod]
    public void CreateConfigFile_CreatesFileWithDefaultContent()
    {
        var folder = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var filePath = Path.Combine(folder, ".mcp.json");

        try
        {
            bool created = McpConfigService.CreateConfigFile(filePath);

            Assert.IsTrue(created);
            Assert.IsTrue(File.Exists(filePath));
        }
        finally
        {
            if (Directory.Exists(folder))
            {
                Directory.Delete(folder, recursive: true);
            }
        }
    }

    [TestMethod]
    public void GetExistingLocations_IncludesSolutionRoot_WhenConfigFileExists()
    {
        var solutionDirectory = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var expectedPath = Path.Combine(solutionDirectory, ".mcp.json");

        try
        {
            Directory.CreateDirectory(solutionDirectory);
            File.WriteAllText(expectedPath, McpConfigService.GetDefaultContent());

            var existing = McpConfigService.GetExistingLocations(solutionDirectory);

            Assert.IsTrue(existing.Exists(location => string.Equals(location.FilePath, expectedPath, StringComparison.OrdinalIgnoreCase)));
        }
        finally
        {
            if (Directory.Exists(solutionDirectory))
            {
                Directory.Delete(solutionDirectory, recursive: true);
            }
        }
    }
}
