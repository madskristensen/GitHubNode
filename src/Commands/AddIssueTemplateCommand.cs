using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add an issue template.
    /// </summary>
    [Command(PackageIds.AddIssueTemplate)]
    internal sealed class AddIssueTemplateCommand : GitHubFileCommandBase<AddIssueTemplateCommand>
    {
        protected override string DialogTitle => "New Issue Template";
        protected override string DialogPrompt => "Enter the template name:";
        protected override string DialogDefaultValue => "Bug Report";
        protected override string ErrorMessagePrefix => "Failed to create issue template";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var templateFolder = Path.Combine(targetFolder, "ISSUE_TEMPLATE");
            var fileName = CommandHelpers.SanitizeFileName(userInput) + ".md";
            return Path.Combine(templateFolder, fileName);
        }

        protected override string GetFileContent(string userInput)
        {
            return string.Format(FileTemplates.IssueTemplate, userInput);
        }
    }
}
