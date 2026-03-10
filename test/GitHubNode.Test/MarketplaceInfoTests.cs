using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceInfoTests
{
    [TestMethod]
    public void GitHubUrl_ReturnsCorrectUrl()
    {
        var marketplace = new MarketplaceInfo
        {
            Owner = "myowner",
            RepoName = "myrepo"
        };

        Assert.AreEqual("https://github.com/myowner/myrepo", marketplace.GitHubUrl);
    }

    [TestMethod]
    public void CloneUrl_ReturnsCorrectUrl()
    {
        var marketplace = new MarketplaceInfo
        {
            Owner = "myowner",
            RepoName = "myrepo"
        };

        Assert.AreEqual("https://github.com/myowner/myrepo.git", marketplace.CloneUrl);
    }

    [TestMethod]
    public void HasAssetType_ReturnsTrueWhenPluginHasAsset()
    {
        var marketplace = new MarketplaceInfo
        {
            Plugins = new List<MarketplacePlugin>
            {
                new MarketplacePlugin
                {
                    Name = "plugin1",
                    Assets = new List<PluginAsset>
                    {
                        new PluginAsset { Type = AssetType.Agent, Name = "agent1" }
                    }
                }
            }
        };

        Assert.IsTrue(marketplace.HasAssetType(AssetType.Agent));
    }

    [TestMethod]
    public void HasAssetType_ReturnsFalseWhenNoPluginHasAsset()
    {
        var marketplace = new MarketplaceInfo
        {
            Plugins = new List<MarketplacePlugin>
            {
                new MarketplacePlugin
                {
                    Name = "plugin1",
                    Assets = new List<PluginAsset>
                    {
                        new PluginAsset { Type = AssetType.Agent, Name = "agent1" }
                    }
                }
            }
        };

        Assert.IsFalse(marketplace.HasAssetType(AssetType.Skill));
    }

    [TestMethod]
    public void GetAllAssets_ReturnsAssetsFromAllPlugins()
    {
        var marketplace = new MarketplaceInfo
        {
            Plugins = new List<MarketplacePlugin>
            {
                new MarketplacePlugin
                {
                    Name = "plugin1",
                    Assets = new List<PluginAsset>
                    {
                        new PluginAsset { Type = AssetType.Agent, Name = "agent1" },
                        new PluginAsset { Type = AssetType.Agent, Name = "agent2" }
                    }
                },
                new MarketplacePlugin
                {
                    Name = "plugin2",
                    Assets = new List<PluginAsset>
                    {
                        new PluginAsset { Type = AssetType.Agent, Name = "agent3" },
                        new PluginAsset { Type = AssetType.Skill, Name = "skill1" }
                    }
                }
            }
        };

        var agents = marketplace.GetAllAssets(AssetType.Agent).ToList();

        Assert.AreEqual(3, agents.Count);
    }

    [TestMethod]
    public void DefaultBranch_IsMain()
    {
        var marketplace = new MarketplaceInfo();

        Assert.AreEqual("main", marketplace.Branch);
    }
}
