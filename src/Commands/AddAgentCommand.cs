using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a custom Copilot agent file.
    /// </summary>
    [Command(PackageIds.AddAgent)]
    internal sealed class AddAgentCommand : GitHubFileCommandBase<AddAgentCommand>
    {
        protected override string DialogTitle => "New Custom Agent";
        protected override string DialogPrompt => "Enter the agent name:";
        protected override string DialogDefaultValue => "my-agent";
        protected override string ErrorMessagePrefix => "Failed to create agent";

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var agentsFolder = Path.Combine(targetFolder, "Agents");
            var fileName = CommandHelpers.SanitizeFileName(userInput) + ".agent.md";
            return Path.Combine(agentsFolder, fileName);
        }

        protected override string GetFileContent(string userInput)
        {
            return string.Format(FileTemplates.CustomAgent, userInput);
        }
    }
}
