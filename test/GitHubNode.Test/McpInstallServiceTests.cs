using System.IO;
using System.Text.Json;
using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class McpInstallServiceTests
{
    private string _tempDir;

    [TestInitialize]
    public void Setup()
    {
        _tempDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(_tempDir);
    }

    [TestCleanup]
    public void Cleanup()
    {
        if (Directory.Exists(_tempDir))
        {
            Directory.Delete(_tempDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // Guard / error path tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void InstallFromMarketplace_ReturnsFail_WhenSourceFileDoesNotExist()
    {
        string missing = Path.Combine(_tempDir, "missing.mcp.json");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(missing, null, _tempDir, target);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "not found");
    }

    [TestMethod]
    public void InstallFromMarketplace_ReturnsFail_WhenTargetPathIsEmpty()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"srv\": {} } }");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, null, _tempDir, "");

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "No target configuration path");
    }

    [TestMethod]
    public void InstallFromMarketplace_ReturnsFail_WhenSourceHasNoServers()
    {
        string source = WriteSourceFile("{ \"otherKey\": {} }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, null, _tempDir, target);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "No MCP servers found");
    }

    [TestMethod]
    public void InstallFromMarketplace_ReturnsFail_WhenRequestedServerNotFound()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"real-server\": {} } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "no-such-server", _tempDir, target);

        Assert.IsFalse(result.Success);
        StringAssert.Contains(result.Message, "not found in the source");
    }

    // -------------------------------------------------------------------------
    // Parsing format tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void InstallFromMarketplace_ParsesMcpServersFormat()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"my-server\": { \"command\": \"node\" } } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "my-server", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.Contains(result.InstalledServers, "my-server");
    }

    [TestMethod]
    public void InstallFromMarketplace_ParsesServersFormat()
    {
        string source = WriteSourceFile("{ \"servers\": { \"vs-server\": { \"command\": \"dotnet\" } } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "vs-server", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.Contains(result.InstalledServers, "vs-server");
    }

    // -------------------------------------------------------------------------
    // Merge / install tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void InstallFromMarketplace_CreatesNewConfigFile_WhenTargetDoesNotExist()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"new-server\": { \"command\": \"node\" } } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "new-server", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(File.Exists(target));
        string written = File.ReadAllText(target);
        StringAssert.Contains(written, "new-server");
    }

    [TestMethod]
    public void InstallFromMarketplace_MergesServerIntoExistingConfig()
    {
        string existingContent = "{\n  \"servers\": {\n    \"existing\": { \"command\": \"cmd\" }\n  }\n}";
        string target = WriteTargetFile(existingContent);
        string source = WriteSourceFile("{ \"mcpServers\": { \"added-server\": { \"command\": \"git\" } } }");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "added-server", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        string written = File.ReadAllText(target);
        StringAssert.Contains(written, "existing");
        StringAssert.Contains(written, "added-server");
    }

    [TestMethod]
    public void InstallFromMarketplace_SkipsServer_WhenAlreadyPresent()
    {
        string existingContent = "{\n  \"servers\": {\n    \"dup-server\": { \"command\": \"cmd\" }\n  }\n}";
        string target = WriteTargetFile(existingContent);
        string source = WriteSourceFile("{ \"mcpServers\": { \"dup-server\": { \"command\": \"node\" } } }");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "dup-server", _tempDir, target);

        Assert.IsFalse(result.Success);
        CollectionAssert.Contains(result.SkippedServers, "dup-server");
        Assert.AreEqual(0, result.InstalledServers.Count);
    }

    [TestMethod]
    public void InstallFromMarketplace_InstallsAllServers_WhenServerNameIsNull()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"alpha\": {}, \"beta\": {} } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, null, _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        Assert.AreEqual(2, result.InstalledServers.Count);
        CollectionAssert.Contains(result.InstalledServers, "alpha");
        CollectionAssert.Contains(result.InstalledServers, "beta");
    }

    [TestMethod]
    public void InstallFromMarketplace_TargetContainsValidJson_AfterInstall()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"json-server\": { \"command\": \"npx\", \"args\": [\"start\"] } } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "json-server", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        string written = File.ReadAllText(target);
        // Verify the result is parseable JSON
        using JsonDocument doc = JsonDocument.Parse(written);
        Assert.IsTrue(doc.RootElement.TryGetProperty("servers", out _));
    }

    [TestMethod]
    public void InstallFromMarketplace_CreatesTargetDirectory_WhenNotPresent()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"s\": {} } }");
        string subDir = Path.Combine(_tempDir, "nested", ".github");
        string target = Path.Combine(subDir, "mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "s", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        Assert.IsTrue(File.Exists(target));
    }

    // -------------------------------------------------------------------------
    // GetTargetConfigPath tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void GetTargetConfigPath_ReturnsSolutionRootMcpJson_WhenNoConfigExists()
    {
        string expected = Path.Combine(_tempDir, ".mcp.json");

        string actual = McpInstallService.GetTargetConfigPath(_tempDir);

        Assert.AreEqual(expected, actual, StringComparer.OrdinalIgnoreCase);
    }

    [TestMethod]
    public void GetTargetConfigPath_ReturnsNull_WhenSolutionDirectoryIsNull()
    {
        string actual = McpInstallService.GetTargetConfigPath(null);

        Assert.IsNull(actual);
    }

    [TestMethod]
    public void GetTargetConfigPath_ReturnsExistingConfig_WhenSolutionRootConfigExists()
    {
        string existingConfig = Path.Combine(_tempDir, ".mcp.json");
        File.WriteAllText(existingConfig, McpConfigService.GetDefaultContent());

        string actual = McpInstallService.GetTargetConfigPath(_tempDir);

        Assert.AreEqual(existingConfig, actual, StringComparer.OrdinalIgnoreCase);
    }

    // -------------------------------------------------------------------------
    // Success message tests
    // -------------------------------------------------------------------------

    [TestMethod]
    public void InstallFromMarketplace_SuccessMessageContainsServerName()
    {
        string source = WriteSourceFile("{ \"mcpServers\": { \"named-server\": {} } }");
        string target = Path.Combine(_tempDir, ".mcp.json");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, "named-server", _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        StringAssert.Contains(result.Message, "named-server");
    }

    [TestMethod]
    public void InstallFromMarketplace_SuccessMessageMentionsSkippedServers_WhenSomeSkipped()
    {
        string existingContent = "{\n  \"servers\": {\n    \"old\": {}\n  }\n}";
        string target = WriteTargetFile(existingContent);
        string source = WriteSourceFile("{ \"mcpServers\": { \"old\": {}, \"new-one\": {} } }");

        McpInstallResult result = McpInstallService.InstallFromMarketplace(source, null, _tempDir, target);

        Assert.IsTrue(result.Success, result.Message);
        CollectionAssert.Contains(result.SkippedServers, "old");
        StringAssert.Contains(result.Message, "Skipped");
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    private string WriteSourceFile(string content)
    {
        string path = Path.Combine(_tempDir, $"source-{Path.GetRandomFileName()}.mcp.json");
        File.WriteAllText(path, content);
        return path;
    }

    private string WriteTargetFile(string content)
    {
        string path = Path.Combine(_tempDir, $"target-{Path.GetRandomFileName()}.mcp.json");
        File.WriteAllText(path, content);
        return path;
    }
}
