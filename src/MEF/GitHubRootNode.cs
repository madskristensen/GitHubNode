using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using Microsoft.Internal.VisualStudio.PlatformUI;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// The root "GitHub" node shown as a child under the solution in Solution Explorer.
    /// Represents the .github folder that exists in the repository.
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

        public GitHubRootNode(object sourceItem, string gitHubFolderPath)
            : base(sourceItem)
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
                includeSubdirectories: true);

            _childrenManager.Initialize();
        }

        /// <summary>
        /// Gets the path to the .github folder.
        /// </summary>
        public string GitHubFolderPath => _gitHubFolderPath;

        // IAttachedCollectionSource
        public bool HasItems => _childrenManager.HasItems;
        public IEnumerable Items => _children;

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
