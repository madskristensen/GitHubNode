using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
using System.Linq;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// The root "GitHub" node shown as a child under the solution in Solution Explorer.
    /// Represents the .github folder (or potential .github folder) in the repository.
    /// </summary>
    internal sealed class GitHubRootNode :
        GitHubNodeBase,
        IAttachedCollectionSource,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IContextMenuPattern
    {
        private readonly ObservableCollection<object> _children;
        private readonly string _gitHubFolderPath;
        private readonly NodeChildrenManager _childrenManager;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubRootNode(object parentItem, string gitHubFolderPath)
            : base(parentItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _gitHubFolderPath = gitHubFolderPath;
            _children = [];
            _childrenManager = new NodeChildrenManager(
                gitHubFolderPath,
                this,
                _children,
                () =>
                {
                    RaisePropertyChanged(nameof(HasItems));
                    RaisePropertyChanged(nameof(Items));
                },
                includeSubdirectories: false);

            _childrenManager.Initialize();
        }

        /// <summary>
        /// Gets the path to the .github folder.
        /// </summary>
        public string GitHubFolderPath => _gitHubFolderPath;

        // IAttachedCollectionSource
        public bool HasItems => _childrenManager.HasItems;
        public IEnumerable Items => _children;

        /// <summary>
        /// Gets the children for search enumeration without modifying the tree.
        /// This enumerates the file system directly for unexpanded folders to allow
        /// search to find items without requiring tree expansion.
        /// </summary>
        public IEnumerable<GitHubNodeBase> GetChildrenForSearch()
        {
            // If children have already been loaded (tree was expanded), return them
            if (_children.Count > 0)
            {
                return _children.OfType<GitHubNodeBase>().ToList();
            }

            // For unexpanded tree, enumerate file system directly without modifying _children
            if (!Directory.Exists(_gitHubFolderPath))
            {
                return Enumerable.Empty<GitHubNodeBase>();
            }

            return EnumerateChildrenForSearch();
        }

        /// <summary>
        /// Lazily enumerates children for search using yield return to avoid allocating a full list.
        /// </summary>
        private IEnumerable<GitHubNodeBase> EnumerateChildrenForSearch()
        {
            string[] directories;
            string[] files;

            try
            {
                directories = Directory.GetDirectories(_gitHubFolderPath);
                files = Directory.GetFiles(_gitHubFolderPath);
            }
            catch (UnauthorizedAccessException)
            {
                yield break;
            }
            catch (DirectoryNotFoundException)
            {
                yield break;
            }
            catch (IOException)
            {
                yield break;
            }

            // Return folders first
            foreach (var dir in directories)
            {
                GitHubFolderNode node;
                try
                {
                    // Create lightweight node without FileSystemWatcher for search
                    node = new GitHubFolderNode(dir, this, forSearchOnly: true);
                }
                catch
                {
                    continue;
                }
                yield return node;
            }

            // Then files
            foreach (var file in files)
            {
                GitHubFileNode node;
                try
                {
                    // Create lightweight node without git status loading for search
                    node = new GitHubFileNode(file, this, forSearchOnly: true);
                }
                catch
                {
                    continue;
                }
                yield return node;
            }
        }

        // ITreeDisplayItem
        public override string Text => "GitHub";
        public override string ToolTipText => _gitHubFolderPath;
        public override System.Windows.FontWeight FontWeight => System.Windows.FontWeights.SemiBold;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => KnownMonikers.GitHub;
        public ImageMoniker ExpandedIconMoniker => KnownMonikers.GitHub;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        // IPrioritizedComparable - Priority 0 to appear near the top but after solution items
        public int Priority => 0;

        public int CompareTo(object obj)
        {
            if (obj is IPrioritizedComparable other)
            {
                return Priority.CompareTo(other.Priority);
            }
            return -1;
        }

        // IContextMenuPattern
        public IContextMenuController ContextMenuController => GitHubContextMenuController.Instance;

        protected override void OnDisposing()
        {
            _childrenManager.Dispose();
        }
    }
}
