using System;
using System.IO;
using System.Threading.Tasks;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Controls visibility of the Add GitHub File submenu.
    /// </summary>
    [Command(PackageIds.AddGitHubSubMenu)]
    internal sealed class AddGitHubSubMenuCommand : BaseCommand<AddGitHubSubMenuCommand>
    {
        protected override void BeforeQueryStatus(EventArgs e)
        {
            base.BeforeQueryStatus(e);

            string currentFolderPath = GitHubContextMenuController.CurrentFolderPath;
            string rootFolderPath = CommandHelpers.GetGitHubFolderPath(currentFolderPath);

            Command.Visible = !string.IsNullOrEmpty(rootFolderPath) &&
                string.Equals(Path.GetFileName(rootFolderPath), ".github", StringComparison.OrdinalIgnoreCase);
        }

        protected override Task ExecuteAsync(OleMenuCmdEventArgs e)
        {
            return Task.CompletedTask;
        }
    }
}
