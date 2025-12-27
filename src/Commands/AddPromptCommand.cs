using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a reusable prompt file.
    /// </summary>
    [Command(PackageIds.AddPrompt)]
    internal sealed class AddPromptCommand : GitHubFileCommandBase<AddPromptCommand>
    {
        protected override string DialogTitle => "New Prompt File";
        protected override string DialogPrompt => "Select a template or create a custom prompt:";
        protected override string DialogDefaultValue => "my-prompt.prompt.md";
        protected override string ErrorMessagePrefix => "Failed to create prompt file";
        protected override TemplateType? TemplateType => Services.TemplateType.Prompt;
        protected override string RequiredExtension => ".prompt.md";
        protected override string SubfolderName => "prompts";

        protected override string GetFilePath(string targetFolder, string userInput)
            => BuildFilePath(targetFolder, userInput);

        protected override string GetFileContent(string userInput)
            => string.Format(FileTemplates.PromptFile, GetBaseName(userInput));
    }
}
