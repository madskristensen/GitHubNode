using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class WellKnownDiscoveryServiceTests
{
    [TestMethod]
    public void TryCreateOriginUri_AcceptsDomain()
    {
        bool result = WellKnownDiscoveryService.TryCreateOriginUri("example.com", out Uri originUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/", originUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateOriginUri_AcceptsWellKnownIndexUrl()
    {
        bool result = WellKnownDiscoveryService.TryCreateOriginUri("https://example.com/.well-known/agent-skills/index.json", out Uri originUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/", originUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateOriginUri_AcceptsLegacyWellKnownIndexUrl()
    {
        bool result = WellKnownDiscoveryService.TryCreateOriginUri("https://docs.stripe.com/.well-known/skills/index.json", out Uri originUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://docs.stripe.com/", originUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateOriginUri_AcceptsAnyWellKnownPath()
    {
        bool result = WellKnownDiscoveryService.TryCreateOriginUri("https://example.com/.well-known/mcp/server-card.json", out Uri originUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/", originUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateOriginUri_RejectsRepositoryUrl()
    {
        bool result = WellKnownDiscoveryService.TryCreateOriginUri("https://github.com/owner/repo", out Uri originUri);

        Assert.IsFalse(result);
        Assert.IsNull(originUri);
    }

    [DataTestMethod]
    [DataRow("code-review", true)]
    [DataRow("a", true)]
    [DataRow("skill-123", true)]
    [DataRow("-skill", false)]
    [DataRow("skill-", false)]
    [DataRow("skill--name", false)]
    [DataRow("Skill", false)]
    [DataRow("skill_name", false)]
    public void IsValidSkillName_ValidatesRfcNameRules(string name, bool expected)
    {
        Assert.AreEqual(expected, WellKnownDiscoveryService.IsValidSkillName(name));
    }

    [TestMethod]
    public void IsValidDigest_RequiresSha256LowercaseHexDigest()
    {
        Assert.IsTrue(WellKnownDiscoveryService.IsValidDigest("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
        Assert.IsFalse(WellKnownDiscoveryService.IsValidDigest("sha256:0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef"));
        Assert.IsFalse(WellKnownDiscoveryService.IsValidDigest("md5:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
    }

    [TestMethod]
    public void ResolveArtifactUri_ResolvesRelativeUrlsAgainstIndexDirectory()
    {
        var indexUri = new Uri("https://example.com/.well-known/agent-skills/index.json");

        Uri relativeUri = WellKnownDiscoveryService.ResolveArtifactUri(indexUri, "code-review/SKILL.md");
        Uri pathAbsoluteUri = WellKnownDiscoveryService.ResolveArtifactUri(indexUri, "/skills/code-review/SKILL.md");
        Uri absoluteUri = WellKnownDiscoveryService.ResolveArtifactUri(indexUri, "https://cdn.example.com/code-review/SKILL.md");

        Assert.AreEqual("https://example.com/.well-known/agent-skills/code-review/SKILL.md", relativeUri.AbsoluteUri);
        Assert.AreEqual("https://example.com/skills/code-review/SKILL.md", pathAbsoluteUri.AbsoluteUri);
        Assert.AreEqual("https://cdn.example.com/code-review/SKILL.md", absoluteUri.AbsoluteUri);
    }
}
