using System.Linq;
using System.Text.Json;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceConfigTests
{
    [TestMethod]
    public void Deserialize_AcceptsLegacyAgentSkillsDiscoverySourceKind()
    {
        const string json = """
        {
            "marketplaces": [
                {
                    "sourceKind": "AgentSkillsDiscovery",
                    "owner": "docs.stripe.com",
                    "repo": "agent-skills",
                    "url": "https://docs.stripe.com/.well-known/skills/index.json",
                    "agentSkillsIndexUrl": "https://docs.stripe.com/.well-known/skills/index.json",
                    "displayName": "Agent Skills - docs.stripe.com",
                    "trusted": true,
                    "branch": "main"
                }
            ],
            "updateIntervalHours": 168,
            "enableBuiltInMarketplaces": true
        }
        """;

        MarketplaceConfig config = JsonSerializer.Deserialize<MarketplaceConfig>(json);

        Assert.IsNotNull(config);
        Assert.HasCount(1, config.Marketplaces);

        MarketplaceEntry entry = config.Marketplaces.Single();
        Assert.AreEqual(MarketplaceSourceKind.WellKnownDiscovery, entry.SourceKind);
        Assert.AreEqual("https://docs.stripe.com/.well-known/skills/index.json", entry.WellKnownIndexUrl);
        Assert.AreEqual("docs.stripe.com", entry.Owner);
    }

    [TestMethod]
    public void Deserialize_PreservesUserEntries_WhenLegacyAndNewSourcesAreMixed()
    {
        const string json = """
        {
            "marketplaces": [
                {
                    "sourceKind": "Repository",
                    "owner": "madskristensen",
                    "repo": "vs-agent-plugins",
                    "url": "https://github.com/madskristensen/vs-agent-plugins",
                    "branch": "main"
                },
                {
                    "sourceKind": "AgentSkillsDiscovery",
                    "owner": "schemastore.org",
                    "repo": "agent-skills",
                    "url": "https://schemastore.org/.well-known/agent-skills/index.json",
                    "agentSkillsIndexUrl": "https://schemastore.org/.well-known/agent-skills/index.json",
                    "trusted": true
                }
            ]
        }
        """;

        MarketplaceConfig config = JsonSerializer.Deserialize<MarketplaceConfig>(json);

        Assert.IsNotNull(config);
        Assert.HasCount(2, config.Marketplaces);
        Assert.AreEqual(MarketplaceSourceKind.Repository, config.Marketplaces[0].SourceKind);
        Assert.AreEqual(MarketplaceSourceKind.WellKnownDiscovery, config.Marketplaces[1].SourceKind);
        Assert.AreEqual("https://schemastore.org/.well-known/agent-skills/index.json", config.Marketplaces[1].WellKnownIndexUrl);
    }

    [TestMethod]
    public void Deserialize_FallsBackToRepository_ForUnknownSourceKind()
    {
        const string json = """
        {
            "marketplaces": [
                {
                    "sourceKind": "SomethingUnknown",
                    "owner": "someone",
                    "repo": "something"
                }
            ]
        }
        """;

        MarketplaceConfig config = JsonSerializer.Deserialize<MarketplaceConfig>(json);

        Assert.IsNotNull(config);
        Assert.HasCount(1, config.Marketplaces);
        Assert.AreEqual(MarketplaceSourceKind.Repository, config.Marketplaces[0].SourceKind);
    }

    [TestMethod]
    public void Serialize_DoesNotEmitLegacyAgentSkillsIndexUrl()
    {
        var config = new MarketplaceConfig
        {
            Marketplaces =
            {
                new MarketplaceEntry
                {
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    Owner = "docs.stripe.com",
                    Repo = "well-known",
                    WellKnownIndexUrl = "https://docs.stripe.com/"
                }
            }
        };

        string json = JsonSerializer.Serialize(config);

        Assert.DoesNotContain("agentSkillsIndexUrl", json);
        Assert.Contains("wellKnownIndexUrl", json);
    }
}
