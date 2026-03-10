using GitHubNode.Services;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceTemplateAdapterTests
{
    [TestMethod]
    public void ToAssetType_ConvertsAgentCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToAssetType(TemplateType.Agent);

        Assert.AreEqual(AssetType.Agent, result);
    }

    [TestMethod]
    public void ToAssetType_ConvertsSkillCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToAssetType(TemplateType.Skill);

        Assert.AreEqual(AssetType.Skill, result);
    }

    [TestMethod]
    public void ToAssetType_ConvertsInstructionsCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToAssetType(TemplateType.Instructions);

        Assert.AreEqual(AssetType.Instructions, result);
    }

    [TestMethod]
    public void ToAssetType_ConvertsPromptCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToAssetType(TemplateType.Prompt);

        Assert.AreEqual(AssetType.Prompt, result);
    }

    [TestMethod]
    public void ToTemplateType_ConvertsAgentCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToTemplateType(AssetType.Agent);

        Assert.AreEqual(TemplateType.Agent, result);
    }

    [TestMethod]
    public void ToTemplateType_ConvertsSkillCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToTemplateType(AssetType.Skill);

        Assert.AreEqual(TemplateType.Skill, result);
    }

    [TestMethod]
    public void ToTemplateType_ConvertsInstructionsCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToTemplateType(AssetType.Instructions);

        Assert.AreEqual(TemplateType.Instructions, result);
    }

    [TestMethod]
    public void ToTemplateType_ConvertsPromptCorrectly()
    {
        var result = MarketplaceTemplateAdapter.ToTemplateType(AssetType.Prompt);

        Assert.AreEqual(TemplateType.Prompt, result);
    }

    [TestMethod]
    public void ToTemplateType_ReturnsNullForMcpServer()
    {
        var result = MarketplaceTemplateAdapter.ToTemplateType(AssetType.McpServer);

        Assert.IsNull(result);
    }
}
