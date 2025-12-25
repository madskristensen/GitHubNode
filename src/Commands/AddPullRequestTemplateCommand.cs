using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a pull request template.
    /// </summary>
    [Command(PackageIds.AddPullRequestTemplate)]
    internal sealed class AddPullRequestTemplateCommand : GitHubFileCommandBase<AddPullRequestTemplateCommand>
    {
        // No dialog needed - fixed file name
        protected override string DialogTitle => null;
        protected override string ErrorMessagePrefix => "Failed to create PR template";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            return Path.Combine(targetFolder, "PULL_REQUEST_TEMPLATE.md");
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.PullRequestTemplate;
        }
    }
}
