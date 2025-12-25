using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Base class for commands that create files in the .github folder.
    /// Provides common functionality for path validation, file existence checking,
    /// and file creation with templates.
    /// </summary>
    internal abstract class GitHubFileCommandBase<T> : BaseCommand<T> where T : class, new()
    {
        /// <summary>
        /// Gets the title for the input dialog. Return null if no input is needed.
        /// </summary>
        protected virtual string DialogTitle => null;

        /// <summary>
        /// Gets the prompt text for the input dialog.
        /// </summary>
        protected virtual string DialogPrompt => "Enter the file name:";

        /// <summary>
        /// Gets the default value for the input dialog.
        /// </summary>
        protected virtual string DialogDefaultValue => "new-file";

        /// <summary>
        /// Gets the error message prefix for failure messages.
        /// </summary>
        protected virtual string ErrorMessagePrefix => "Failed to create file";

        /// <summary>
        /// When true, the command uses the .github folder path.
        /// When false, it uses the current folder path directly.
        /// </summary>
        protected virtual bool RequiresGitHubFolder => true;

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var basePath = GitHubContextMenuController.CurrentFolderPath;
            if (string.IsNullOrEmpty(basePath))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot determine target folder.");
                return;
            }

            string targetFolder;
            if (RequiresGitHubFolder)
            {
                var gitHubFolder = CommandHelpers.GetGitHubFolderPath(basePath);
                if (string.IsNullOrEmpty(gitHubFolder))
                {
                    await VS.MessageBox.ShowWarningAsync("Cannot find .github folder.");
                    return;
                }
                targetFolder = gitHubFolder;
            }
            else
            {
                targetFolder = basePath;
            }

            // Get user input if dialog is configured
            string userInput = null;
            if (DialogTitle != null)
            {
                var dialog = new InputDialog(DialogTitle, DialogPrompt, DialogDefaultValue);
                if (dialog.ShowModal() != true || string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    return;
                }
                userInput = dialog.InputText;

                // Allow subclasses to validate the input
                if (!await ValidateInputAsync(userInput))
                {
                    return;
                }
            }

            // Get the file path from the subclass
            var filePath = GetFilePath(targetFolder, userInput);
            var fileName = Path.GetFileName(filePath);

            // Ensure parent directory exists
            var parentDir = Path.GetDirectoryName(filePath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            // Check if file already exists
            if (File.Exists(filePath))
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    $"{fileName} already exists. Do you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(filePath);
                }
                return;
            }

            // Create the file
            try
            {
                var content = GetFileContent(userInput);
                File.WriteAllText(filePath, content);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"{ErrorMessagePrefix}: {ex.Message}");
            }
        }

        /// <summary>
        /// Validates the user input. Return false to cancel the command.
        /// </summary>
        protected virtual System.Threading.Tasks.Task<bool> ValidateInputAsync(string input) => System.Threading.Tasks.Task.FromResult(true);

        /// <summary>
        /// Gets the full file path where the file should be created.
        /// </summary>
        /// <param name="targetFolder">The .github folder path or current folder path.</param>
        /// <param name="userInput">The user input from the dialog, or null if no dialog was shown.</param>
        protected abstract string GetFilePath(string targetFolder, string userInput);

        /// <summary>
        /// Gets the content to write to the file.
        /// </summary>
        /// <param name="userInput">The user input from the dialog, or null if no dialog was shown.</param>
        protected abstract string GetFileContent(string userInput);
    }
}
