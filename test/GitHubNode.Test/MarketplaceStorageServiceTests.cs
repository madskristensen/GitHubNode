using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceStorageServiceTests
{
    [TestMethod]
    public void BuiltInMarketplaces_ContainsExpectedMarketplaces()
    {
        var builtIn = MarketplaceStorageService.BuiltInMarketplaces;

        Assert.IsTrue(builtIn.Count >= 3);
        Assert.IsTrue(builtIn.Any(m => m.Owner == "github" && m.Repo == "awesome-copilot"));
        Assert.IsTrue(builtIn.Any(m => m.Owner == "dotnet" && m.Repo == "skills"));
    }

    [TestMethod]
    public void GetMarketplaceId_ReturnsOwnerSlashRepo()
    {
        string id = MarketplaceStorageService.GetMarketplaceId("myowner", "myrepo");

        Assert.AreEqual("myowner/myrepo", id);
    }

    [TestMethod]
    public void GetMarketplaceDirectory_ReturnsPathWithSanitizedName()
    {
        string dir = MarketplaceStorageService.GetMarketplaceDirectory("owner", "repo");

        Assert.IsTrue(dir.Contains("owner_repo"));
        Assert.IsTrue(dir.Contains("Marketplaces"));
    }

    [TestMethod]
    public void GetMarketplaceId_ReturnsHostAwareId_ForCustomHost()
    {
        string id = MarketplaceStorageService.GetMarketplaceId("myowner", "myrepo", "https://contoso.ghe.com/myowner/myrepo");

        Assert.AreEqual("contoso.ghe.com/myowner/myrepo", id);
    }

    [TestMethod]
    public void GetMarketplaceDirectory_ReturnsHostAwarePath_ForCustomHost()
    {
        string dir = MarketplaceStorageService.GetMarketplaceDirectory("owner", "repo", "https://contoso.ghe.com/owner/repo");

        StringAssert.Contains(dir, "contoso.ghe.com_owner_repo");
    }

    [TestMethod]
    public void GetAgentSkillsDiscoveryIconPath_ReturnsFaviconPath()
    {
        string iconPath = MarketplaceStorageService.GetAgentSkillsDiscoveryIconPath(
            new Uri("https://docs.stripe.com/.well-known/skills/index.json"),
            ".ico");

        StringAssert.Contains(iconPath, "AgentSkills");
        StringAssert.Contains(iconPath, "_favicon.ico");
    }

    [TestMethod]
    public void GetLinkedRepositoryDirectory_ReturnsHostAwarePath_ForCustomHost()
    {
        string dir = MarketplaceStorageService.GetLinkedRepositoryDirectory(
            "parent",
            "marketplace",
            "owner",
            "repo",
            parentRepositoryUrl: "https://contoso.ghe.com/parent/marketplace",
            linkedRepositoryUrl: "git@bitbucket.example:team/repo.git");

        StringAssert.Contains(dir, "bitbucket.example_owner_repo");
    }

    [TestMethod]
    public void IsBuiltIn_ReturnsTrueForBuiltInMarketplace()
    {
        bool result = MarketplaceStorageService.IsBuiltIn("github", "awesome-copilot");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsBuiltIn_ReturnsFalseForUserMarketplace()
    {
        bool result = MarketplaceStorageService.IsBuiltIn("someuser", "somerepo");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void IsBuiltIn_IsCaseInsensitive()
    {
        bool result = MarketplaceStorageService.IsBuiltIn("GitHub", "Awesome-Copilot");

        Assert.IsTrue(result);
    }

    [TestMethod]
    public void IsBuiltIn_ReturnsFalseForCustomHost()
    {
        bool result = MarketplaceStorageService.IsBuiltIn("github", "awesome-copilot", "https://contoso.ghe.com/github/awesome-copilot");

        Assert.IsFalse(result);
    }

    [TestMethod]
    public void LoadConfig_ReturnsNewConfigWhenFileDoesNotExist()
    {
        // This should not throw and should return a default config
        var config = MarketplaceStorageService.LoadConfig();

        Assert.IsNotNull(config);
        Assert.IsNotNull(config.Marketplaces);
        Assert.IsTrue(config.EnableBuiltInMarketplaces);
        Assert.AreEqual(168, config.UpdateIntervalHours);
    }
}
