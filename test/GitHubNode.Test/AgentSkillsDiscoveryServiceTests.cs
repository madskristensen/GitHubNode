using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class AgentSkillsDiscoveryServiceTests
{
    [TestMethod]
    public void TryCreateIndexUri_AcceptsDomain()
    {
        bool result = AgentSkillsDiscoveryService.TryCreateIndexUri("example.com", out Uri indexUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/.well-known/agent-skills/index.json", indexUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateIndexUri_AcceptsWellKnownIndexUrl()
    {
        bool result = AgentSkillsDiscoveryService.TryCreateIndexUri("https://example.com/.well-known/agent-skills/index.json", out Uri indexUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://example.com/.well-known/agent-skills/index.json", indexUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateIndexUri_AcceptsLegacyWellKnownIndexUrl()
    {
        bool result = AgentSkillsDiscoveryService.TryCreateIndexUri("https://docs.stripe.com/.well-known/skills/index.json", out Uri indexUri);

        Assert.IsTrue(result);
        Assert.AreEqual("https://docs.stripe.com/.well-known/skills/index.json", indexUri.AbsoluteUri);
    }

    [TestMethod]
    public void TryCreateIndexUri_RejectsRepositoryUrl()
    {
        bool result = AgentSkillsDiscoveryService.TryCreateIndexUri("https://github.com/owner/repo", out Uri indexUri);

        Assert.IsFalse(result);
        Assert.IsNull(indexUri);
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
        Assert.AreEqual(expected, AgentSkillsDiscoveryService.IsValidSkillName(name));
    }

    [TestMethod]
    public void IsValidDigest_RequiresSha256LowercaseHexDigest()
    {
        Assert.IsTrue(AgentSkillsDiscoveryService.IsValidDigest("sha256:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
        Assert.IsFalse(AgentSkillsDiscoveryService.IsValidDigest("sha256:0123456789ABCDEF0123456789abcdef0123456789abcdef0123456789abcdef"));
        Assert.IsFalse(AgentSkillsDiscoveryService.IsValidDigest("md5:0123456789abcdef0123456789abcdef0123456789abcdef0123456789abcdef"));
    }

    [TestMethod]
    public void ResolveArtifactUri_ResolvesRelativeUrlsAgainstIndexDirectory()
    {
        var indexUri = new Uri("https://example.com/.well-known/agent-skills/index.json");

        Uri relativeUri = AgentSkillsDiscoveryService.ResolveArtifactUri(indexUri, "code-review/SKILL.md");
        Uri pathAbsoluteUri = AgentSkillsDiscoveryService.ResolveArtifactUri(indexUri, "/skills/code-review/SKILL.md");
        Uri absoluteUri = AgentSkillsDiscoveryService.ResolveArtifactUri(indexUri, "https://cdn.example.com/code-review/SKILL.md");

        Assert.AreEqual("https://example.com/.well-known/agent-skills/code-review/SKILL.md", relativeUri.AbsoluteUri);
        Assert.AreEqual("https://example.com/skills/code-review/SKILL.md", pathAbsoluteUri.AbsoluteUri);
        Assert.AreEqual("https://cdn.example.com/code-review/SKILL.md", absoluteUri.AbsoluteUri);
    }
}
