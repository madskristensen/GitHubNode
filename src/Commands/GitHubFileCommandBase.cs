using System.IO;
using GitHubNode.Services;
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

        /// <summary>
        /// Gets the template type for loading templates from awesome-copilot.
        /// Return null if templates are not supported for this command.
        /// </summary>
        protected virtual TemplateType? TemplateType => null;

        /// <summary>
        /// Gets the required file extension for this file type (e.g., ".agent.md", ".prompt.md").
        /// Return null if no specific extension is required.
        /// </summary>
        protected virtual string RequiredExtension => null;

        /// <summary>
        /// Gets the subfolder name within .github where files should be created (e.g., "agents", "prompts").
        /// Return null if files should be created directly in the target folder.
        /// </summary>
        protected virtual string SubfolderName => null;

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
            string selectedTemplateContent = null;
            if (DialogTitle != null)
            {
                // Create preview generator that uses the subclass's GetFileContent method
                Func<string, string> previewGenerator = GetFileContent;
                var dialog = new InputDialog(DialogTitle, DialogPrompt, DialogDefaultValue, previewGenerator, TemplateType);
                if (dialog.ShowModal() != true || string.IsNullOrWhiteSpace(dialog.InputText))
                {
                    return;
                }
                userInput = dialog.InputText;
                selectedTemplateContent = dialog.SelectedTemplateContent;

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
                // Use selected template content if available, otherwise use default content
                var content = selectedTemplateContent ?? GetFileContent(userInput);
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
        /// Default implementation validates against RequiredExtension if set.
        /// </summary>
        protected virtual async System.Threading.Tasks.Task<bool> ValidateInputAsync(string input)
        {
            if (!string.IsNullOrEmpty(RequiredExtension) &&
                !input.EndsWith(RequiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                await VS.MessageBox.ShowWarningAsync("Invalid File Name", $"File names must end with {RequiredExtension}");
                return false;
            }
            return true;
        }

        /// <summary>
        /// Gets the subfolder path for this command, creating it from SubfolderName if set.
        /// </summary>
        protected string GetSubfolderPath(string targetFolder)
        {
            return string.IsNullOrEmpty(SubfolderName)
                ? targetFolder
                : Path.Combine(targetFolder, SubfolderName);
        }

        /// <summary>
        /// Builds the file path using SubfolderName, sanitized input, and RequiredExtension.
        /// </summary>
        protected string BuildFilePath(string targetFolder, string userInput)
        {
            var subfolder = GetSubfolderPath(targetFolder);
            var baseName = GetBaseName(userInput);
            var fileName = CommandHelpers.SanitizeFileName(baseName) + (RequiredExtension ?? "");
            return Path.Combine(subfolder, fileName);
        }

        /// <summary>
        /// Extracts the base name from user input by stripping the required extension if present.
        /// </summary>
        protected string GetBaseName(string userInput)
        {
            if (string.IsNullOrEmpty(userInput))
                return userInput;

            // Strip the required extension if present
            if (!string.IsNullOrEmpty(RequiredExtension) &&
                userInput.EndsWith(RequiredExtension, StringComparison.OrdinalIgnoreCase))
            {
                return userInput.Substring(0, userInput.Length - RequiredExtension.Length);
            }

            // For double extensions like .agent.md, strip both parts
            var ext1 = Path.GetExtension(userInput);
            if (!string.IsNullOrEmpty(ext1))
            {
                var withoutExt1 = Path.GetFileNameWithoutExtension(userInput);
                var ext2 = Path.GetExtension(withoutExt1);
                if (!string.IsNullOrEmpty(ext2))
                {
                    return Path.GetFileNameWithoutExtension(withoutExt1);
                }
                return withoutExt1;
            }

            return userInput;
        }

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
