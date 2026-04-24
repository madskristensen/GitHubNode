using GitHubNode.Services;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to toggle visibility of the GitHub node.
    /// </summary>
    [Command(PackageIds.ToggleGitHubNode)]
    internal sealed class ToggleGitHubNodeCommand : BaseCommand<ToggleGitHubNodeCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            base.BeforeQueryStatus(e);
            Command.Checked = McpSettingsService.IsGitHubNodeEnabled();
        }

        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var isEnabled = McpSettingsService.IsGitHubNodeEnabled();
            McpSettingsService.SetGitHubNodeEnabled(!isEnabled);

            GitHubSourceProvider.Instance?.UpdateVisibility();
        }
    }
}
