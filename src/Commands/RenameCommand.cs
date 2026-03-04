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
        internal static string BuildRenamedPath(string existingPath, string requestedName)
        {
            if (string.IsNullOrWhiteSpace(existingPath) || string.IsNullOrWhiteSpace(requestedName))
            {
                return null;
            }

            var sanitizedName = CommandHelpers.SanitizeFileName(requestedName.Trim());
            if (string.IsNullOrWhiteSpace(sanitizedName))
            {
                return null;
            }

            var parentDirectory = Path.GetDirectoryName(existingPath);
            return string.IsNullOrWhiteSpace(parentDirectory)
                ? sanitizedName
                : Path.Combine(parentDirectory, sanitizedName);
        }

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

                var newPath = BuildRenamedPath(filePath, newName);
                if (string.IsNullOrWhiteSpace(newPath))
                {
                    return;
                }

                newName = Path.GetFileName(newPath);

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
                    await ex.LogAsync();
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

                var newPath = BuildRenamedPath(folderPath, newName);
                if (string.IsNullOrWhiteSpace(newPath))
                {
                    return;
                }

                newName = Path.GetFileName(newPath);

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
                    await ex.LogAsync();
                    await VS.MessageBox.ShowErrorAsync("Rename Failed", $"Could not rename the folder: {ex.Message}");
                }
            }
        }
    }
}
