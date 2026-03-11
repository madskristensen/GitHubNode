using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a GitHub Actions workflow file.
    /// </summary>
    [Command(PackageIds.AddWorkflow)]
    internal sealed class AddWorkflowCommand : GitHubFileCommandBase<AddWorkflowCommand>
    {
        protected override string RequiredRootFolderName => ".github";
        protected override string DialogTitle => "New GitHub Actions Workflow";
        protected override string DialogPrompt => "Enter the workflow name:";
        protected override string DialogDefaultValue => "build.yml";
        protected override string ErrorMessagePrefix => "Failed to create workflow";
        protected override TemplateType? TemplateType => Services.TemplateType.Workflow;
        protected override string RequiredExtension => ".yml";
        protected override string SubfolderName => "workflows";

        protected override string GetFilePath(string targetFolder, string userInput)
            => BuildFilePath(targetFolder, userInput);

        protected override string GetFileContent(string userInput)
            => string.Format(FileTemplates.GitHubActionsWorkflow, GetBaseName(userInput));
    }
}
