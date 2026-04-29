using System;
using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Result of MCP server discovery from a well-known location.
    /// </summary>
    internal sealed class McpServerDiscoveryResult
    {
        /// <summary>
        /// Gets or sets the unique identifier for this discovery source.
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the URI of the discovery source (well-known endpoint).
        /// </summary>
        public Uri DiscoveryUri { get; set; }

        /// <summary>
        /// Gets or sets the display name for this discovery source.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the origin (host) of this discovery source.
        /// </summary>
        public string Origin { get; set; }

        /// <summary>
        /// Gets the list of discovered MCP servers.
        /// </summary>
        public List<McpServerDefinition> Servers { get; } = new List<McpServerDefinition>();

        /// <summary>
        /// Gets the list of warnings encountered during discovery.
        /// </summary>
        public List<string> Warnings { get; } = new List<string>();
    }

    /// <summary>
    /// Represents an MCP server definition discovered from a well-known location.
    /// </summary>
    internal sealed class McpServerDefinition
    {
        /// <summary>
        /// Gets or sets the server name.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the server description.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the server version.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the artifact URI (URL to download the server definition).
        /// </summary>
        public Uri ArtifactUri { get; set; }

        /// <summary>
        /// Gets or sets optional digest for verification.
        /// </summary>
        public string Digest { get; set; }
    }

    /// <summary>
    /// Internal model for JSON deserialization of server-card.json format.
    /// </summary>
    internal sealed class RawMcpServerCard
    {
        [JsonPropertyName("$schema")]
        public string Schema { get; set; }

        public List<RawMcpServer> Servers { get; set; }
    }

    /// <summary>
    /// Internal model for a single MCP server in server-card.json format.
    /// </summary>
    internal sealed class RawMcpServer
    {
        public string Name { get; set; }

        public string Description { get; set; }

        public string Version { get; set; }

        public string Url { get; set; }

        public string Digest { get; set; }
    }
}
