namespace GitHubNode.Services
{
    /// <summary>
    /// Types of templates available from marketplace repositories.
    /// </summary>
    internal enum TemplateType
    {
        Agent,
        Prompt,
        Skill,
        Instructions
    }

    /// <summary>
    /// Information about a template from a marketplace repository.
    /// </summary>
    internal class TemplateInfo
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public string DownloadUrl { get; set; }
        public TemplateType TemplateType { get; set; }
        public string ProviderId { get; set; }

        /// <summary>
        /// Local file path to the template content.
        /// </summary>
        public string LocalPath { get; set; }

        /// <summary>
        /// Cached content of the template file.
        /// </summary>
        public string Content { get; set; }
    }

    internal sealed class TemplateContentResult
    {
        public static TemplateContentResult Empty { get; } = new TemplateContentResult();

        public string Content { get; private set; }

        public string ErrorMessage { get; private set; }

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        public static TemplateContentResult FromContent(string content)
            => new TemplateContentResult { Content = content };

        public static TemplateContentResult FromError(string errorMessage)
            => new TemplateContentResult { ErrorMessage = errorMessage };
    }
}
