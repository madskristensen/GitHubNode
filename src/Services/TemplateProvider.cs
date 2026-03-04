using System.Collections.Generic;

namespace GitHubNode.Services
{
    internal sealed class TemplateProvider
    {
        public string Id { get; set; }

        public string DisplayName { get; set; }

        public string RepoOwner { get; set; }

        public string RepoName { get; set; }

        public string Branch { get; set; }

        public List<TemplateSearchRule> SearchRules { get; set; } = new();

        public TemplateSearchRule GetRule(TemplateType templateType)
            => SearchRules?.Find(rule => rule.TemplateType == templateType);

        public override string ToString()
            => DisplayName;
    }

    internal sealed class TemplateSearchRule
    {
        public TemplateType TemplateType { get; set; }

        public string RootPath { get; set; }

        public bool Recursive { get; set; }

        public string FileSuffix { get; set; }

        public string FileName { get; set; }

        public bool UseFolderNameAsTemplateName { get; set; }
    }
}
