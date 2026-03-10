using System.Collections.ObjectModel;
using System.Diagnostics;
using System.IO;
using System.Threading;
using GitHubNode.Services;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Manages file system watching and child collection refresh for GitHub nodes.
    /// Encapsulates common logic shared between GitHubRootNode and GitHubFolderNode.
    /// When the folder does not exist, watches the parent directory for creation
    /// of the target folder.
    /// </summary>
    internal sealed class NodeChildrenManager : IDisposable
    {
        private readonly string _folderPath;
        private readonly object _parent;
        private readonly ObservableCollection<object> _children;
        private readonly Action _onPropertyChanged;
        private readonly bool _includeSubdirectories;
        private FileSystemWatcher _watcher;
        private FileSystemWatcher _parentWatcher;
        private bool _disposed;
        private CancellationTokenSource _debounceCts;
        private readonly object _debounceLock = new();

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
        /// Must be called on the UI thread.
        /// </summary>
        public void Initialize()
        {
            ThreadHelper.ThrowIfNotOnUIThread();
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

            foreach (var child in _children)
            {
                (child as IDisposable)?.Dispose();
            }

            _children.Clear();

            if (!Directory.Exists(_folderPath))
            {
                return;
            }

            foreach (var dir in Directory.GetDirectories(_folderPath))
            {
                _children.Add(new GitHubFolderNode(dir, _parent));
            }

            foreach (var file in Directory.GetFiles(_folderPath))
            {
                _children.Add(new GitHubFileNode(file, _parent));
            }

            _onPropertyChanged?.Invoke();
        }

        private void SetupFileWatcher()
        {
            if (!Directory.Exists(_folderPath))
            {
                SetupParentWatcher();
                return;
            }

            SetupFolderWatcher();
        }

        private void SetupParentWatcher()
        {
            var parentDir = Path.GetDirectoryName(_folderPath);
            if (string.IsNullOrEmpty(parentDir) || !Directory.Exists(parentDir))
            {
                return;
            }

            _parentWatcher = new FileSystemWatcher(parentDir)
            {
                IncludeSubdirectories = false,
                NotifyFilter = NotifyFilters.DirectoryName,
                Filter = Path.GetFileName(_folderPath)
            };

            _parentWatcher.Created += OnTargetFolderCreated;
            _parentWatcher.EnableRaisingEvents = true;
        }

        private void OnTargetFolderCreated(object sender, FileSystemEventArgs e)
        {
#pragma warning disable VSSDK007 // Use ThreadHelper.JoinableTaskFactory.RunAsync (fire and forget)
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                try
                {
                    await System.Threading.Tasks.Task.Delay(50);
                    await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                    if (_disposed)
                    {
                        return;
                    }

                    if (_parentWatcher != null)
                    {
                        _parentWatcher.EnableRaisingEvents = false;
                        _parentWatcher.Dispose();
                        _parentWatcher = null;
                    }

                    SetupFolderWatcher();
                    GitStatusService.InvalidateCache();
                    RefreshChildren();
                }
                catch (Exception ex)
                {
                    _ = ex.LogAsync();
                    Debug.WriteLine($"NodeChildrenManager.OnTargetFolderCreated failed for '{_folderPath}': {ex}");
                }
            });
#pragma warning restore VSSDK007
        }

        private void SetupFolderWatcher()
        {
            if (_watcher != null || !Directory.Exists(_folderPath))
            {
                return;
            }

            _watcher = new FileSystemWatcher(_folderPath)
            {
                IncludeSubdirectories = _includeSubdirectories,
                NotifyFilter = NotifyFilters.FileName | NotifyFilters.DirectoryName
            };

            _watcher.Created += OnFileSystemChanged;
            _watcher.Deleted += OnFileSystemChanged;
            _watcher.Renamed += OnFileSystemRenamed;
            _watcher.EnableRaisingEvents = true;
        }

        private void OnFileSystemChanged(object sender, FileSystemEventArgs e)
        {
            if (IsTempFile(e.FullPath))
            {
                return;
            }

            var parentDir = Path.GetDirectoryName(e.FullPath);
            if (!string.Equals(parentDir, _folderPath, StringComparison.OrdinalIgnoreCase))
            {
                return;
            }

            DebouncedRefresh();
        }

        private void DebouncedRefresh()
        {
            lock (_debounceLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = new CancellationTokenSource();
                CancellationToken token = _debounceCts.Token;

#pragma warning disable VSSDK007 // Use ThreadHelper.JoinableTaskFactory.RunAsync (fire and forget)
                _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
                {
                    try
                    {
                        await System.Threading.Tasks.Task.Delay(100, token);

                        if (token.IsCancellationRequested)
                        {
                            return;
                        }

                        GitStatusService.InvalidateCache();
                        await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync(token);
                        RefreshChildren();
                    }
                    catch (System.Threading.Tasks.TaskCanceledException)
                    {
                        // Expected when debounce is superseded by a newer request
                    }
                });
#pragma warning restore VSSDK007
            }
        }

        private static bool IsTempFile(string path)
        {
            var fileName = Path.GetFileName(path);

            if (fileName.StartsWith("~") || fileName.EndsWith(".tmp") || fileName.EndsWith(".TMP"))
            {
                return true;
            }

            return fileName.StartsWith(".");
        }

        private void OnFileSystemRenamed(object sender, RenamedEventArgs e)
        {
            if (IsTempFile(e.OldFullPath) || IsTempFile(e.FullPath))
            {
                return;
            }

            GitStatusService.InvalidateCache();

#pragma warning disable VSSDK007 // Use ThreadHelper.JoinableTaskFactory.RunAsync (fire and forget)
            _ = ThreadHelper.JoinableTaskFactory.RunAsync(async () =>
            {
                await ThreadHelper.JoinableTaskFactory.SwitchToMainThreadAsync();

                var parentDir = Path.GetDirectoryName(e.OldFullPath);
                if (!string.Equals(parentDir, _folderPath, StringComparison.OrdinalIgnoreCase))
                {
                    return;
                }

                foreach (var child in _children)
                {
                    if (child is GitHubFileNode fileNode &&
                        string.Equals(fileNode.FilePath, e.OldFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        fileNode.UpdatePath(e.FullPath);
                        return;
                    }

                    if (child is GitHubFolderNode folderNode &&
                        string.Equals(folderNode.FolderPath, e.OldFullPath, StringComparison.OrdinalIgnoreCase))
                    {
                        folderNode.UpdatePath(e.FullPath);
                        return;
                    }
                }

                RefreshChildren();
            });
#pragma warning restore VSSDK007
        }

        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            lock (_debounceLock)
            {
                _debounceCts?.Cancel();
                _debounceCts?.Dispose();
                _debounceCts = null;
            }

            if (_parentWatcher != null)
            {
                _parentWatcher.EnableRaisingEvents = false;
                _parentWatcher.Dispose();
                _parentWatcher = null;
            }

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
