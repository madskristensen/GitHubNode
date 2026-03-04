using System.Collections.Generic;

namespace GitHubNode.Services
{
    internal static class DotNetSkillsTemplateProvider
    {
        internal const string ProviderId = "dotnet-skills-plugins";

        public static TemplateProvider Create()
        {
            return new TemplateProvider
            {
                Id = ProviderId,
                DisplayName = "dotnet/skills plugins",
                RepoOwner = "dotnet",
                RepoName = "skills",
                Branch = "main",
                SearchRules =
                [
                    new TemplateSearchRule
                    {
                        TemplateType = TemplateType.Agent,
                        RootPath = "plugins",
                        Recursive = true,
                        FileSuffix = ".agent.md"
                    },
                    new TemplateSearchRule
                    {
                        TemplateType = TemplateType.Skill,
                        RootPath = "plugins",
                        Recursive = true,
                        FileName = "SKILL.md"
                    }
                ]
            };
        }
    }
}
