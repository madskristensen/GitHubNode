using GitHubNode.Services;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to toggle visibility of the MCP Servers node.
    /// </summary>
    [Command(PackageIds.ToggleMcpServers)]
    internal sealed class ToggleMcpServersCommand : BaseCommand<ToggleMcpServersCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            base.BeforeQueryStatus(e);
            Command.Checked = McpSettingsService.IsMcpServersEnabled();
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var isEnabled = McpSettingsService.IsMcpServersEnabled();
            McpSettingsService.SetMcpServersEnabled(!isEnabled);

            McpSourceProvider.Instance?.UpdateVisibility();
        }
    }
}
