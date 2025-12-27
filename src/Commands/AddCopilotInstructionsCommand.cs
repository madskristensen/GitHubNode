using System.IO;
using GitHubNode.Services;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a Copilot instructions file in the Instructions folder.
    /// </summary>
    [Command(PackageIds.AddCopilotInstructions)]
    internal sealed class AddCopilotInstructionsCommand : GitHubFileCommandBase<AddCopilotInstructionsCommand>
    {
        protected override string DialogTitle => "New Copilot Instructions";
        protected override string DialogPrompt => "Select a template or create custom instructions:";
        protected override string DialogDefaultValue => "copilot.instructions.md";
        protected override string ErrorMessagePrefix => "Failed to create instructions file";
        protected override TemplateType? TemplateType => Services.TemplateType.Instructions;

        protected override async System.Threading.Tasks.Task<bool> ValidateInputAsync(string input)
        {
            if (!input.EndsWith(".instructions.md", StringComparison.OrdinalIgnoreCase))
            {
                await VS.MessageBox.ShowWarningAsync("Invalid File Name", "Instructions file names must end with .instructions.md");
                return false;
            }
            return true;
        }

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var instructionsFolder = Path.Combine(targetFolder, "instructions");
            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(userInput));
            var fileName = CommandHelpers.SanitizeFileName(baseName) + ".instructions.md";
            return Path.Combine(instructionsFolder, fileName);
        }

        protected override string GetFileContent(string userInput)
        {
            return FileTemplates.CopilotInstructions;
        }
    }
}
