using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceAsProviderTests
{
    [TestMethod]
    public void Id_ReturnsMarketplaceId()
    {
        var marketplace = new MarketplaceInfo
        {
            Id = "owner/repo"
        };
        var provider = new MarketplaceAsProvider(marketplace);

        Assert.AreEqual("owner/repo", provider.Id);
    }

    [TestMethod]
    public void DisplayName_ReturnsMarketplaceDisplayName()
    {
        var marketplace = new MarketplaceInfo
        {
            DisplayName = "My Marketplace"
        };
        var provider = new MarketplaceAsProvider(marketplace);

        Assert.AreEqual("My Marketplace", provider.DisplayName);
    }

    [TestMethod]
    public void DisplayName_FallsBackToId_WhenDisplayNameIsNull()
    {
        var marketplace = new MarketplaceInfo
        {
            Id = "owner/repo",
            DisplayName = null
        };
        var provider = new MarketplaceAsProvider(marketplace);

        Assert.AreEqual("owner/repo", provider.DisplayName);
    }

    [TestMethod]
    public void IsBuiltIn_ReflectsMarketplaceProperty()
    {
        var builtIn = new MarketplaceInfo { IsBuiltIn = true };
        var userAdded = new MarketplaceInfo { IsBuiltIn = false };

        Assert.IsTrue(new MarketplaceAsProvider(builtIn).IsBuiltIn);
        Assert.IsFalse(new MarketplaceAsProvider(userAdded).IsBuiltIn);
    }

    [TestMethod]
    public void HasError_ReturnsTrueWhenErrorMessageExists()
    {
        var marketplace = new MarketplaceInfo
        {
            ErrorMessage = "Something went wrong"
        };
        var provider = new MarketplaceAsProvider(marketplace);

        Assert.IsTrue(provider.HasError);
        Assert.AreEqual("Something went wrong", provider.ErrorMessage);
    }

    [TestMethod]
    public void HasError_ReturnsFalseWhenNoError()
    {
        var marketplace = new MarketplaceInfo
        {
            ErrorMessage = null
        };
        var provider = new MarketplaceAsProvider(marketplace);

        Assert.IsFalse(provider.HasError);
    }

    [TestMethod]
    public void ToString_ReturnsDisplayName()
    {
        var marketplace = new MarketplaceInfo
        {
            DisplayName = "Test Provider"
        };
        var provider = new MarketplaceAsProvider(marketplace);

        Assert.AreEqual("Test Provider", provider.ToString());
    }

    [TestMethod]
    public void NullMarketplace_ReturnsDefaults()
    {
        var provider = new MarketplaceAsProvider(null);

        Assert.AreEqual("unknown", provider.Id);
        Assert.AreEqual("Unknown", provider.DisplayName);
        Assert.IsFalse(provider.IsBuiltIn);
        Assert.IsFalse(provider.HasError);
    }
}
