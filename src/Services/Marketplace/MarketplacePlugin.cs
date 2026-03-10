using System.Collections.Generic;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Represents an installable asset within a MarketplaceInfo plugin.
    /// </summary>
    internal sealed class PluginAsset
    {
        /// <summary>
        /// Gets or sets the type of asset.
        /// </summary>
        public AssetType Type { get; set; }

        /// <summary>
        /// Gets or sets the display name of the asset.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the description of the asset.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the relative file path within the cloned repository.
        /// </summary>
        public string RelativePath { get; set; }

        /// <summary>
        /// Gets or sets the full local file path in the cloned repository.
        /// </summary>
        public string LocalPath { get; set; }

        /// <summary>
        /// Gets or sets the name of the plugin this asset belongs to.
        /// </summary>
        public string PluginName { get; set; }

        /// <summary>
        /// Gets or sets the MarketplaceInfo ID this asset belongs to.
        /// </summary>
        public string MarketplaceId { get; set; }
    }

    /// <summary>
    /// Represents a plugin within a MarketplaceInfo.
    /// A plugin is a collection of related assets (agents, skills, instructions, etc.).
    /// </summary>
    internal sealed class MarketplacePlugin
    {
        /// <summary>
        /// Gets or sets the unique name of the plugin.
        /// </summary>
        public string Name { get; set; }

        /// <summary>
        /// Gets or sets the description of the plugin.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the version of the plugin.
        /// </summary>
        public string Version { get; set; }

        /// <summary>
        /// Gets or sets the relative source path within the repository.
        /// </summary>
        public string Source { get; set; }

        /// <summary>
        /// Gets or sets the skill paths referenced by this plugin.
        /// </summary>
        public List<string> Skills { get; set; } = new List<string>();

        /// <summary>
        /// Gets or sets the assets discovered in this plugin.
        /// </summary>
        public List<PluginAsset> Assets { get; set; } = new List<PluginAsset>();

        /// <summary>
        /// Gets or sets the MarketplaceInfo ID this plugin belongs to.
        /// </summary>
        public string MarketplaceId { get; set; }

        /// <summary>
        /// Gets assets of a specific type.
        /// </summary>
        public IEnumerable<PluginAsset> GetAssets(AssetType type)
        {
            foreach (var asset in Assets)
            {
                if (asset.Type == type)
                {
                    yield return asset;
                }
            }
        }

        /// <summary>
        /// Returns true if the plugin contains any assets of the specified type.
        /// </summary>
        public bool HasAssetType(AssetType type)
        {
            foreach (var asset in Assets)
            {
                if (asset.Type == type)
                {
                    return true;
                }
            }

            return false;
        }
    }
}
