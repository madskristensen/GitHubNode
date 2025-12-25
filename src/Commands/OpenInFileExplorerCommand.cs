using System.Diagnostics;
using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to open the current folder in Windows File Explorer.
    /// </summary>
    [Command(PackageIds.OpenInFileExplorer)]
    internal sealed class OpenInFileExplorerCommand : BaseCommand<OpenInFileExplorerCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var item = GitHubContextMenuController.CurrentItem;
            string pathToOpen = null;

            switch (item)
            {
                case GitHubRootNode root:
                    pathToOpen = root.GitHubFolderPath;
                    break;
                case GitHubFolderNode folder:
                    pathToOpen = folder.FolderPath;
                    break;
                case GitHubFileNode file:
                    // Select the file in explorer
                    if (File.Exists(file.FilePath))
                    {
                        Process.Start("explorer.exe", $"/select,\"{file.FilePath}\"");
                        return;
                    }
                    pathToOpen = Path.GetDirectoryName(file.FilePath);
                    break;
            }

            if (!string.IsNullOrEmpty(pathToOpen) && Directory.Exists(pathToOpen))
            {
                Process.Start("explorer.exe", $"\"{pathToOpen}\"");
            }
            else
            {
                await VS.MessageBox.ShowWarningAsync("Folder not found", "The folder could not be located.");
            }
        }
    }
}
