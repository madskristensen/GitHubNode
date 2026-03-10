using System.Collections.Generic;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Represents owner information in a MarketplaceInfo.json file.
    /// </summary>
    internal sealed record MarketplaceOwner
    {
        /// <summary>
        /// Gets the owner name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the owner email.
        /// </summary>
        public string Email { get; init; }
    }

    /// <summary>
    /// Represents metadata in a MarketplaceInfo.json file.
    /// </summary>
    internal sealed record MarketplaceMetadata
    {
        /// <summary>
        /// Gets the MarketplaceInfo description.
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// Gets the MarketplaceInfo version.
        /// </summary>
        public string Version { get; init; }
    }

    /// <summary>
    /// Represents a MarketplaceInfo - a repository containing plugins for Copilot.
    /// Marketplaces are defined by a MarketplaceInfo.json file at .github/plugin/MarketplaceInfo.json.
    /// </summary>
    internal sealed class MarketplaceInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for this MarketplaceInfo (owner/repo format).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the GitHub owner (user or organization).
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        /// Gets or sets the repository name.
        /// </summary>
        public string RepoName { get; set; }

        /// <summary>
        /// Gets or sets the branch to use.
        /// </summary>
        public string Branch { get; set; } = "main";

        /// <summary>
        /// Gets or sets the display name from MarketplaceInfo.json.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the description from MarketplaceInfo.json metadata.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the owner information from MarketplaceInfo.json.
        /// </summary>
        public MarketplaceOwner OwnerInfo { get; set; }

        /// <summary>
        /// Gets or sets the metadata from MarketplaceInfo.json.
        /// </summary>
        public MarketplaceMetadata Metadata { get; set; }

        /// <summary>
        /// Gets or sets whether this is a built-in MarketplaceInfo.
        /// Built-in marketplaces cannot be removed by the user.
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Gets or sets the local path where the MarketplaceInfo is cloned.
        /// </summary>
        public string LocalPath { get; set; }

        /// <summary>
        /// Gets or sets whether the MarketplaceInfo has been cloned successfully.
        /// </summary>
        public bool IsCloned { get; set; }

        /// <summary>
        /// Gets or sets the last time the MarketplaceInfo was updated (git pull).
        /// </summary>
        public System.DateTime? LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets any error message from the last operation.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the path to the locally cached icon file.
        /// </summary>
        public string IconPath { get; set; }

        /// <summary>
        /// Gets or sets the plugins defined in this MarketplaceInfo.
        /// </summary>
        public List<MarketplacePlugin> Plugins { get; set; } = new List<MarketplacePlugin>();

        /// <summary>
        /// Gets the GitHub URL for this MarketplaceInfo.
        /// </summary>
        public string GitHubUrl => $"https://github.com/{Owner}/{RepoName}";

        /// <summary>
        /// Gets the clone URL for this MarketplaceInfo.
        /// </summary>
        public string CloneUrl => $"https://github.com/{Owner}/{RepoName}.git";

        /// <summary>
        /// Gets all assets of a specific type across all plugins.
        /// </summary>
        public IEnumerable<PluginAsset> GetAllAssets(AssetType type)
        {
            foreach (var plugin in Plugins)
            {
                foreach (var asset in plugin.GetAssets(type))
                {
                    yield return asset;
                }
            }
        }

        /// <summary>
        /// Returns true if any plugin contains assets of the specified type.
        /// </summary>
        public bool HasAssetType(AssetType type)
        {
            foreach (var plugin in Plugins)
            {
                if (plugin.HasAssetType(type))
                {
                    return true;
                }
            }

            return false;
        }
    }
}
