using System.Collections;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.IO;
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
        private readonly NodeChildrenManager _childrenManager;
        private bool _isExpanded;

        protected override HashSet<Type> SupportedPatterns { get; } =
        [
            typeof(ITreeDisplayItem),
            typeof(IBrowsablePattern),
            typeof(IContextMenuPattern),
            typeof(ISupportDisposalNotification),
        ];

        public GitHubFolderNode(string folderPath, object parent)
            : base(parent)
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            FolderPath = folderPath;
            _folderName = Path.GetFileName(folderPath);
            _children = [];
            _childrenManager = new NodeChildrenManager(
                folderPath,
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
        /// Gets the full path to this folder.
        /// </summary>
        public string FolderPath { get; private set; }

        /// <summary>
        /// Updates the folder path after a rename operation.
        /// </summary>
        public void UpdatePath(string newPath)
        {
            FolderPath = newPath;
            _folderName = Path.GetFileName(newPath);
            RaisePropertyChanged(nameof(Text));
            RaisePropertyChanged(nameof(ToolTipText));
        }

        // IAttachedCollectionSource
        public bool HasItems => _childrenManager.HasItems;
        public IEnumerable Items => _children;

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
            _childrenManager.Dispose();
        }
    }
}
