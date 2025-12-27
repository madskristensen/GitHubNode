using System.Diagnostics;
using GitHubNode.Services;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to open a file on GitHub.
    /// </summary>
    [Command(PackageIds.OpenOnGitHubFile)]
    internal sealed class OpenOnGitHubFileCommand : BaseCommand<OpenOnGitHubFileCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string path = null;

            if (GitHubContextMenuController.CurrentItem is GitHubFileNode file)
            {
                path = file.FilePath;
            }

            if (!string.IsNullOrEmpty(path))
            {
                var url = GitHubUrlService.GetGitHubUrl(path);
                if (!string.IsNullOrEmpty(url))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else
                {
                    await VS.MessageBox.ShowWarningAsync("Could not determine GitHub URL for this file.");
                }
            }
        }
    }

    /// <summary>
    /// Command to open a folder on GitHub.
    /// </summary>
    [Command(PackageIds.OpenOnGitHubFolder)]
    internal sealed class OpenOnGitHubFolderCommand : BaseCommand<OpenOnGitHubFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            string path = null;

            if (GitHubContextMenuController.CurrentItem is GitHubFolderNode folder)
            {
                path = folder.FolderPath;
            }
            else if (GitHubContextMenuController.CurrentItem is GitHubRootNode root)
            {
                path = root.GitHubFolderPath;
            }

            if (!string.IsNullOrEmpty(path))
            {
                var url = GitHubUrlService.GetGitHubUrl(path);
                if (!string.IsNullOrEmpty(url))
                {
                    Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
                }
                else
                {
                    await VS.MessageBox.ShowWarningAsync("Could not determine GitHub URL for this folder.");
                }
            }
        }
    }
}
