using System.Collections.Generic;
using System.Linq;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Shell.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Handles context menu display for GitHub nodes.
    /// </summary>
    internal sealed class GitHubContextMenuController : IContextMenuController
    {
        private static GitHubContextMenuController _instance;

        /// <summary>
        /// Singleton instance.
        /// </summary>
        public static GitHubContextMenuController Instance =>
            _instance ??= new GitHubContextMenuController();

        /// <summary>
        /// Gets the currently selected item for command handlers.
        /// </summary>
        public static object CurrentItem { get; private set; }

        /// <summary>
        /// Gets the current folder path based on the selected item.
        /// </summary>
        public static string CurrentFolderPath
        {
            get
            {
                return CurrentItem switch
                {
                    GitHubRootNode root => root.GitHubFolderPath,
                    GitHubFolderNode folder => folder.FolderPath,
                    GitHubFileNode file => System.IO.Path.GetDirectoryName(file.FilePath),
                    _ => null
                };
            }
        }

        private GitHubContextMenuController() { }

        public bool ShowContextMenu(IEnumerable<object> items, Point location)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            var itemList = items.ToList();
            CurrentItem = itemList.FirstOrDefault();

            if (CurrentItem == null)
            {
                return false;
            }

            IVsUIShell shell = VS.GetRequiredService<SVsUIShell, IVsUIShell>();
            Guid guid = PackageGuids.GitHubNode;

            var menuId = GetMenuId(CurrentItem);
            if (menuId == 0)
            {
                return false;
            }

            var result = shell.ShowContextMenu(
                dwCompRole: 0,
                rclsidActive: ref guid,
                nMenuId: menuId,
                pos: [new POINTS { x = (short)location.X, y = (short)location.Y }],
                pCmdTrgtActive: null);

            return ErrorHandler.Succeeded(result);
        }

        private static int GetMenuId(object item)
        {
            return item switch
            {
                GitHubRootNode => PackageIds.GitHubRootContextMenu,
                GitHubFolderNode => PackageIds.GitHubFolderContextMenu,
                GitHubFileNode => PackageIds.GitHubFileContextMenu,
                _ => 0,
            };
        }
    }
}
