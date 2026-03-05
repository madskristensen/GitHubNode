using System.Collections.Generic;

namespace GitHubNode.Services
{
    internal static class TemplateProviderRegistry
    {
        public static List<TemplateProvider> CreateProviders()
        {
            return
            [
                AwesomeCopilotTemplateProvider.Create(),
                DotNetSkillsTemplateProvider.Create(),
                AnthropicSkillsTemplateProvider.Create()
            ];
        }
    }
}
