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
