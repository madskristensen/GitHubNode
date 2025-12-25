using System.Collections.ObjectModel;
using System.IO;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Manages file system watching and child collection refresh for GitHub nodes.
    /// Encapsulates common logic shared between GitHubRootNode and GitHubFolderNode.
    /// </summary>
    internal sealed class NodeChildrenManager : IDisposable
    {
        private readonly string _folderPath;
        private readonly object _parent;
        private readonly ObservableCollection<object> _children;
        private readonly Action _onPropertyChanged;
        private readonly bool _includeSubdirectories;
        private FileSystemWatcher _watcher;
        private bool _disposed;

        /// <summary>
        /// Creates a new NodeChildrenManager.
        /// </summary>
        /// <param name="folderPath">The folder path to watch.</param>
        /// <param name="parent">The parent node for creating child nodes.</param>
        /// <param name="children">The observable collection to populate with children.</param>
        /// <param name="onPropertyChanged">Action to invoke when properties change.</param>
        /// <param name="includeSubdirectories">Whether to watch subdirectories recursively.</param>
        public NodeChildrenManager(
            string folderPath,
            object parent,
            ObservableCollection<object> children,
            Action onPropertyChanged,
            bool includeSubdirectories = false)
        {
            _folderPath = folderPath;
            _parent = parent;
            _children = children;
            _onPropertyChanged = onPropertyChanged;
            _includeSubdirectories = includeSubdirectories;
        }

        /// <summary>
        /// Gets whether there are any children.
        /// </summary>
        public bool HasItems => _children.Count > 0;

        /// <summary>
        /// Initializes the manager by refreshing children and setting up the file watcher.
        /// </summary>
        public void Initialize()
        {
            RefreshChildren();
            SetupFileWatcher();
        }

        /// <summary>
        /// Refreshes the children collection from the file system.
        /// Must be called on the UI thread.
        /// </summary>
        public void RefreshChildren()
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
                _children.Add(new GitHubFolderNode(dir, _parent));
            }

            // Then add files
            foreach (var file in Directory.GetFiles(_folderPath))
            {
                _children.Add(new GitHubFileNode(file, _parent));
            }

            _onPropertyChanged?.Invoke();
        }

        private void SetupFileWatcher()
        {
            if (!Directory.Exists(_folderPath))
                return;

            _watcher = new FileSystemWatcher(_folderPath)
            {
                IncludeSubdirectories = _includeSubdirectories,
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

        public void Dispose()
        {
            if (_disposed)
                return;

            _disposed = true;

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
