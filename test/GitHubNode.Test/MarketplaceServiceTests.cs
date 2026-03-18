using System.Reflection;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceServiceTests
{
    [TestMethod]
    public void ParseMarketplaceInput_ParsesGheRepositoryUrl()
    {
        var result = InvokeParseMarketplaceInput("https://contoso.ghe.com/team/marketplace.git");

        Assert.AreEqual("team", result.owner);
        Assert.AreEqual("marketplace", result.repo);
        Assert.IsNull(result.branch);
        Assert.AreEqual("https://contoso.ghe.com/team/marketplace.git", result.repositoryUrl);
    }

    [TestMethod]
    public void ParseMarketplaceInput_ParsesRepositoryTreeUrl()
    {
        var result = InvokeParseMarketplaceInput("https://bitbucket.contoso.local/scm/team/marketplace/tree/main");

        Assert.AreEqual("team", result.owner);
        Assert.AreEqual("marketplace", result.repo);
        Assert.AreEqual("main", result.branch);
        Assert.AreEqual("https://bitbucket.contoso.local/scm/team/marketplace", result.repositoryUrl);
    }

    [TestMethod]
    public void ParseMarketplaceInput_ParsesScpStyleSshRepositoryUrl()
    {
        var result = InvokeParseMarketplaceInput("git@bitbucket.contoso.local:scm/team/marketplace.git");

        Assert.AreEqual("team", result.owner);
        Assert.AreEqual("marketplace", result.repo);
        Assert.IsNull(result.branch);
        Assert.AreEqual("git@bitbucket.contoso.local:scm/team/marketplace.git", result.repositoryUrl);
    }

    [TestMethod]
    public void ParseMarketplaceInput_ParsesSshRepositoryUrl()
    {
        var result = InvokeParseMarketplaceInput("ssh://git@bitbucket.contoso.local/scm/team/marketplace.git");

        Assert.AreEqual("team", result.owner);
        Assert.AreEqual("marketplace", result.repo);
        Assert.IsNull(result.branch);
        Assert.AreEqual("ssh://git@bitbucket.contoso.local/scm/team/marketplace.git", result.repositoryUrl);
    }

    private static (string owner, string repo, string branch, string repositoryUrl) InvokeParseMarketplaceInput(string input)
    {
        MethodInfo method = typeof(MarketplaceService).GetMethod("ParseMarketplaceInput", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        object result = method.Invoke(null, [input]);
        Assert.IsNotNull(result);

        return ((string owner, string repo, string branch, string repositoryUrl))result;
    }
}