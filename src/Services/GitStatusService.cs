using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
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
        private static readonly object _refreshLock = new();
        private static DateTime _lastRefresh = DateTime.MinValue;

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

            if (_statusCache.TryGetValue(filePath, out CachedStatus cached))
            {
                return cached.Status;
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

            // Refresh status for all files if cache is stale
            if (DateTime.UtcNow - _lastRefresh > _cacheExpiration)
            {
                lock (_refreshLock)
                {
                    // Recapture 'now' inside the lock
                    DateTime now = DateTime.UtcNow;
                    if (now - _lastRefresh > _cacheExpiration)
                    {
                        RefreshStatusCache(repoRoot);
                        _lastRefresh = now;
                    }
                }
            }

            // Return cached status or default to Unmodified
            if (_statusCache.TryGetValue(filePath, out CachedStatus cached))
            {
                return cached.Status;
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
            if (_statusCache.TryGetValue(filePath, out CachedStatus cached) &&
                DateTime.UtcNow - cached.Timestamp < _cacheExpiration)
            {
                return cached.Status;
            }

            // Find repo root
            var repoRoot = FindGitRoot(filePath);
            if (string.IsNullOrEmpty(repoRoot))
            {
                return GitFileStatus.NotInRepo;
            }

            // Refresh status for all files in .github folder if cache is stale
            if (DateTime.UtcNow - _lastRefresh > _cacheExpiration)
            {
                lock (_refreshLock)
                {
                    // Recapture 'now' inside the lock
                    DateTime now = DateTime.UtcNow;
                    if (now - _lastRefresh > _cacheExpiration)
                    {
                        RefreshStatusCache(repoRoot);
                        _lastRefresh = now;
                    }
                }
            }

            // Return cached status or default to Unmodified
            if (_statusCache.TryGetValue(filePath, out cached))
            {
                return cached.Status;
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
            _lastRefresh = DateTime.MinValue;
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

                    _statusCache[fullPath] = new CachedStatus
                    {
                        Status = status,
                        Timestamp = DateTime.UtcNow
                    };
                }
            }
            catch
            {
                // Silently fail - Git status is a nice-to-have feature
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
                if (Directory.Exists(Path.Combine(current, ".git")))
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

                    var output = process.StandardOutput.ReadToEnd();
                    process.WaitForExit(5000);

                    return process.ExitCode == 0 ? output : null;
                }
            }
            catch
            {
                return null;
            }
        }
    }
}

