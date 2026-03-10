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
        private readonly AiRootFolderDefinition _rootDefinition;
        private readonly GitHubUserProfileNode _userProfileNode;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubRootNode(object parentItem, string gitHubFolderPath, AiRootFolderDefinition rootDefinition)
            : base(parentItem)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            _gitHubFolderPath = gitHubFolderPath;
            _rootDefinition = rootDefinition;
            _children = [];

            // Add User Profile node only for the .github root (not for .claude, .agents, etc.)
            if (rootDefinition == AiRootFolders.GitHub)
            {
                _userProfileNode = new GitHubUserProfileNode(this);
            }

            _childrenManager = new NodeChildrenManager(
                gitHubFolderPath,
                this,
                _children,
                () =>
                {
                    // Re-insert User Profile node at the beginning after NodeChildrenManager refreshes
                    EnsureUserProfileNodeFirst();
                    RaisePropertyChanged(nameof(HasItems));
                    RaisePropertyChanged(nameof(Items));
                },
                includeSubdirectories: false);

            _childrenManager.Initialize();

            // Ensure User Profile node is at the beginning after initial load
            EnsureUserProfileNodeFirst();
        }

        /// <summary>
        /// Ensures the User Profile node is the first child in the collection.
        /// Called after NodeChildrenManager refreshes children.
        /// </summary>
        private void EnsureUserProfileNodeFirst()
        {
            if (_userProfileNode == null)
            {
                return;
            }

            // Remove if already present (shouldn't happen but be safe)
            _children.Remove(_userProfileNode);

            // Insert at beginning
            _children.Insert(0, _userProfileNode);
        }

        /// <summary>
        /// Gets the User Profile node, if this is the GitHub root node.
        /// </summary>
        public GitHubUserProfileNode UserProfileNode => _userProfileNode;

        /// <summary>
        /// Gets the path to the .github folder.
        /// </summary>
        public string GitHubFolderPath => _gitHubFolderPath;

        // IAttachedCollectionSource
        // Always has items if User Profile node exists, or if file system has items
        public bool HasItems => _userProfileNode != null || _childrenManager.HasItems;
        public IEnumerable Items => _children;

        /// <summary>
        /// Gets the children for search enumeration without modifying the tree.
        /// This enumerates the file system directly for unexpanded folders to allow
        /// search to find items without requiring tree expansion.
        /// </summary>
        public IEnumerable<GitHubNodeBase> GetChildrenForSearch()
        {
            var results = new List<GitHubNodeBase>();

            // Include User Profile node if present
            if (_userProfileNode != null)
            {
                results.Add(_userProfileNode);
            }

            // If children have already been loaded (tree was expanded), return them
            if (_children.Count > (_userProfileNode != null ? 1 : 0))
            {
                results.AddRange(_children.OfType<GitHubNodeBase>().Where(n => n != _userProfileNode));
                return results;
            }

            // For unexpanded tree, enumerate file system directly without modifying _children
            if (!Directory.Exists(_gitHubFolderPath))
            {
                return results;
            }

            results.AddRange(EnumerateChildrenForSearch());
            return results;
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
        public override string Text => _rootDefinition.DisplayName;
        public override string ToolTipText => _gitHubFolderPath;
        public override System.Windows.FontWeight FontWeight => System.Windows.FontWeights.SemiBold;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => _rootDefinition.IconMoniker;
        public ImageMoniker ExpandedIconMoniker => _rootDefinition.IconMoniker;
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
            _userProfileNode?.Dispose();
            _childrenManager.Dispose();
        }
    }
}
