using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Represents a MarketplaceInfo entry in the user's configuration.
    /// </summary>
    internal sealed class MarketplaceEntry
    {
        /// <summary>
        /// Gets or sets the GitHub owner (user or organization).
        /// </summary>
        [JsonPropertyName("owner")]
        public string Owner { get; set; }

        /// <summary>
        /// Gets or sets the repository name.
        /// </summary>
        [JsonPropertyName("repo")]
        public string Repo { get; set; }

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
}
