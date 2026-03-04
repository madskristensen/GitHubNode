using System.Linq;
using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class TemplateProviderRegistryTests
{
    [TestMethod]
    public void CreateProviders_ReturnsKnownProviders()
    {
        var providers = TemplateProviderRegistry.CreateProviders();

        Assert.IsTrue(providers.Count >= 2);
        Assert.IsTrue(providers.Any(provider => provider.Id == AwesomeCopilotTemplateProvider.ProviderId));
        Assert.IsTrue(providers.Any(provider => provider.Id == DotNetSkillsTemplateProvider.ProviderId));
    }
}
