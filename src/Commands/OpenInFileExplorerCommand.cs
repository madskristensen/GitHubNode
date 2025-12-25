using System.Diagnostics;
using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to open the .github folder in Windows File Explorer (from root node).
    /// </summary>
    [Command(PackageIds.OpenInFileExplorer)]
    internal sealed class OpenInFileExplorerCommand : BaseCommand<OpenInFileExplorerCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubRootNode root)
            {
                OpenFolderInExplorer(root.GitHubFolderPath);
            }
        }

        internal static void OpenFolderInExplorer(string path)
        {
            if (!string.IsNullOrEmpty(path) && Directory.Exists(path))
            {
                Process.Start("explorer.exe", $"\"{path}\"");
            }
        }

        internal static void SelectFileInExplorer(string filePath)
        {
            if (!string.IsNullOrEmpty(filePath) && File.Exists(filePath))
            {
                Process.Start("explorer.exe", $"/select,\"{filePath}\"");
            }
        }
    }

    /// <summary>
    /// Command to open a subfolder in Windows File Explorer.
    /// </summary>
    [Command(PackageIds.OpenInFileExplorerFolder)]
    internal sealed class OpenInFileExplorerFolderCommand : BaseCommand<OpenInFileExplorerFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFolderNode folder)
            {
                OpenInFileExplorerCommand.OpenFolderInExplorer(folder.FolderPath);
            }
        }
    }

    /// <summary>
    /// Command to open the containing folder and select a file in Windows File Explorer.
    /// </summary>
    [Command(PackageIds.OpenContainingFolder)]
    internal sealed class OpenContainingFolderCommand : BaseCommand<OpenContainingFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFileNode file)
            {
                OpenInFileExplorerCommand.SelectFileInExplorer(file.FilePath);
            }
        }
    }
}
