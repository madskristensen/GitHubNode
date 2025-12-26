using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to rename a folder.
    /// </summary>
    [Command(PackageIds.RenameFolder)]
    internal sealed class RenameFolderCommand : BaseCommand<RenameFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFolderNode folder)
            {
                await RenameHelper.RenameFolderAsync(folder.FolderPath);
            }
        }
    }

    /// <summary>
    /// Command to rename a file.
    /// </summary>
    [Command(PackageIds.RenameFile)]
    internal sealed class RenameFileCommand : BaseCommand<RenameFileCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFileNode file)
            {
                await RenameHelper.RenameFileAsync(file.FilePath);
            }
        }
    }

    /// <summary>
    /// Helper class for rename operations.
    /// </summary>
    internal static class RenameHelper
    {
        public static async Task RenameFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return;
            }

            var currentName = Path.GetFileName(filePath);
            var dialog = new InputDialog("Rename File", "Enter the new file name:", currentName);

            if (dialog.ShowDialog() == true)
            {
                var newName = dialog.InputText?.Trim();
                if (string.IsNullOrEmpty(newName) || newName == currentName)
                {
                    return;
                }

                // Sanitize the new name
                newName = CommandHelpers.SanitizeFileName(newName);
                var directory = Path.GetDirectoryName(filePath);
                var newPath = Path.Combine(directory, newName);

                if (File.Exists(newPath))
                {
                    await VS.MessageBox.ShowWarningAsync("Rename Failed", $"A file named '{newName}' already exists.");
                    return;
                }

                try
                {
                    File.Move(filePath, newPath);
                }
                catch (Exception ex)
                {
                    await VS.MessageBox.ShowErrorAsync("Rename Failed", $"Could not rename the file: {ex.Message}");
                }
            }
        }

        public static async Task RenameFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            var currentName = Path.GetFileName(folderPath);
            var dialog = new InputDialog("Rename Folder", "Enter the new folder name:", currentName);

            if (dialog.ShowDialog() == true)
            {
                var newName = dialog.InputText?.Trim();
                if (string.IsNullOrEmpty(newName) || newName == currentName)
                {
                    return;
                }

                // Sanitize the new name
                newName = CommandHelpers.SanitizeFileName(newName);
                var parentDirectory = Path.GetDirectoryName(folderPath);
                var newPath = Path.Combine(parentDirectory, newName);

                if (Directory.Exists(newPath))
                {
                    await VS.MessageBox.ShowWarningAsync("Rename Failed", $"A folder named '{newName}' already exists.");
                    return;
                }

                try
                {
                    Directory.Move(folderPath, newPath);
                }
                catch (Exception ex)
                {
                    await VS.MessageBox.ShowErrorAsync("Rename Failed", $"Could not rename the folder: {ex.Message}");
                }
            }
        }
    }
}
