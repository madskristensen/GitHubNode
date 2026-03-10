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
        protected override void BeforeQueryStatus(EventArgs e)
        {
            base.BeforeQueryStatus(e);

            // Hide for items under User Profile node (not in a git repo)
            Command.Visible = !IsUnderUserProfileNode();
        }

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

        private static bool IsUnderUserProfileNode()
        {
            var current = GitHubContextMenuController.CurrentItem;
            while (current != null)
            {
                if (current is GitHubUserProfileNode)
                {
                    return true;
                }

                if (current is GitHubNodeBase node)
                {
                    current = node.ParentItem;
                }
                else
                {
                    break;
                }
            }
            return false;
        }
    }

    /// <summary>
    /// Command to open a folder on GitHub.
    /// </summary>
    [Command(PackageIds.OpenOnGitHubFolder)]
    internal sealed class OpenOnGitHubFolderCommand : BaseCommand<OpenOnGitHubFolderCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            base.BeforeQueryStatus(e);

            // Hide for User Profile node and items under it (not in a git repo)
            Command.Visible = !IsUnderUserProfileNode();
        }

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

        private static bool IsUnderUserProfileNode()
        {
            var current = GitHubContextMenuController.CurrentItem;
            while (current != null)
            {
                if (current is GitHubUserProfileNode)
                {
                    return true;
                }

                if (current is GitHubNodeBase node)
                {
                    current = node.ParentItem;
                }
                else
                {
                    break;
                }
            }
            return false;
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
