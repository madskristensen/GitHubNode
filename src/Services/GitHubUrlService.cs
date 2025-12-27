using System.Collections.Concurrent;
using System.Diagnostics;
using System.IO;
using System.Text.RegularExpressions;

namespace GitHubNode.Services
{
    /// <summary>
    /// Service for constructing GitHub URLs from local file paths.
    /// </summary>
    internal static class GitHubUrlService
    {
        // Pre-compiled regex patterns for better performance
        private static readonly Regex _sshUrlRegex = new(@"git@github\.com:(.+?)(?:\.git)?$", RegexOptions.Compiled);
        private static readonly Regex _httpsUrlRegex = new(@"https://github\.com/(.+?)(?:\.git)?$", RegexOptions.Compiled);

        // Cache for git repository information (remote URL and branch per repo root)
        private static readonly ConcurrentDictionary<string, (string RemoteUrl, string Branch, DateTime Timestamp)> _repoInfoCache = new(StringComparer.OrdinalIgnoreCase);
        private static readonly TimeSpan _cacheExpiration = TimeSpan.FromMinutes(5);

        /// <summary>
        /// Gets the GitHub URL for a local file or folder path.
        /// </summary>
        /// <param name="localPath">The local file or folder path.</param>
        /// <returns>The GitHub URL, or null if it cannot be determined.</returns>
        public static string GetGitHubUrl(string localPath)
        {
            if (string.IsNullOrEmpty(localPath))
            {
                return null;
            }

            // Find the repository root
            var repoRoot = FindGitRoot(localPath);
            if (string.IsNullOrEmpty(repoRoot))
            {
                return null;
            }

            // Get the remote URL
            var remoteUrl = GetGitRemoteUrl(repoRoot);
            if (string.IsNullOrEmpty(remoteUrl))
            {
                return null;
            }

            // Get the current branch
            var branch = GetGitBranch(repoRoot) ?? "main";

            // Convert remote URL to GitHub web URL
            var gitHubBaseUrl = ConvertToGitHubWebUrl(remoteUrl);
            if (string.IsNullOrEmpty(gitHubBaseUrl))
            {
                return null;
            }

            // Get relative path from repo root
            var relativePath = GetRelativePath(repoRoot, localPath);
            if (string.IsNullOrEmpty(relativePath))
            {
                return gitHubBaseUrl;
            }

            // Determine if it's a file or folder
            var pathType = Directory.Exists(localPath) ? "tree" : "blob";

            // Construct the URL
            return $"{gitHubBaseUrl}/{pathType}/{branch}/{relativePath.Replace('\\', '/')}";
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

        private static string GetGitRemoteUrl(string repoRoot)
        {
            (var RemoteUrl, _) = GetCachedRepoInfo(repoRoot);
            return RemoteUrl;
        }

        private static string GetGitBranch(string repoRoot)
        {
            (_, var Branch) = GetCachedRepoInfo(repoRoot);
            return Branch;
        }

        private static (string RemoteUrl, string Branch) GetCachedRepoInfo(string repoRoot)
        {
            if (_repoInfoCache.TryGetValue(repoRoot, out (string RemoteUrl, string Branch, DateTime Timestamp) cached) &&
                DateTime.UtcNow - cached.Timestamp < _cacheExpiration)
            {
                return (cached.RemoteUrl, cached.Branch);
            }

            string remoteUrl = null;
            string branch = null;

            try
            {
                remoteUrl = RunGitCommand(repoRoot, "config --get remote.origin.url")?.Trim();
                branch = RunGitCommand(repoRoot, "rev-parse --abbrev-ref HEAD")?.Trim();
            }
            catch
            {
                // Ignore errors - will return null values
            }

            _repoInfoCache[repoRoot] = (remoteUrl, branch, DateTime.UtcNow);
            return (remoteUrl, branch);
        }

        private static string RunGitCommand(string workingDirectory, string arguments)
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

        private static string ConvertToGitHubWebUrl(string remoteUrl)
        {
            if (string.IsNullOrEmpty(remoteUrl))
            {
                return null;
            }

            // Handle SSH format: git@github.com:owner/repo.git
            Match sshMatch = _sshUrlRegex.Match(remoteUrl);
            if (sshMatch.Success)
            {
                return $"https://github.com/{sshMatch.Groups[1].Value}";
            }

            // Handle HTTPS format: https://github.com/owner/repo.git
            Match httpsMatch = _httpsUrlRegex.Match(remoteUrl);
            if (httpsMatch.Success)
            {
                return $"https://github.com/{httpsMatch.Groups[1].Value}";
            }

            return null;
        }

        private static string GetRelativePath(string basePath, string fullPath)
        {
            if (!basePath.EndsWith(Path.DirectorySeparatorChar.ToString()))
            {
                basePath += Path.DirectorySeparatorChar;
            }

            if (fullPath.StartsWith(basePath, StringComparison.OrdinalIgnoreCase))
            {
                return fullPath.Substring(basePath.Length);
            }

            return null;
        }
    }
}
