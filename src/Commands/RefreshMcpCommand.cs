using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to refresh the MCP Servers node.
    /// </summary>
    [Command(PackageIds.RefreshMcp)]
    internal sealed class RefreshMcpCommand : BaseCommand<RefreshMcpCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            if (McpContextMenuController.CurrentItem is McpRootNode rootNode)
            {
                rootNode.RefreshChildren();
            }
        }
    }
}
