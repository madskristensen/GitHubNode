using System.Windows;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to copy the path of a folder to the clipboard.
    /// </summary>
    [Command(PackageIds.CopyPathFolder)]
    internal sealed class CopyPathFolderCommand : BaseCommand<CopyPathFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFolderNode folder)
            {
                Clipboard.SetText(folder.FolderPath);
            }
        }
    }

    /// <summary>
    /// Command to copy the path of a file to the clipboard.
    /// </summary>
    [Command(PackageIds.CopyPathFile)]
    internal sealed class CopyPathFileCommand : BaseCommand<CopyPathFileCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFileNode file)
            {
                Clipboard.SetText(file.FilePath);
            }
        }
    }
}
