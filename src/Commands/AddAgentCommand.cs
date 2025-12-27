using System.IO;
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

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var agentsFolder = Path.Combine(targetFolder, "agents");
            // User input is already a complete filename (e.g., my-agent.agent.md)
            var fileName = CommandHelpers.SanitizeFileName(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(userInput))) + ".agent.md";
            return Path.Combine(agentsFolder, fileName);
        }

        protected override string GetFileContent(string userInput)
        {
            return string.Format(FileTemplates.CustomAgent, userInput);
        }
    }
}
