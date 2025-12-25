using System.IO;
using System.Linq;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a GitHub Actions workflow file.
    /// </summary>
    [Command(PackageIds.AddWorkflow)]
    internal sealed class AddWorkflowCommand : BaseCommand<AddWorkflowCommand>
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

            // Ensure Workflows folder exists
            var workflowsFolder = Path.Combine(gitHubFolder, "Workflows");
            Directory.CreateDirectory(workflowsFolder);

            // Prompt for workflow name
            var nameDialog = new InputDialog(
                "New GitHub Actions Workflow",
                "Enter the workflow name:",
                "build");

            if (nameDialog.ShowModal() != true || string.IsNullOrWhiteSpace(nameDialog.InputText))
            {
                return;
            }
            var workflowName = nameDialog.InputText;

            // Sanitize filename
            var fileName = CommandHelpers.SanitizeFileName(workflowName) + ".yml";
            var filePath = Path.Combine(workflowsFolder, fileName);

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
                var content = string.Format(FileTemplates.GitHubActionsWorkflow, workflowName);
                File.WriteAllText(filePath, content);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create workflow: {ex.Message}");
            }
        }
    }
}
