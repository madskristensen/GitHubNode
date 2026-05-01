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
    /// Represents a subfolder within the .github folder in Solution Explorer.
    /// </summary>
    internal sealed class GitHubFolderNode :
        GitHubNodeBase,
        IAttachedCollectionSource,
        ITreeDisplayItemWithImages,
        IPrioritizedComparable,
        IContextMenuPattern
    {

        private readonly ObservableCollection<object> _children;
        private string _folderName;
        private NodeChildrenManager _childrenManager;
        private bool _isExpanded;
        private bool _initialized;
        private bool _initializeOnDemand;
        private bool? _hasFileSystemItems;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubFolderNode(string folderPath, object parent)
            : this(folderPath, parent, forSearchOnly: false)
        {
        }

        /// <summary>
        /// Creates a GitHubFolderNode, optionally as a lightweight search-only node.
        /// </summary>
        /// <param name="folderPath">The folder path.</param>
        /// <param name="parent">The parent node.</param>
        /// <param name="forSearchOnly">If true, skips creating FileSystemWatcher (for search-only nodes).</param>
        internal GitHubFolderNode(string folderPath, object parent, bool forSearchOnly)
            : base(parent)
        {
            FolderPath = folderPath;
            _folderName = Path.GetFileName(folderPath);
            _children = [];

            // Skip heavy initialization for search-only nodes to avoid memory leaks.
            // For full nodes, defer initialization until the folder is first expanded
            // (i.e. until Items is enumerated by Solution Explorer) so a solution open
            // does not recursively walk the entire subtree on the UI thread.
            if (!forSearchOnly)
            {
                _initializeOnDemand = true;
            }
        }

        private void EnsureInitialized()
        {
            if (_initialized || !_initializeOnDemand)
            {
                return;
            }

            ThreadHelper.ThrowIfNotOnUIThread();

            _childrenManager = new NodeChildrenManager(
                FolderPath,
                this,
                _children,
                () =>
                {
                    _hasFileSystemItems = null;
                    RaisePropertyChanged(nameof(HasItems));
                    RaisePropertyChanged(nameof(Items));
                },
                includeSubdirectories: false);

            _initialized = true;
            _childrenManager.Initialize();
        }

        /// <summary>
        /// Gets the full path to this folder.
        /// </summary>
        public string FolderPath { get; private set; }

        /// <summary>
        /// Updates the folder path after a rename operation.
        /// </summary>
        public void UpdatePath(string newPath)
        {
            if (string.IsNullOrEmpty(newPath) ||
                string.Equals(FolderPath, newPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            FolderPath = newPath;
            _folderName = Path.GetFileName(newPath);
            _hasFileSystemItems = null;
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(ToolTipText));

            // Cascade the new path to children and re-bind the file system watcher
            // so file invocations resolve against the current on-disk location.
            _childrenManager?.UpdatePath(newPath);
        }

        // IAttachedCollectionSource
        // For search-only nodes, assume folders have items (they won't be expanded anyway).
        // For deferred nodes, do a cheap file-system probe so Solution Explorer can draw the
        // expander without us having to materialize the whole subtree.
        public bool HasItems
        {
            get
            {
                if (_initialized)
                {
                    return _children.Count > 0;
                }

                if (!_initializeOnDemand)
                {
                    return true;
                }

                if (!_hasFileSystemItems.HasValue)
                {
                    _hasFileSystemItems = ProbeHasFileSystemItems(FolderPath);
                }

                return _hasFileSystemItems.Value;
            }
        }

        public IEnumerable Items
        {
            get
            {
                EnsureInitialized();
                return _children;
            }
        }

        private static bool ProbeHasFileSystemItems(string folderPath)
        {
            if (string.IsNullOrEmpty(folderPath) || !Directory.Exists(folderPath))
            {
                return false;
            }

            try
            {
                return Directory.EnumerateFileSystemEntries(folderPath).Any();
            }
            catch (UnauthorizedAccessException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (IOException)
            {
                return false;
            }
        }

        /// <summary>
        /// Gets the children for search enumeration without modifying the tree.
        /// This enumerates the file system directly for unexpanded folders to allow
        /// search to find items without requiring tree expansion.
        /// </summary>
        public IEnumerable<GitHubNodeBase> GetChildrenForSearch()
        {
            // If children have already been loaded (folder was expanded), return them
            if (_children.Count > 0)
            {
                return _children.OfType<GitHubNodeBase>().ToList();
            }

            // For unexpanded folders, enumerate file system directly without modifying _children
            if (!Directory.Exists(FolderPath))
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
                directories = Directory.GetDirectories(FolderPath);
                files = Directory.GetFiles(FolderPath);
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
        public override string Text => _folderName;
        public override string ToolTipText => FolderPath;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => KnownMonikers.FolderClosed;
        public ImageMoniker ExpandedIconMoniker => KnownMonikers.FolderOpened;
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        // IPrioritizedComparable - Folders appear before files
        public int Priority => 0;

        public int CompareTo(object obj)
        {
            if (obj is GitHubFolderNode otherFolder)
            {
                return StringComparer.OrdinalIgnoreCase.Compare(Text, otherFolder.Text);
            }
            if (obj is GitHubFileNode)
            {
                return -1; // Folders before files
            }
            return 0;
        }

        // IContextMenuPattern
        public IContextMenuController ContextMenuController => GitHubContextMenuController.Instance;

        /// <summary>
        /// Updates the expanded state for icon changes.
        /// </summary>
        public void SetExpanded(bool expanded)
        {
            if (_isExpanded != expanded)
            {
                _isExpanded = expanded;
                RaisePropertyChanged(nameof(IconMoniker));
            }
        }

        protected override void OnDisposing()
        {
            _childrenManager?.Dispose();
        }
    }
}
