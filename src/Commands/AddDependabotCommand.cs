using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a dependabot.yml file.
    /// </summary>
    [Command(PackageIds.AddDependabot)]
    internal sealed class AddDependabotCommand : BaseCommand<AddDependabotCommand>
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

            var filePath = Path.Combine(gitHubFolder, "dependabot.yml");

            if (File.Exists(filePath))
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    "dependabot.yml already exists. Do you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(filePath);
                }
                return;
            }

            try
            {
                File.WriteAllText(filePath, FileTemplates.Dependabot);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create dependabot.yml: {ex.Message}");
            }
        }
    }
}
