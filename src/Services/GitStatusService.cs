using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.Services
{
    /// <summary>
    /// Represents the Git status of a file.
    /// </summary>
    internal enum GitFileStatus
    {
        /// <summary>File is not in a Git repository.</summary>
        NotInRepo,
        /// <summary>File is untracked (not added to Git).</summary>
        Untracked,
        /// <summary>File is ignored by Git.</summary>
        Ignored,
        /// <summary>File is committed and unmodified.</summary>
        Unmodified,
        /// <summary>File has been modified.</summary>
        Modified,
        /// <summary>File has been staged for commit.</summary>
        Staged,
        /// <summary>File is newly added and staged.</summary>
        Added,
        /// <summary>File has been deleted.</summary>
        Deleted,
        /// <summary>File has been renamed.</summary>
        Renamed,
        /// <summary>File has merge conflicts.</summary>
        Conflict
    }

    /// <summary>
    /// Service for getting Git status of files.
    /// </summary>
    internal static class GitStatusService
    {
        private static readonly ConcurrentDictionary<string, CachedStatus> _statusCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromSeconds(5);
        private static readonly TimeSpan _gitCommandTimeout = TimeSpan.FromSeconds(5);
        private static readonly object _refreshLock = new();
        private static readonly ConcurrentDictionary<string, DateTime> _lastRefreshByRepo = new(StringComparer.OrdinalIgnoreCase);

        private sealed class CachedStatus
        {
            public GitFileStatus Status { get; set; }
            public DateTime Timestamp { get; set; }
        }

        /// <summary>
        /// Gets the cached Git status for a file synchronously.
        /// Returns the cached value if available, or Unknown if not yet loaded.
        /// Use <see cref="GetFileStatusAsync"/> to ensure fresh data.
        /// </summary>
        public static GitFileStatus GetCachedFileStatus(string filePath)
        {
            if (string.IsNullOrEmpty(filePath))
            {
                return GitFileStatus.NotInRepo;
            }

            // Normalize the file path for consistent cache lookups
            filePath = Path.GetFullPath(filePath);

            if (TryGetFreshCachedStatus(filePath, out GitFileStatus cachedStatus))
            {
                return cachedStatus;
            }

            // Check if any parent directory has a status (e.g., git reports "?? folder/" for untracked folders)
            GitFileStatus parentStatus = GetParentDirectoryStatus(filePath);
            if (parentStatus != GitFileStatus.NotInRepo)
            {
                return parentStatus;
            }

            // Not in cache - return a neutral status that won't show an icon
            return GitFileStatus.NotInRepo;
        }

        /// <summary>
        /// Gets the Git status for a file asynchronously.
        /// </summary>
        public static async Task<GitFileStatus> GetFileStatusAsync(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return GitFileStatus.NotInRepo;
            }

            // Normalize the file path for consistent cache lookups
            filePath = Path.GetFullPath(filePath);

            // Check cache first
            if (_statusCache.TryGetValue(filePath, out CachedStatus cached) &&
                DateTime.UtcNow - cached.Timestamp < _cacheExpiration)
            {
                return cached.Status;
            }

            // Run Git operations on a background thread
            return await Task.Run(() => GetFileStatusCore(filePath));
        }

        private static GitFileStatus GetFileStatusCore(string filePath)
        {
            // Normalize the file path for consistent cache lookups
            filePath = Path.GetFullPath(filePath);

            // Find repo root
            var repoRoot = FindGitRoot(filePath);
            if (string.IsNullOrEmpty(repoRoot))
            {
                return GitFileStatus.NotInRepo;
            }

            EnsureStatusCacheFresh(repoRoot);

            // Return cached status or default to Unmodified
            if (TryGetFreshCachedStatus(filePath, out GitFileStatus cachedStatus))
            {
                return cachedStatus;
            }

            // Check if any parent directory has a status (e.g., git reports "?? folder/" for untracked folders)
            GitFileStatus parentStatus = GetParentDirectoryStatus(filePath);
            if (parentStatus != GitFileStatus.NotInRepo)
            {
                return parentStatus;
            }

            // If not in cache after refresh, it's likely unmodified (committed)
            return GitFileStatus.Unmodified;
        }

        /// <summary>
        /// Gets the Git status for a file synchronously.
        /// Warning: This may block the UI thread. Prefer <see cref="GetFileStatusAsync"/> when possible.
        /// </summary>
        public static GitFileStatus GetFileStatus(string filePath)
        {
            if (string.IsNullOrEmpty(filePath) || !File.Exists(filePath))
            {
                return GitFileStatus.NotInRepo;
            }

            // Normalize the file path for consistent cache lookups
            filePath = Path.GetFullPath(filePath);

            // Check cache first
            if (TryGetFreshCachedStatus(filePath, out GitFileStatus cachedStatus))
            {
                return cachedStatus;
            }

            // Find repo root
            var repoRoot = FindGitRoot(filePath);
            if (string.IsNullOrEmpty(repoRoot))
            {
                return GitFileStatus.NotInRepo;
            }

            EnsureStatusCacheFresh(repoRoot);

            // Return cached status or default to Unmodified
            if (TryGetFreshCachedStatus(filePath, out cachedStatus))
            {
                return cachedStatus;
            }

            // Check if any parent directory has a status (e.g., git reports "?? folder/" for untracked folders)
            GitFileStatus parentStatus = GetParentDirectoryStatus(filePath);
            if (parentStatus != GitFileStatus.NotInRepo)
            {
                return parentStatus;
            }

            // If not in cache after refresh, it's likely unmodified (committed)
            return GitFileStatus.Unmodified;
        }

        /// <summary>
        /// Gets the appropriate state icon for a Git file status.
        /// Returns default (no icon) for unmodified/committed files.
        /// </summary>
        public static ImageMoniker GetStatusIcon(GitFileStatus status)
        {
            return status switch
            {
                GitFileStatus.Unmodified => KnownMonikers.CheckedInNode,
                GitFileStatus.Modified => KnownMonikers.CheckedOutForEditNode,
                GitFileStatus.Staged => KnownMonikers.Checkmark,
                GitFileStatus.Added or GitFileStatus.Untracked => KnownMonikers.PendingAddNode,
                GitFileStatus.Deleted => KnownMonikers.PendingDeleteNode,
                GitFileStatus.Conflict => KnownMonikers.StatusWarning,
                GitFileStatus.Ignored => KnownMonikers.HideMember,
                GitFileStatus.Renamed => KnownMonikers.PendingRenameNode,
                // Unmodified, NotInRepo, and other states show no icon
                _ => default,
            };
        }

        /// <summary>
        /// Checks if any parent directory of the file has a cached status.
        /// This handles cases where git reports folder-level status (e.g., "?? folder/") instead of individual files.
        /// </summary>
        private static GitFileStatus GetParentDirectoryStatus(string filePath)
        {
            var directory = Path.GetDirectoryName(filePath);

            while (!string.IsNullOrEmpty(directory))
            {
                // Check both with and without trailing slash since git may report "?? folder/"
                if (_statusCache.TryGetValue(directory, out CachedStatus cached) ||
                    _statusCache.TryGetValue(directory + Path.DirectorySeparatorChar, out cached))
                {
                    return cached.Status;
                }

                var parent = Path.GetDirectoryName(directory);
                if (parent == directory)
                {
                    break; // Reached root
                }

                directory = parent;
            }

            return GitFileStatus.NotInRepo;
        }

        /// <summary>
        /// Invalidates the cache, forcing a refresh on next status request.
        /// </summary>
        public static void InvalidateCache()
        {
            _statusCache.Clear();
            _lastRefreshByRepo.Clear();
        }

        private static bool TryGetFreshCachedStatus(string filePath, out GitFileStatus status)
        {
            status = GitFileStatus.NotInRepo;

            if (!_statusCache.TryGetValue(filePath, out CachedStatus cached))
            {
                return false;
            }

            if (DateTime.UtcNow - cached.Timestamp >= _cacheExpiration)
            {
                _statusCache.TryRemove(filePath, out _);
                return false;
            }

            status = cached.Status;
            return true;
        }

        private static void EnsureStatusCacheFresh(string repoRoot)
        {
            if (string.IsNullOrWhiteSpace(repoRoot))
            {
                return;
            }

            if (_lastRefreshByRepo.TryGetValue(repoRoot, out DateTime lastRefresh) &&
                DateTime.UtcNow - lastRefresh <= _cacheExpiration)
            {
                return;
            }

            lock (_refreshLock)
            {
                if (_lastRefreshByRepo.TryGetValue(repoRoot, out lastRefresh) &&
                    DateTime.UtcNow - lastRefresh <= _cacheExpiration)
                {
                    return;
                }

                RefreshStatusCache(repoRoot);
                _lastRefreshByRepo[repoRoot] = DateTime.UtcNow;
            }
        }

        private static void RefreshStatusCache(string repoRoot)
        {
            try
            {
                // Get status for all files using porcelain format for easy parsing
                // --porcelain=v1 gives us: XY filename
                // X = index status, Y = working tree status
                var output = RunGitCommand(repoRoot, "status --porcelain=v1");
                if (string.IsNullOrEmpty(output))
                {
                    return;
                }

                var lines = output.Split(['\n', '\r'], StringSplitOptions.RemoveEmptyEntries);
                var statusPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                DateTime now = DateTime.UtcNow;

                foreach (var line in lines)
                {
                    if (line.Length < 3)
                    {
                        continue;
                    }

                    var indexStatus = line[0];
                    var workTreeStatus = line[1];
                    var relativePath = line.Substring(3).Trim();

                    // Git may quote paths with special characters - remove quotes
                    if (relativePath.StartsWith("\"") && relativePath.EndsWith("\""))
                    {
                        relativePath = relativePath.Substring(1, relativePath.Length - 2);
                    }

                    // Handle renamed files: "R  old -> new"
                    if (relativePath.Contains(" -> "))
                    {
                        var parts = relativePath.Split([" -> "], StringSplitOptions.None);
                        relativePath = parts.Length > 1 ? parts[1] : parts[0];
                    }

                    // Normalize path separators (git uses forward slashes)
                    relativePath = relativePath.Replace('/', Path.DirectorySeparatorChar);

                    var fullPath = Path.GetFullPath(Path.Combine(repoRoot, relativePath));
                    GitFileStatus status = ParseGitStatus(indexStatus, workTreeStatus);
                    statusPaths.Add(fullPath);

                    _statusCache[fullPath] = new CachedStatus
                    {
                        Status = status,
                        Timestamp = now
                    };
                }

                string repoPrefix = repoRoot.TrimEnd(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar) + Path.DirectorySeparatorChar;
                foreach (string key in _statusCache.Keys)
                {
                    if (!key.StartsWith(repoPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (statusPaths.Contains(key))
                    {
                        continue;
                    }

                    _statusCache.TryRemove(key, out _);
                }
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"GitStatusService.RefreshStatusCache failed for '{repoRoot}': {ex}");
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"GitStatusService.RefreshStatusCache failed for '{repoRoot}': {ex}");
            }
        }

        private static GitFileStatus ParseGitStatus(char indexStatus, char workTreeStatus)
        {
            // Check for conflicts first (both modified)
            if (indexStatus == 'U' || workTreeStatus == 'U' ||
                (indexStatus == 'A' && workTreeStatus == 'A') ||
                (indexStatus == 'D' && workTreeStatus == 'D'))
            {
                return GitFileStatus.Conflict;
            }

            // Check working tree status first (local changes)
            switch (workTreeStatus)
            {
                case 'M':
                    return GitFileStatus.Modified;
                case 'D':
                    return GitFileStatus.Deleted;
                case '?':
                    return GitFileStatus.Untracked;
                case '!':
                    return GitFileStatus.Ignored;
            }

            // Check index status (staged changes)
            switch (indexStatus)
            {
                case 'M':
                    return GitFileStatus.Staged;
                case 'A':
                    return GitFileStatus.Added;
                case 'D':
                    return GitFileStatus.Deleted;
                case 'R':
                    return GitFileStatus.Renamed;
            }

            return GitFileStatus.Unmodified;
        }

        private static string FindGitRoot(string path)
        {
            var current = Directory.Exists(path) ? path : Path.GetDirectoryName(path);

            while (!string.IsNullOrEmpty(current))
            {
                var gitPath = Path.Combine(current, ".git");
                if (Directory.Exists(gitPath) || File.Exists(gitPath))
                {
                    return current;
                }
                current = Path.GetDirectoryName(current);
            }

            return null;
        }

        private static string RunGitCommand(string workingDirectory, string arguments)
        {
            try
            {
                return RunGitCommandAsync(workingDirectory, arguments).GetAwaiter().GetResult();
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"GitStatusService.RunGitCommand failed in '{workingDirectory}': {ex}");
                return null;
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"GitStatusService.RunGitCommand failed in '{workingDirectory}': {ex}");
                return null;
            }
        }

        private static async Task<string> RunGitCommandAsync(string workingDirectory, string arguments)
        {
            var startInfo = new ProcessStartInfo
            {
                FileName = "git",
                Arguments = arguments,
                WorkingDirectory = workingDirectory,
                UseShellExecute = false,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                CreateNoWindow = true
            };

            using (var process = Process.Start(startInfo))
            {
                if (process == null)
                {
                    return null;
                }

                Task<string> outputTask = process.StandardOutput.ReadToEndAsync();
                Task<string> errorTask = process.StandardError.ReadToEndAsync();
                Task completionTask = Task.WhenAll(outputTask, errorTask, WaitForExitAsync(process));

                using (var timeoutCancellation = new CancellationTokenSource(_gitCommandTimeout))
                {
                    Task timeoutTask = Task.Delay(Timeout.InfiniteTimeSpan, timeoutCancellation.Token);
                    Task finishedTask = await Task.WhenAny(completionTask, timeoutTask).ConfigureAwait(false);
                    if (!ReferenceEquals(finishedTask, completionTask))
                    {
                        TryTerminateProcess(process);
                        return null;
                    }

                    timeoutCancellation.Cancel();
                }

                return process.ExitCode == 0
                    ? await outputTask.ConfigureAwait(false)
                    : null;
            }
        }

        private static Task WaitForExitAsync(Process process)
        {
            if (process.HasExited)
            {
                return Task.CompletedTask;
            }

            var completionSource = new TaskCompletionSource<object>();
            EventHandler handler = null;
            handler = (sender, args) =>
            {
                process.Exited -= handler;
                completionSource.TrySetResult(null);
            };

            process.EnableRaisingEvents = true;
            process.Exited += handler;

            if (process.HasExited)
            {
                process.Exited -= handler;
                return Task.CompletedTask;
            }

            return completionSource.Task;
        }

        private static void TryTerminateProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                    process.WaitForExit(1000);
                }
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                // Best-effort cleanup only
            }
            catch (System.ComponentModel.Win32Exception ex)
            {
                _ = ex.LogAsync();
                // Best-effort cleanup only
            }
        }
    }
}

