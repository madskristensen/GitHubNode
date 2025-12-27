using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a custom Copilot agent file.
    /// </summary>
    [Command(PackageIds.AddAgent)]
    internal sealed class AddAgentCommand : GitHubFileCommandBase<AddAgentCommand>
    {
        protected override string DialogTitle => "New Custom Agent";
        protected override string DialogPrompt => "Select a template or create a custom agent:";
        protected override string DialogDefaultValue => "my-agent.agent.md";
        protected override string ErrorMessagePrefix => "Failed to create agent";
        protected override TemplateType? TemplateType => Services.TemplateType.Agent;
        protected override string RequiredExtension => ".agent.md";
        protected override string SubfolderName => "agents";

        protected override string GetFilePath(string targetFolder, string userInput)
            => BuildFilePath(targetFolder, userInput);

        protected override string GetFileContent(string userInput)
            => string.Format(FileTemplates.CustomAgent, GetBaseName(userInput));
    }
}
