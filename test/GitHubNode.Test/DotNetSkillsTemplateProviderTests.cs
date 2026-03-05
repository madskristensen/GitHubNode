using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class DotNetSkillsTemplateProviderTests
{
    [TestMethod]
    public void Create_ReturnsExpectedRepositoryAndBranch()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        Assert.AreEqual("dotnet", provider.RepoOwner);
        Assert.AreEqual("skills", provider.RepoName);
        Assert.AreEqual("main", provider.Branch);
    }

    [TestMethod]
    public void Create_AgentRule_UsesRecursivePluginSearch()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        TemplateSearchRule agentRule = provider.GetRule(TemplateType.Agent);

        Assert.IsNotNull(agentRule);
        Assert.AreEqual("plugins", agentRule.RootPath);
        Assert.IsTrue(agentRule.Recursive);
        Assert.AreEqual(".agent.md", agentRule.FileSuffix);
    }

    [TestMethod]
    public void Create_SkillRule_UsesSkillFileName()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        TemplateSearchRule skillRule = provider.GetRule(TemplateType.Skill);

        Assert.IsNotNull(skillRule);
        Assert.AreEqual("plugins", skillRule.RootPath);
        Assert.IsTrue(skillRule.Recursive);
        Assert.AreEqual("SKILL.md", skillRule.FileName);
    }

    [TestMethod]
    public void AnthropicCreate_ReturnsExpectedRepositoryAndBranch()
    {
        TemplateProvider provider = AnthropicSkillsTemplateProvider.Create();

        Assert.AreEqual("anthropics", provider.RepoOwner);
        Assert.AreEqual("skills", provider.RepoName);
        Assert.AreEqual("main", provider.Branch);
    }

    [TestMethod]
    public void AnthropicCreate_SkillRule_UsesRecursiveSkillFileName()
    {
        TemplateProvider provider = AnthropicSkillsTemplateProvider.Create();

        TemplateSearchRule skillRule = provider.GetRule(TemplateType.Skill);

        Assert.IsNotNull(skillRule);
        Assert.AreEqual("skills", skillRule.RootPath);
        Assert.IsTrue(skillRule.Recursive);
        Assert.AreEqual("SKILL.md", skillRule.FileName);
    }
}
