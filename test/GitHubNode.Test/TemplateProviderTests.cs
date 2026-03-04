using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class TemplateProviderTests
{
    [TestMethod]
    public void GetRule_ReturnsMatchingRule_WhenTemplateTypeExists()
    {
        var provider = new TemplateProvider
        {
            SearchRules =
            [
                new TemplateSearchRule { TemplateType = TemplateType.Agent, RootPath = "agents" },
                new TemplateSearchRule { TemplateType = TemplateType.Prompt, RootPath = "prompts" }
            ]
        };

        TemplateSearchRule rule = provider.GetRule(TemplateType.Prompt);

        Assert.IsNotNull(rule);
        Assert.AreEqual("prompts", rule.RootPath);
    }

    [TestMethod]
    public void GetRule_ReturnsNull_WhenTemplateTypeDoesNotExist()
    {
        var provider = new TemplateProvider
        {
            SearchRules =
            [
                new TemplateSearchRule { TemplateType = TemplateType.Agent, RootPath = "agents" }
            ]
        };

        TemplateSearchRule rule = provider.GetRule(TemplateType.Instructions);

        Assert.IsNull(rule);
    }

    [TestMethod]
    public void ToString_ReturnsDisplayName()
    {
        var provider = new TemplateProvider
        {
            DisplayName = "Provider Display"
        };

        string text = provider.ToString();

        Assert.AreEqual("Provider Display", text);
    }
}
