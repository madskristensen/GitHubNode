using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a GitHub Actions workflow file.
    /// </summary>
    [Command(PackageIds.AddWorkflow)]
    internal sealed class AddWorkflowCommand : GitHubFileCommandBase<AddWorkflowCommand>
    {
        protected override string DialogTitle => "New GitHub Actions Workflow";
        protected override string DialogPrompt => "Enter the workflow name:";
        protected override string DialogDefaultValue => "build";
        protected override string ErrorMessagePrefix => "Failed to create workflow";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var workflowsFolder = Path.Combine(targetFolder, "Workflows");
            var fileName = CommandHelpers.SanitizeFileName(userInput) + ".yml";
            return Path.Combine(workflowsFolder, fileName);
        }

        protected override string GetFileContent(string userInput)
        {
            return string.Format(FileTemplates.GitHubActionsWorkflow, userInput);
        }
    }
}
