using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplacePluginTests
{
    [TestMethod]
    public void HasAssetType_ReturnsTrueWhenAssetExists()
    {
        var plugin = new MarketplacePlugin
        {
            Name = "test-plugin",
            Assets = new List<PluginAsset>
            {
                new PluginAsset { Type = AssetType.Agent, Name = "agent1" },
                new PluginAsset { Type = AssetType.Skill, Name = "skill1" }
            }
        };

        Assert.IsTrue(plugin.HasAssetType(AssetType.Agent));
        Assert.IsTrue(plugin.HasAssetType(AssetType.Skill));
    }

    [TestMethod]
    public void HasAssetType_ReturnsFalseWhenAssetDoesNotExist()
    {
        var plugin = new MarketplacePlugin
        {
            Name = "test-plugin",
            Assets = new List<PluginAsset>
            {
                new PluginAsset { Type = AssetType.Agent, Name = "agent1" }
            }
        };

        Assert.IsFalse(plugin.HasAssetType(AssetType.Skill));
        Assert.IsFalse(plugin.HasAssetType(AssetType.McpServer));
    }

    [TestMethod]
    public void GetAssets_ReturnsOnlyMatchingType()
    {
        var plugin = new MarketplacePlugin
        {
            Name = "test-plugin",
            Assets = new List<PluginAsset>
            {
                new PluginAsset { Type = AssetType.Agent, Name = "agent1" },
                new PluginAsset { Type = AssetType.Agent, Name = "agent2" },
                new PluginAsset { Type = AssetType.Skill, Name = "skill1" }
            }
        };

        var agents = plugin.GetAssets(AssetType.Agent).ToList();
        var skills = plugin.GetAssets(AssetType.Skill).ToList();

        Assert.AreEqual(2, agents.Count);
        Assert.AreEqual(1, skills.Count);
        Assert.AreEqual("skill1", skills[0].Name);
    }

    [TestMethod]
    public void GetAssets_ReturnsEmptyWhenNoMatch()
    {
        var plugin = new MarketplacePlugin
        {
            Name = "test-plugin",
            Assets = new List<PluginAsset>
            {
                new PluginAsset { Type = AssetType.Agent, Name = "agent1" }
            }
        };

        var prompts = plugin.GetAssets(AssetType.Prompt).ToList();

        Assert.AreEqual(0, prompts.Count);
    }
}
