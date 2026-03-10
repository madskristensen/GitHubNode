using System.Collections.Generic;
using System.Linq;
using System.Threading;
using GitHubNode.Services;
using GitHubNode.Services.Marketplace;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to install MCP servers from marketplace plugins.
    /// Shows a picker dialog to select which MCP server to install.
    /// </summary>
    [Command(PackageIds.InstallMcpServer)]
    internal sealed class InstallMcpServerCommand : BaseCommand<InstallMcpServerCommand>
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

            // Load MCP server assets from marketplaces
            List<PluginAsset> mcpAssets;
            try
            {
                mcpAssets = await MarketplaceService.GetAllAssetsAsync(AssetType.McpServer, CancellationToken.None);
            }
            catch (System.Exception ex)
            {
                await VS.MessageBox.ShowErrorAsync("Error", $"Failed to load MCP servers from marketplaces: {ex.Message}");
                return;
            }

            if (mcpAssets == null || mcpAssets.Count == 0)
            {
                await VS.MessageBox.ShowWarningAsync(
                    "No MCP Servers Found",
                    "No MCP servers were found in the configured marketplaces.\n\n" +
                    "You can add marketplaces via the GitHub node context menu.");
                return;
            }

            // Show the MCP server picker dialog
            var dialog = new InstallMcpServerDialog(mcpAssets, solutionDirectory);
            if (dialog.ShowDialog() != true || dialog.SelectedAsset == null)
            {
                return;
            }

            // Install the selected MCP server
            McpInstallResult result = McpInstallService.InstallFromMarketplace(
                dialog.SelectedAsset.LocalPath,
                solutionDirectory);

            if (result.Success)
            {
                await VS.MessageBox.ShowAsync(
                    "MCP Server Installed",
                    result.Message + $"\n\nConfiguration updated: {result.TargetFilePath}");

                // Open the configuration file
                await VS.Documents.OpenAsync(result.TargetFilePath);
            }
            else
            {
                await VS.MessageBox.ShowWarningAsync("Installation Failed", result.Message);
            }
        }
    }
}
