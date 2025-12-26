using System.IO;
using GitHubNode.SolutionExplorer;
using Microsoft.VisualStudio.Shell.Interop;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to delete a folder.
    /// </summary>
    [Command(PackageIds.DeleteFolder)]
    internal sealed class DeleteFolderCommand : BaseCommand<DeleteFolderCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFolderNode folder)
            {
                await DeleteHelper.DeleteFolderAsync(folder.FolderPath);
            }
        }
    }

    /// <summary>
    /// Command to delete a file.
    /// </summary>
    [Command(PackageIds.DeleteFile)]
    internal sealed class DeleteFileCommand : BaseCommand<DeleteFileCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (GitHubContextMenuController.CurrentItem is GitHubFileNode file)
            {
                await DeleteHelper.DeleteFileAsync(file.FilePath);
            }
        }
    }

    /// <summary>
    /// Helper class for delete operations.
    /// </summary>
    internal static class DeleteHelper
    {
        public static async Task DeleteFileAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return;
            }

            var fileName = Path.GetFileName(filePath);
            var result = await VS.MessageBox.ShowConfirmAsync(
                "Delete File",
                $"Are you sure you want to delete '{fileName}'?\n\nThis action cannot be undone.");

            if (result)
            {
                try
                {
                    File.Delete(filePath);
                }
                catch (Exception ex)
                {
                    await VS.MessageBox.ShowErrorAsync("Delete Failed", $"Could not delete the file: {ex.Message}");
                }
            }
        }

        public static async Task DeleteFolderAsync(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return;
            }

            var folderName = Path.GetFileName(folderPath);
            var fileCount = Directory.GetFiles(folderPath, "*", SearchOption.AllDirectories).Length;
            var message = fileCount > 0
                ? $"Are you sure you want to delete '{folderName}' and its {fileCount} file(s)?\n\nThis action cannot be undone."
                : $"Are you sure you want to delete the empty folder '{folderName}'?\n\nThis action cannot be undone.";

            var result = await VS.MessageBox.ShowConfirmAsync("Delete Folder", message);

            if (result)
            {
                try
                {
                    Directory.Delete(folderPath, recursive: true);
                }
                catch (Exception ex)
                {
                    await VS.MessageBox.ShowErrorAsync("Delete Failed", $"Could not delete the folder: {ex.Message}");
                }
            }
        }
    }
}
