using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Services
{
    /// <summary>
    /// Registry for template providers.
    /// Routes template requests to the marketplace system.
    /// </summary>
    internal static class TemplateProviderRegistry
    {
        /// <summary>
        /// Gets providers that support a specific template type.
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
