using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a custom Copilot agent file.
    /// </summary>
    [Command(PackageIds.AddAgent)]
    internal sealed class AddAgentCommand : BaseCommand<AddAgentCommand>
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

            // Get the .github folder path
            var gitHubFolder = CommandHelpers.GetGitHubFolderPath(basePath);
            if (string.IsNullOrEmpty(gitHubFolder))
            {
                await VS.MessageBox.ShowWarningAsync("Cannot find .github folder.");
                return;
            }

            // Prompt for agent name
            var nameDialog = new InputDialog(
                "New Custom Agent",
                "Enter the agent name:",
                "my-agent");

            if (nameDialog.ShowModal() != true || string.IsNullOrWhiteSpace(nameDialog.InputText))
            {
                return;
            }
            var agentName = nameDialog.InputText;

            // Sanitize filename and create in Agents folder directly under .github
            var agentsFolder = Path.Combine(gitHubFolder, "Agents");
            Directory.CreateDirectory(agentsFolder);

            var fileName = CommandHelpers.SanitizeFileName(agentName) + ".agent.md";
            var filePath = Path.Combine(agentsFolder, fileName);

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
                var content = string.Format(FileTemplates.CustomAgent, agentName);
                File.WriteAllText(filePath, content);
                await VS.Documents.OpenAsync(filePath);
            }
            catch (Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to create agent: {ex.Message}");
            }
        }
    }
}
