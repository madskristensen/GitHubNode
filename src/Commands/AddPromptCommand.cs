using System.IO;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a reusable prompt file.
    /// </summary>
    [Command(PackageIds.AddPrompt)]
    internal sealed class AddPromptCommand : GitHubFileCommandBase<AddPromptCommand>
    {
        protected override string DialogTitle => "New Prompt File";
        protected override string DialogPrompt => "Enter the prompt file name (must end with .prompt.md):";
        protected override string DialogDefaultValue => "my-prompt.prompt.md";
        protected override string ErrorMessagePrefix => "Failed to create prompt file";

        protected override async System.Threading.Tasks.Task<bool> ValidateInputAsync(string input)
        {
            if (!input.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
            {
                await VS.MessageBox.ShowWarningAsync("Invalid File Name", "Prompt file names must end with .prompt.md");
                return false;
            }
            return true;
        }

        protected override string GetFilePath(string targetFolder, string userInput)
        {
            var promptsFolder = Path.Combine(targetFolder, "Prompts");
            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(userInput));
            var fileName = CommandHelpers.SanitizeFileName(baseName) + ".prompt.md";
            return Path.Combine(promptsFolder, fileName);
        }

        protected override string GetFileContent(string userInput)
        {
            var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(userInput));
            return string.Format(FileTemplates.PromptFile, baseName);
        }
    }
}
