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
            ["Workflows"] = KnownMonikers.Run,
            
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
        private readonly string _folderPath;
        private readonly string _folderName;
        private bool _isExpanded;
        private FileSystemWatcher _watcher;

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
            _folderPath = folderPath;
            _folderName = Path.GetFileName(folderPath);
            _children = [];

            RefreshChildren();
            SetupFileWatcher();
        }

        /// <summary>
        /// Gets the full path to this folder.
        /// </summary>
        public string FolderPath => _folderPath;

        private void SetupFileWatcher()
        {
            if (!Directory.Exists(_folderPath))
                return;

            _watcher = new FileSystemWatcher(_folderPath)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName | NotifyFilters.LastWrite
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemChanged;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();
                RefreshChildren();
            }).FireAndForget();
        }

        private void RefreshChildren()
        {
            ThreadHelper.ThrowIfNotOnUIThread();

            // Dispose existing children
            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();

            if (!Directory.Exists(_folderPath))
                return;

            // Add subdirectories first
            foreach (var dir in Directory.GetDirectories(_folderPath))
            {
                _children.Add(new GitHubFolderNode(dir, this));
            }

            // Then add files
            foreach (var file in Directory.GetFiles(_folderPath))
            {
                _children.Add(new GitHubFileNode(file, this));
            }

            RaisePropertyChanged(nameof(HasItems));
            RaisePropertyChanged(nameof(Items));
        }

        // IAttachedCollectionSource
        public bool HasItems => _children.Count > 0;
        public IEnumerable Items => _children;

        // ITreeDisplayItem
        public override string Text => _folderName;
        public override string ToolTipText => _folderPath;

        // ITreeDisplayItemWithImages
        public ImageMoniker IconMoniker => GetFolderIcon(_isExpanded);
        public ImageMoniker ExpandedIconMoniker => GetFolderIcon(expanded: true);
        public ImageMoniker OverlayIconMoniker => default;
        public ImageMoniker StateIconMoniker => default;

        private ImageMoniker GetFolderIcon(bool expanded)
        {
            // Check for well-known folder names
            if (_knownFolderIcons.TryGetValue(_folderName, out var knownIcon))
            {
                return knownIcon;
            }

            return expanded ? KnownMonikers.FolderOpened : KnownMonikers.FolderClosed;
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
            if (_watcher != null)
            {
                _watcher.EnableRaisingEvents = false;
                _watcher.Dispose();
                _watcher = null;
            }

            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }
            _children.Clear();
        }
    }
}
