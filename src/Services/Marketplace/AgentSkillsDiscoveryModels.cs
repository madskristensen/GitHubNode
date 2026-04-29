using System;
using System.Collections.Generic;

namespace GitHubNode.Services.Marketplace
{
    internal enum MarketplaceSourceKind
    {
        Repository,
        AgentSkillsDiscovery,
        McpServerDiscovery
    }

    internal sealed class AgentSkillsDiscoveryResult
    {
        public string Id { get; set; }

        public Uri IndexUri { get; set; }

        public string DisplayName { get; set; }

        public string Origin { get; set; }

        public string CacheDirectory { get; set; }

        public string IconPath { get; set; }

        public DateTime LastUpdated { get; set; }

        public List<AgentSkillsDiscoverySkill> Skills { get; } = new List<AgentSkillsDiscoverySkill>();

        public List<string> Warnings { get; } = new List<string>();
    }

    internal sealed class AgentSkillsDiscoverySkill
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public string Type { get; set; }

        public Uri ArtifactUri { get; set; }

        public string Digest { get; set; }

        public string LocalSkillPath { get; set; }
    }
}
