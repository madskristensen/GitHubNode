using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a copilot-instructions.md file.
    /// </summary>
    [Command(PackageIds.AddCopilotInstructions)]
    internal sealed class AddCopilotInstructionsCommand : GitHubFileCommandBase<AddCopilotInstructionsCommand>
    {
        // No dialog needed - fixed file name
        protected override string DialogTitle => null;
        protected override string ErrorMessagePrefix => "Failed to create file";

        // This command creates the file in the current folder, not .github
        protected override bool RequiresGitHubFolder => false;

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            return Path.Combine(targetFolder, "copilot-instructions.md");
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.CopilotInstructions;
        }
    }
}
