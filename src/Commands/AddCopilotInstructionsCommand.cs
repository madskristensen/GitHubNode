using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a copilot-instructions.md file.
    /// </summary>
    [Command(PackageIds.AddCopilotInstructions)]
    internal sealed class AddCopilotInstructionsCommand : BaseCommand<AddCopilotInstructionsCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var folderPath = GitHubContextMenuController.CurrentFolderPath;
            if (string.IsNullOrEmpty(folderPath))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot determine target folder.");
                return;
            }

            var filePath = Path.Combine(folderPath, "copilot-instructions.md");

            if (File.Exists(filePath))
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    "copilot-instructions.md already exists. Do you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(filePath);
                }
                return;
            }

            try
            {
                File.WriteAllText(filePath, FileTemplates.CopilotInstructions);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create file: {ex.Message}");
            }
        }
    }
}
