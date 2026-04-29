using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Represents a MarketplaceInfo entry in the user's configuration.
    /// </summary>
    internal sealed class MarketplaceEntry
    {
        /// <summary>
        /// Gets or sets the marketplace source kind. Missing values are treated as repository sources.
        /// </summary>
        [JsonPropertyName("sourceKind")]
        [JsonConverter(typeof(MarketplaceSourceKindJsonConverter))]
        public MarketplaceSourceKind SourceKind { get; set; } = MarketplaceSourceKind.Repository;

        /// <summary>
        /// Gets or sets the GitHub owner (user or organization), or source host for discovery sources.
        /// </summary>
        [JsonPropertyName("owner")]
        public string Owner { get; set; }

        /// <summary>
        /// Gets or sets the repository name, or source name for discovery sources.
        /// </summary>
        [JsonPropertyName("repo")]
        public string Repo { get; set; }

        /// <summary>
        /// Gets or sets the repository URL when the marketplace is not hosted on github.com.
        /// </summary>
        [JsonPropertyName("url")]
        public string RepositoryUrl { get; set; }

        /// <summary>
        /// Gets or sets the Well-Known Discovery index URL for discovery sources.
        /// </summary>
        [JsonPropertyName("wellKnownIndexUrl")]
        public string WellKnownIndexUrl { get; set; }

        /// <summary>
        /// Legacy JSON shim. Older configs persisted the well-known index URL as
        /// "agentSkillsIndexUrl". When present and the new property is missing,
        /// the value is mirrored into <see cref="WellKnownIndexUrl"/> on load so
        /// user-added entries remain visible after the rename.
        /// </summary>
        [JsonPropertyName("agentSkillsIndexUrl")]
        [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
        public string LegacyAgentSkillsIndexUrl
        {
            get => null;
            set
            {
                if (string.IsNullOrWhiteSpace(WellKnownIndexUrl) && !string.IsNullOrWhiteSpace(value))
                {
                    WellKnownIndexUrl = value;
                }
            }
        }

        /// <summary>
        /// Gets or sets the display name for non-repository marketplace sources.
        /// </summary>
        [JsonPropertyName("displayName")]
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets whether the user trusted this source when it was added.
        /// </summary>
        [JsonPropertyName("trusted")]
        public bool IsTrusted { get; set; }

        /// <summary>
        /// Gets or sets the branch to use. Defaults to "main".
        /// </summary>
        [JsonPropertyName("branch")]
        public string Branch { get; set; } = "main";
    }

    /// <summary>
    /// User configuration for marketplaces.
    /// Stored in %LocalAppData%/GitHubNode/marketplaces.json.
    /// </summary>
    internal sealed class MarketplaceConfig
    {
        /// <summary>
        /// Gets or sets the list of user-added marketplaces.
        /// </summary>
        [JsonPropertyName("marketplaces")]
        public List<MarketplaceEntry> Marketplaces { get; set; } = new List<MarketplaceEntry>();

        /// <summary>
        /// Gets or sets the update interval in hours. Defaults to 168 (7 days).
        /// </summary>
        [JsonPropertyName("updateIntervalHours")]
        public int UpdateIntervalHours { get; set; } = 168;

        /// <summary>
        /// Gets or sets whether built-in marketplaces are enabled. Defaults to true.
        /// </summary>
        [JsonPropertyName("enableBuiltInMarketplaces")]
        public bool EnableBuiltInMarketplaces { get; set; } = true;
    }

    /// <summary>
    /// Tolerant string-based converter for <see cref="MarketplaceSourceKind"/>.
    /// Accepts the current names plus the legacy "AgentSkillsDiscovery" value
    /// written by older versions of the extension. Unknown values fall back to
    /// <see cref="MarketplaceSourceKind.Repository"/> so a single bad entry does
    /// not break loading the entire config.
    /// </summary>
    internal sealed class MarketplaceSourceKindJsonConverter : JsonConverter<MarketplaceSourceKind>
    {
        public override MarketplaceSourceKind Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType == JsonTokenType.Null)
            {
                return MarketplaceSourceKind.Repository;
            }

            if (reader.TokenType == JsonTokenType.String)
            {
                var value = reader.GetString();
                if (string.IsNullOrWhiteSpace(value))
                {
                    return MarketplaceSourceKind.Repository;
                }

                if (string.Equals(value, "AgentSkillsDiscovery", StringComparison.OrdinalIgnoreCase))
                {
                    return MarketplaceSourceKind.WellKnownDiscovery;
                }

                if (Enum.TryParse<MarketplaceSourceKind>(value, ignoreCase: true, out var parsed))
                {
                    return parsed;
                }
            }

            return MarketplaceSourceKind.Repository;
        }

        public override void Write(Utf8JsonWriter writer, MarketplaceSourceKind value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
