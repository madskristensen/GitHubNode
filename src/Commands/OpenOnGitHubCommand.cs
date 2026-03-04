using System.Diagnostics;
using System.ComponentModel;
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
                    await OpenOnGitHubHelper.OpenUrlAsync(url);
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
                    await OpenOnGitHubHelper.OpenUrlAsync(url);
                }
                else
                {
                    await VS.MessageBox.ShowWarningAsync("Could not determine GitHub URL for this folder.");
                }
            }
        }
    }

    internal static class OpenOnGitHubHelper
    {
        public static async Task OpenUrlAsync(string url)
        {
            if (string.IsNullOrWhiteSpace(url))
            {
                return;
            }

            try
            {
                Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
            }
            catch (Win32Exception ex)
            {
                await ex.LogAsync();
                await VS.MessageBox.ShowWarningAsync("Could not open the GitHub URL.");
            }
            catch (InvalidOperationException ex)
            {
                await ex.LogAsync();
                await VS.MessageBox.ShowWarningAsync("Could not open the GitHub URL.");
            }
        }
    }
}
