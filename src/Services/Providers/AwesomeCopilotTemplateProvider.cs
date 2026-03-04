using System.Collections.Generic;

namespace GitHubNode.Services
{
    internal static class AwesomeCopilotTemplateProvider
    {
        internal const string ProviderId = "awesome-copilot";

        public static TemplateProvider Create()
        {
            return new TemplateProvider
            {
                Id = ProviderId,
                DisplayName = "GitHub Awesome Copilot",
                RepoOwner = "github",
                RepoName = "awesome-copilot",
                Branch = "main",
                SearchRules =
                [
                    new TemplateSearchRule { TemplateType = TemplateType.Agent, RootPath = "agents", FileSuffix = ".md" },
                    new TemplateSearchRule { TemplateType = TemplateType.Prompt, RootPath = "prompts", FileSuffix = ".md" },
                    new TemplateSearchRule { TemplateType = TemplateType.Instructions, RootPath = "instructions", FileSuffix = ".md" },
                    new TemplateSearchRule
                    {
                        TemplateType = TemplateType.Skill,
                        RootPath = "skills",
                        UseFolderNameAsTemplateName = true,
                        FileName = "skill.md"
                    }
                ]
            };
        }
    }
}
