using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add an issue template.
    /// </summary>
    [Command(PackageIds.AddIssueTemplate)]
    internal sealed class AddIssueTemplateCommand : BaseCommand<AddIssueTemplateCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var basePath = GitHubContextMenuController.CurrentFolderPath;
            if (string.IsNullOrEmpty(basePath))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot determine target folder.");
                return;
            }

            var gitHubFolder = CommandHelpers.GetGitHubFolderPath(basePath);
            if (string.IsNullOrEmpty(gitHubFolder))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot find .github folder.");
                return;
            }

            // Prompt for template name
            var nameDialog = new InputDialog(
                "New Issue Template",
                "Enter the template name:",
                "Bug Report");

            if (nameDialog.ShowModal() != true || string.IsNullOrWhiteSpace(nameDialog.InputText))
            {
                return;
            }
            var templateName = nameDialog.InputText;

            // Create in ISSUE_TEMPLATE folder
            var templateFolder = Path.Combine(gitHubFolder, "ISSUE_TEMPLATE");
            Directory.CreateDirectory(templateFolder);

            var fileName = CommandHelpers.SanitizeFileName(templateName) + ".md";
            var filePath = Path.Combine(templateFolder, fileName);

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

            try
            {
                var content = string.Format(FileTemplates.IssueTemplate, templateName);
                File.WriteAllText(filePath, content);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create issue template: {ex.Message}");
            }
        }
    }
}
