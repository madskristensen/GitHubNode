using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Services
{
    /// <summary>
    /// Registry for template providers.
    /// This class bridges the old provider system with the new Marketplace system.
    /// </summary>
    internal static class TemplateProviderRegistry
    {
        /// <summary>
        /// Creates the legacy template providers for backward compatibility.
        /// </summary>
        [System.Obsolete("Use GetProvidersForTemplateTypeAsync instead. This method is kept for backward compatibility.")]
        public static List<TemplateProvider> CreateProviders()
        {
            return
            [
                AwesomeCopilotTemplateProvider.Create(),
                DotNetSkillsTemplateProvider.Create(),
                AnthropicSkillsTemplateProvider.Create()
            ];
        }

        /// <summary>
        /// Gets providers that support a specific template type.
        /// Now uses the marketplace system internally.
        /// </summary>
        public static async Task<List<MarketplaceAsProvider>> GetProvidersForTemplateTypeAsync(
            TemplateType templateType,
            CancellationToken cancellationToken = default)
        {
            return await MarketplaceTemplateAdapter.GetProvidersForTemplateTypeAsync(templateType, cancellationToken);
        }

        /// <summary>
        /// Gets templates for a specific type from all marketplaces.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(
            TemplateType templateType,
            CancellationToken cancellationToken = default)
        {
            return await MarketplaceTemplateAdapter.GetTemplatesAsync(templateType, cancellationToken);
        }

        /// <summary>
        /// Gets templates for a specific type from a specific marketplace.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(
            TemplateType templateType,
            MarketplaceAsProvider provider,
            CancellationToken cancellationToken = default)
        {
            if (provider?.Marketplace == null)
            {
                return new List<TemplateInfo>();
            }

            return await MarketplaceTemplateAdapter.GetTemplatesAsync(templateType, provider.Marketplace, cancellationToken);
        }

        /// <summary>
        /// Gets template content from a template info.
        /// </summary>
        public static string GetTemplateContent(TemplateInfo template)
        {
            return MarketplaceTemplateAdapter.GetTemplateContent(template);
        }
    }
}
