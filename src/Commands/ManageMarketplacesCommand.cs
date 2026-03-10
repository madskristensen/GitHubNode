namespace GitHubNode.Commands
{
    /// <summary>
    /// Command to open the Manage Marketplaces dialog.
    /// </summary>
    [Command(PackageIds.ManageMarketplaces)]
    internal sealed class ManageMarketplacesCommand : BaseCommand<ManageMarketplacesCommand>
    {
        protected override async Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

            var dialog = new ManageMarketplacesDialog();
            dialog.ShowModal();
        }
    }
}
