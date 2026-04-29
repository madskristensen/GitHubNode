using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class McpServerDiscoveryServiceTests
{
    [TestMethod]
    public void TryCreateDiscoveryUri_AcceptsDomain()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("example.com", out Uri discoveryUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/.well-known/mcp/server-card.json", discoveryUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_AcceptsWellKnownServerCardUrl()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("https://example.com/.well-known/mcp/server-card.json", out Uri discoveryUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/.well-known/mcp/server-card.json", discoveryUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_AcceptsLegacyWellKnownMcpUrl()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("https://example.com/.well-known/mcp.json", out Uri discoveryUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/.well-known/mcp.json", discoveryUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_AcceptsAbsoluteUri()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("https://example.com", out Uri discoveryUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/.well-known/mcp/server-card.json", discoveryUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_RejectsRepositoryUrl()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("https://github.com/owner/repo", out Uri discoveryUri);

        Assert.IsFalse(result);
        Assert.IsNull(discoveryUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_AcceptsHttpUrl()
    {
        // HTTP URLs are accepted at discovery URI creation time
        // but will be rejected later during actual discovery if not localhost
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("http://example.com", out Uri discoveryUri);

        Assert.IsTrue(result);
        Assert.IsNotNull(discoveryUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_RejectsEmptyString()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri("", out Uri discoveryUri);

        Assert.IsFalse(result);
        Assert.IsNull(discoveryUri);
    }

    [TestMethod]
    public void TryCreateDiscoveryUri_RejectsNull()
    {
        bool result = McpServerDiscoveryService.TryCreateDiscoveryUri(null, out Uri discoveryUri);

        Assert.IsFalse(result);
        Assert.IsNull(discoveryUri);
    }

    [DataTestMethod]
    [DataRow("my-server", true)]
    [DataRow("a", true)]
    [DataRow("server-123", true)]
    [DataRow("0server", true)]
    [DataRow("-server", false)]
    [DataRow("server-", false)]
    [DataRow("server--name", false)]
    [DataRow("Server", false)]
    [DataRow("server_name", false)]
    [DataRow("", false)]
    public void IsValidServerName_ValidatesRfcNameRules(string name, bool expected)
    {
        Assert.AreEqual(expected, McpServerDiscoveryService.IsValidServerName(name));
    }

    [TestMethod]
    public void GetSourceId_CreatesConsistentId()
    {
        var uri = new Uri("https://example.com/.well-known/mcp/server-card.json");
        string id1 = McpServerDiscoveryService.GetSourceId(uri);
        string id2 = McpServerDiscoveryService.GetSourceId(uri);

        Assert.AreEqual(id1, id2);
        Assert.IsTrue(id1.StartsWith("mcp-servers:"));
    }

    [TestMethod]
    public void GetDisplayName_DefaultsToMcpServers()
    {
        string displayName = McpServerDiscoveryService.GetDisplayName(null);
        Assert.AreEqual("MCP Servers", displayName);
    }

    [TestMethod]
    public void GetDisplayName_IncludesHostFromUri()
    {
        var uri = new Uri("https://example.com/.well-known/mcp/server-card.json");
        string displayName = McpServerDiscoveryService.GetDisplayName(uri);

        Assert.IsTrue(displayName.Contains("example.com"));
    }

    [TestMethod]
    public void ResolveArtifactUri_ResolvesRelativeUrls()
    {
        var baseUri = new Uri("https://example.com/.well-known/mcp/server-card.json");

        Uri relativeUri = McpServerDiscoveryService.ResolveArtifactUri(baseUri, "servers/my-server.json");
        Uri pathAbsoluteUri = McpServerDiscoveryService.ResolveArtifactUri(baseUri, "/mcp-servers/my-server.json");
        Uri absoluteUri = McpServerDiscoveryService.ResolveArtifactUri(baseUri, "https://cdn.example.com/servers/my-server.json");

        Assert.AreEqual("https://example.com/.well-known/mcp/servers/my-server.json", relativeUri?.AbsoluteUri);
        Assert.AreEqual("https://example.com/mcp-servers/my-server.json", pathAbsoluteUri?.AbsoluteUri);
        Assert.AreEqual("https://cdn.example.com/servers/my-server.json", absoluteUri?.AbsoluteUri);
    }

    [TestMethod]
    public void ResolveArtifactUri_RejectsInvalidUrls()
    {
        var baseUri = new Uri("https://example.com/.well-known/mcp/server-card.json");

        Uri result = McpServerDiscoveryService.ResolveArtifactUri(baseUri, "ftp://invalid.com/file");
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ResolveArtifactUri_RejectsEmptyUrl()
    {
        var baseUri = new Uri("https://example.com/.well-known/mcp/server-card.json");

        Uri result = McpServerDiscoveryService.ResolveArtifactUri(baseUri, "");
        Assert.IsNull(result);

        result = McpServerDiscoveryService.ResolveArtifactUri(baseUri, null);
        Assert.IsNull(result);
    }

    [TestMethod]
    public void ResolveArtifactUri_RejectsNullBaseUri()
    {
        Uri result = McpServerDiscoveryService.ResolveArtifactUri(null, "servers/my-server.json");
        Assert.IsNull(result);
    }
}
