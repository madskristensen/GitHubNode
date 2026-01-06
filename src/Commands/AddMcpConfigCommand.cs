using System.IO;
using GitHubNode.Services;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to add a new MCP configuration file.
    /// Shows a picker dialog to select the location.
    /// </summary>
    [Command(PackageIds.AddMcpConfig)]
    internal sealed class AddMcpConfigCommand : BaseCommand<AddMcpConfigCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            // Get solution directory from the current MCP root node
            string solutionDirectory = null;
            if (McpContextMenuController.CurrentItem is McpRootNode rootNode)
            {
                solutionDirectory = rootNode.SolutionDirectory;
            }

            if (string.IsNullOrEmpty(solutionDirectory))
            {
                await VS.MessageBox.ShowErrorAsync("Error", "Could not determine solution directory.");
                return;
            }

            // Show the location picker dialog
            var dialog = new McpLocationPickerDialog(solutionDirectory);
            if (dialog.ShowDialog() != true || dialog.SelectedLocation == null)
            {
                return;
            }

            var location = dialog.SelectedLocation;

            // Check if file already exists
            if (location.Exists)
            {
                var result = await VS.MessageBox.ShowConfirmAsync(
                    "File Exists",
                    $"The configuration file already exists at:\n{location.FilePath}\n\nDo you want to open it?");

                if (result)
                {
                    await VS.Documents.OpenAsync(location.FilePath);
                }
                return;
            }

            // Create the configuration file
            if (McpConfigService.CreateConfigFile(location.FilePath))
            {
                await VS.Documents.OpenAsync(location.FilePath);
            }
            else
            {
                await VS.MessageBox.ShowErrorAsync(
                    "Error",
                    $"Failed to create the configuration file at:\n{location.FilePath}");
            }
        }
    }
}
