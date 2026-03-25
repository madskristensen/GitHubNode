using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Adapter that bridges the new Marketplace system with the existing template UI.
    /// Converts marketplace assets to TemplateInfo objects for backward compatibility.
    /// </summary>
    internal static class MarketplaceTemplateAdapter
    {
        /// <summary>
        /// Converts a marketplace AssetType to the legacy TemplateType.
        /// </summary>
        public static TemplateType? ToTemplateType(AssetType assetType)
        {
            return assetType switch
            {
                AssetType.Agent => TemplateType.Agent,
                AssetType.Skill => TemplateType.Skill,
                AssetType.Instructions => TemplateType.Instructions,
                AssetType.Prompt => TemplateType.Prompt,
                _ => null // McpServer doesn't have a TemplateType equivalent
            };
        }

        /// <summary>
        /// Converts a legacy TemplateType to marketplace AssetType.
        /// </summary>
        public static AssetType ToAssetType(TemplateType templateType)
        {
            return templateType switch
            {
                TemplateType.Agent => AssetType.Agent,
                TemplateType.Skill => AssetType.Skill,
                TemplateType.Instructions => AssetType.Instructions,
                TemplateType.Prompt => AssetType.Prompt,
                _ => AssetType.Agent // Fallback
            };
        }

        /// <summary>
        /// Gets templates for a specific type from all marketplaces.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(
            TemplateType templateType,
            CancellationToken cancellationToken = default)
        {
            var assetType = ToAssetType(templateType);
            var assets = await MarketplaceService.GetAllAssetsAsync(assetType, cancellationToken);
            return ConvertToTemplateInfos(assets, templateType);
        }

        /// <summary>
        /// Gets templates for a specific type from a specific marketplace.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(
            TemplateType templateType,
            MarketplaceInfo marketplace,
            CancellationToken cancellationToken = default)
        {
            if (marketplace == null)
            {
                return new List<TemplateInfo>();
            }

            var assetType = ToAssetType(templateType);
            var assets = new List<PluginAsset>(marketplace.GetAllAssets(assetType));

            if (assets.Count == 0 && templateType == TemplateType.Instructions)
            {
                assets.AddRange(DiscoverInstructionAssetsFromRepository(marketplace));
            }

            return ConvertToTemplateInfos(assets, templateType);
        }

        /// <summary>
        /// Gets marketplaces that support a specific template type.
        /// Returns them as pseudo-providers for the UI dropdown.
        /// </summary>
        public static async Task<List<MarketplaceAsProvider>> GetProvidersForTemplateTypeAsync(
            TemplateType templateType,
            CancellationToken cancellationToken = default)
        {
            var assetType = ToAssetType(templateType);
            var marketplaces = await MarketplaceService.GetMarketplacesWithAssetTypeAsync(assetType, cancellationToken);

            if (marketplaces.Count == 0)
            {
                marketplaces = await MarketplaceService.GetAllMarketplacesAsync(forceRefresh: false, cancellationToken);
            }

            var providers = new List<MarketplaceAsProvider>();
            foreach (var marketplace in marketplaces)
            {
                providers.Add(new MarketplaceAsProvider(marketplace));
            }

            return providers;
        }

        private static IEnumerable<PluginAsset> DiscoverInstructionAssetsFromRepository(MarketplaceInfo marketplace)
        {
            if (marketplace == null || string.IsNullOrWhiteSpace(marketplace.LocalPath) || !System.IO.Directory.Exists(marketplace.LocalPath))
            {
                return new List<PluginAsset>();
            }

            var discovered = new List<PluginAsset>();
            var addedPaths = new HashSet<string>(System.StringComparer.OrdinalIgnoreCase);

            foreach (var file in System.IO.Directory.GetFiles(marketplace.LocalPath, "*.instructions.md", System.IO.SearchOption.AllDirectories))
            {
                AddInstructionAsset(discovered, addedPaths, marketplace, file);
            }

            foreach (var file in System.IO.Directory.GetFiles(marketplace.LocalPath, "instructions.md", System.IO.SearchOption.AllDirectories))
            {
                AddInstructionAsset(discovered, addedPaths, marketplace, file);
            }

            foreach (var file in System.IO.Directory.GetFiles(marketplace.LocalPath, "copilot-instructions.md", System.IO.SearchOption.AllDirectories))
            {
                AddInstructionAsset(discovered, addedPaths, marketplace, file);
            }

            return discovered;
        }

        private static void AddInstructionAsset(
            List<PluginAsset> assets,
            HashSet<string> addedPaths,
            MarketplaceInfo marketplace,
            string file)
        {
            if (string.IsNullOrWhiteSpace(file) || !addedPaths.Add(file))
            {
                return;
            }

            var name = System.IO.Path.GetFileNameWithoutExtension(file);
            if (name != null && name.EndsWith(".instructions", System.StringComparison.OrdinalIgnoreCase))
            {
                name = name.Substring(0, name.Length - ".instructions".Length);
            }

            var pluginName = System.IO.Path.GetFileName(System.IO.Path.GetDirectoryName(file));
            if (string.IsNullOrWhiteSpace(pluginName))
            {
                pluginName = marketplace.DisplayName;
            }

            assets.Add(new PluginAsset
            {
                Name = string.IsNullOrWhiteSpace(name) ? System.IO.Path.GetFileName(file) : name,
                Type = AssetType.Instructions,
                RelativePath = file.StartsWith(marketplace.LocalPath, System.StringComparison.OrdinalIgnoreCase)
                    ? file.Substring(marketplace.LocalPath.Length).TrimStart('\\', '/')
                    : file,
                LocalPath = file,
                PluginName = pluginName,
                MarketplaceId = marketplace.Id
            });
        }

        /// <summary>
        /// Gets the content of a template.
        /// </summary>
        public static string GetTemplateContent(TemplateInfo template)
        {
            // If content is already cached, return it
            if (!string.IsNullOrEmpty(template?.Content))
            {
                return template.Content;
            }

            // For marketplace-based templates, the DownloadUrl is actually the local path
            if (!string.IsNullOrEmpty(template?.DownloadUrl) && System.IO.File.Exists(template.DownloadUrl))
            {
                return System.IO.File.ReadAllText(template.DownloadUrl);
            }

            return null;
        }

        private static List<TemplateInfo> ConvertToTemplateInfos(List<PluginAsset> assets, TemplateType templateType)
        {
            var templates = new List<TemplateInfo>();

            foreach (var asset in assets)
            {
                // For skills, use the asset name (folder name) as the filename
                // since the actual file is always "skill.md"
                var fileName = templateType == TemplateType.Skill
                    ? asset.Name
                    : System.IO.Path.GetFileName(asset.LocalPath);

                templates.Add(new TemplateInfo
                {
                    Name = asset.Name,
                    FileName = fileName,
                    DisplayName = asset.Name,
                    Description = asset.Description,
                    Category = asset.PluginName,
                    DownloadUrl = asset.LocalPath, // Local path since we're using git clone
                    TemplateType = templateType,
                    ProviderId = asset.MarketplaceId
                });
            }

            return templates;
        }
    }

    /// <summary>
    /// Wraps a MarketplaceInfo to look like a TemplateProvider for the existing UI.
    /// </summary>
    internal sealed class MarketplaceAsProvider
    {
        public MarketplaceInfo Marketplace { get; }

        public MarketplaceAsProvider(MarketplaceInfo marketplace)
        {
            Marketplace = marketplace;
        }

        public string Id => Marketplace?.Id ?? "unknown";

        public string DisplayName => Marketplace?.DisplayName ?? Marketplace?.Id ?? "Unknown";

        public string Description => Marketplace?.Description;

        public bool IsBuiltIn => Marketplace?.IsBuiltIn ?? false;

        public bool HasError => !string.IsNullOrEmpty(Marketplace?.ErrorMessage);

        public string ErrorMessage => Marketplace?.ErrorMessage;

        public override string ToString() => DisplayName;
    }
}
