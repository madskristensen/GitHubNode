using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class AwesomeCopilotTemplateProviderTests
{
    [TestMethod]
    public void Create_ReturnsExpectedRepositoryAndBranch()
    {
        TemplateProvider provider = AwesomeCopilotTemplateProvider.Create();

        Assert.AreEqual("github", provider.RepoOwner);
        Assert.AreEqual("awesome-copilot", provider.RepoName);
        Assert.AreEqual("main", provider.Branch);
    }

    [TestMethod]
    public void Create_ContainsExpectedTemplateRules()
    {
        TemplateProvider provider = AwesomeCopilotTemplateProvider.Create();

        Assert.IsNotNull(provider.GetRule(TemplateType.Agent));
        Assert.IsNotNull(provider.GetRule(TemplateType.Prompt));
        Assert.IsNotNull(provider.GetRule(TemplateType.Instructions));
        Assert.IsNotNull(provider.GetRule(TemplateType.Skill));
    }

    [TestMethod]
    public void Create_UsesFolderTemplateRule_ForSkill()
    {
        TemplateProvider provider = AwesomeCopilotTemplateProvider.Create();

        TemplateSearchRule skillRule = provider.GetRule(TemplateType.Skill);

        Assert.IsNotNull(skillRule);
        Assert.AreEqual("skills", skillRule.RootPath);
        Assert.IsTrue(skillRule.UseFolderNameAsTemplateName);
        Assert.AreEqual("skill.md", skillRule.FileName);
    }
}
