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
        /// <summary>
        /// Well-known folder names mapped to specific icons.
        /// </summary>
        private static readonly Dictionary<string, ImageMoniker> _knownFolderIcons = new(StringComparer.OrdinalIgnoreCase)
        {
            // GitHub Actions workflows
            ["Workflows"] = KnownMonikers.PublishWithGitHubActions,

            // Copilot customization folders (direct children of .github)
            ["Skills"] = KnownMonikers.ExtensionApplication,
            ["Prompts"] = KnownMonikers.Comment,
            ["Agents"] = KnownMonikers.Application,
            ["Instructions"] = KnownMonikers.DocumentOutline,

            // Issue and PR templates
            ["ISSUE_TEMPLATE"] = KnownMonikers.Bug,
            ["PULL_REQUEST_TEMPLATE"] = KnownMonikers.PullRequest,
        };

        private readonly ObservableCollection<object> _children;
        private readonly string _folderName;
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
        public string FolderPath { get; }

        // IAttachedCollectionSource
        public bool HasItems => _childrenManager.HasItems;
        public IEnumerable Items => _children;

        // ITreeDisplayItem
        public override string Text => _folderName;
        public override string ToolTipText => FolderPath;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => GetFolderIcon(_isExpanded);
        public ImageMoniker ExpandedIconMoniker => GetFolderIcon(expanded: true);
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        private ImageMoniker GetFolderIcon(bool expanded)
        {
            // Check for well-known folder names
            return _knownFolderIcons.TryGetValue(_folderName, out ImageMoniker knownIcon)
                ? knownIcon
                : expanded ? KnownMonikers.FolderOpened : KnownMonikers.FolderClosed;
        }

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
