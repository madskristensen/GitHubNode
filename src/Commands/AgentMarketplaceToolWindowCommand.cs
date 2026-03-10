using GitHubNode.ToolWindows;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to open the Agent Marketplace tool window.
    /// </summary>
    [Command(PackageIds.AgentMarketplace)]
    internal sealed class AgentMarketplaceToolWindowCommand : BaseCommand<AgentMarketplaceToolWindowCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await AgentMarketplaceToolWindow.ShowAsync();
        }
    }
}
