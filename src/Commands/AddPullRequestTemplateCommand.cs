using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a pull request template.
    /// </summary>
    [Command(PackageIds.AddPullRequestTemplate)]
    internal sealed class AddPullRequestTemplateCommand : BaseCommand<AddPullRequestTemplateCommand>
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

            var filePath = Path.Combine(gitHubFolder, "PULL_REQUEST_TEMPLATE.md");

            if (File.Exists(filePath))
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    "PULL_REQUEST_TEMPLATE.md already exists. Do you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(filePath);
                }
                return;
            }

            try
            {
                File.WriteAllText(filePath, FileTemplates.PullRequestTemplate);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create PR template: {ex.Message}");
            }
        }
    }
}
