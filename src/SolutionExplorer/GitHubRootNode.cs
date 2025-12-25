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
        private FileSystemWatcher _watcher;

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
            _gitHubFolderPath = gitHubFolderPath;
            _children = [];

            RefreshChildren();
            SetupFileWatcher();
        }

        /// <summary>
        /// Gets the path to the .github folder.
        /// </summary>
        public string GitHubFolderPath => _gitHubFolderPath;

        private void SetupFileWatcher()
        {
            if (!Directory.Exists(_gitHubFolderPath))
                return;

            _watcher = new FileSystemWatcher(_gitHubFolderPath)
            {
                IncludeSubdirectories = true,
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

            if (!Directory.Exists(_gitHubFolderPath))
                return;

            // Add subdirectories first
            foreach (var dir in Directory.GetDirectories(_gitHubFolderPath))
            {
                _children.Add(new GitHubFolderNode(dir, this));
            }

            // Then add files
            foreach (var file in Directory.GetFiles(_gitHubFolderPath))
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
