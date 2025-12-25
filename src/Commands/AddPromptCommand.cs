using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a reusable prompt file.
    /// </summary>
    [Command(PackageIds.AddPrompt)]
    internal sealed class AddPromptCommand : BaseCommand<AddPromptCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var basePath = GitHubContextMenuController.CurrentFolderPath;
            if (string.IsNullOrEmpty(basePath))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot determine target folder.");
                return;
            }

            var gitHubFolder = CommandHelpers.GetGitHubFolderPath(basePath);
            if (string.IsNullOrEmpty(gitHubFolder))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot find .github folder.");
                return;
            }

            // Prompt for prompt name
            var nameDialog = new InputDialog(
                "New Prompt File",
                "Enter the prompt file name (must end with .prompt.md):",
                "my-prompt.prompt.md");

            if (nameDialog.ShowModal() != true || string.IsNullOrWhiteSpace(nameDialog.InputText))
            {
                return;
            }

            var promptName = nameDialog.InputText;
            
            // Validate that the file name ends with .prompt.md
            if (!promptName.EndsWith(".prompt.md", StringComparison.OrdinalIgnoreCase))
            {
                await VS.MessageBox.ShowWarningAsync("Invalid File Name", "Prompt file names must end with .prompt.md");
                return;
            }

            // Create in Prompts folder directly under .github
            var promptsFolder = Path.Combine(gitHubFolder, "Prompts");
            Directory.CreateDirectory(promptsFolder);

            var fileName = CommandHelpers.SanitizeFileName(Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(promptName))) + ".prompt.md";
            var filePath = Path.Combine(promptsFolder, fileName);

            if (File.Exists(filePath))
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    $"{fileName} already exists. Do you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(filePath);
                }
                return;
            }

            try
            {
                var baseName = Path.GetFileNameWithoutExtension(Path.GetFileNameWithoutExtension(fileName));
                var content = string.Format(FileTemplates.PromptFile, baseName);
                File.WriteAllText(filePath, content);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create prompt file: {ex.Message}");
            }
        }
    }
}
