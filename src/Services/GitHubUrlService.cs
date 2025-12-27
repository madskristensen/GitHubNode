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
            try
            {
                var result = RunGitCommand(repoRoot, "config --get remote.origin.url");
                return result?.Trim();
            }
            catch
            {
                return null;
            }
        }

        private static string GetGitBranch(string repoRoot)
        {
            try
            {
                var result = RunGitCommand(repoRoot, "rev-parse --abbrev-ref HEAD");
                return result?.Trim();
            }
            catch
            {
                return null;
            }
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
            Match sshMatch = Regex.Match(remoteUrl, @"git@github\.com:(.+?)(?:\.git)?$");
            if (sshMatch.Success)
            {
                return $"https://github.com/{sshMatch.Groups[1].Value}";
            }

            // Handle HTTPS format: https://github.com/owner/repo.git
            Match httpsMatch = Regex.Match(remoteUrl, @"https://github\.com/(.+?)(?:\.git)?$");
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
