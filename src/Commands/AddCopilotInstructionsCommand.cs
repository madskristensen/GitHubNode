using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a Copilot instructions file in the instructions folder.
    /// </summary>
    [Command(PackageIds.AddCopilotInstructions)]
    internal sealed class AddCopilotInstructionsCommand : GitHubFileCommandBase<AddCopilotInstructionsCommand>
    {
        protected override string DialogTitle => "New Copilot Instructions";
        protected override string DialogPrompt => "Select a template or create custom instructions:";
        protected override string DialogDefaultValue => "copilot.instructions.md";
        protected override string ErrorMessagePrefix => "Failed to create instructions file";
        protected override TemplateType? TemplateType => Services.TemplateType.Instructions;
        protected override string RequiredExtension => ".instructions.md";
        protected override string SubfolderName => "instructions";

        protected override string GetFilePath(string targetFolder, string userInput)
            => BuildFilePath(targetFolder, userInput);

        protected override string GetFileContent(string userInput)
            => FileTemplates.CopilotInstructions;
    }
}
