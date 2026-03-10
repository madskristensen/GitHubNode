using System;
using System.Diagnostics;
using System.IO;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Manages git operations for MarketplaceInfo repositories.
    /// </summary>
    internal static class MarketplaceGitService
    {
        private const int _gitTimeoutSeconds = 120;
        private static readonly SemaphoreSlim _gitLock = new SemaphoreSlim(1, 1);

        /// <summary>
        /// Result of a git operation.
        /// </summary>
        internal sealed class GitResult
        {
            public bool Success { get; set; }
            public string Output { get; set; }
            public string Error { get; set; }
            public int ExitCode { get; set; }
        }

        /// <summary>
        /// Clones a MarketplaceInfo repository if not already cloned.
        /// </summary>
        public static async Task<GitResult> CloneOrUpdateAsync(
            string owner,
            string repo,
            string branch,
            CancellationToken cancellationToken = default)
        {
            var localPath = MarketplaceStorageService.GetMarketplaceDirectory(owner, repo);
            var cloneUrl = $"https://github.com/{owner}/{repo}.git";

            MarketplaceStorageService.EnsureDirectoriesExist();

            await _gitLock.WaitAsync(cancellationToken);
            try
            {
                if (Directory.Exists(Path.Combine(localPath, ".git")))
                {
                    // Repository exists, do a pull
                    return await PullAsync(localPath, branch, cancellationToken);
                }
                else
                {
                    // Repository doesn't exist, clone it
                    return await CloneAsync(cloneUrl, localPath, branch, cancellationToken);
                }
            }
            finally
            {
                _gitLock.Release();
            }
        }

        /// <summary>
        /// Clones a linked repository into a subfolder of the parent marketplace.
        /// Linked repositories are external repos referenced by plugins in a marketplace.json.
        /// </summary>
        /// <param name="parentMarketplaceOwner">Owner of the parent marketplace.</param>
        /// <param name="parentMarketplaceRepo">Repository name of the parent marketplace.</param>
        /// <param name="linkedOwner">Owner of the linked repository.</param>
        /// <param name="linkedRepo">Repository name of the linked repository.</param>
        /// <param name="branch">Branch to clone (defaults to "main").</param>
        /// <param name="cancellationToken">Cancellation token.</param>
        /// <returns>Result of the git operation and the local path to the cloned repo.</returns>
        public static async Task<(GitResult Result, string LocalPath)> CloneLinkedRepositoryAsync(
            string parentMarketplaceOwner,
            string parentMarketplaceRepo,
            string linkedOwner,
            string linkedRepo,
            string branch = "main",
            CancellationToken cancellationToken = default)
        {
            var localPath = MarketplaceStorageService.GetLinkedRepositoryDirectory(
                parentMarketplaceOwner, parentMarketplaceRepo, linkedOwner, linkedRepo);
            var cloneUrl = $"https://github.com/{linkedOwner}/{linkedRepo}.git";

            await _gitLock.WaitAsync(cancellationToken);
            try
            {
                if (Directory.Exists(Path.Combine(localPath, ".git")))
                {
                    // Linked repo already cloned, do a pull
                    var result = await PullAsync(localPath, branch, cancellationToken);
                    return (result, localPath);
                }
                else
                {
                    // Clone the linked repository
                    var result = await CloneAsync(cloneUrl, localPath, branch, cancellationToken);
                    return (result, localPath);
                }
            }
            finally
            {
                _gitLock.Release();
            }
        }

        /// <summary>
        /// Checks if a MarketplaceInfo repository is cloned.
        /// </summary>
        public static bool IsCloned(string owner, string repo)
        {
            var localPath = MarketplaceStorageService.GetMarketplaceDirectory(owner, repo);
            return Directory.Exists(Path.Combine(localPath, ".git"));
        }

        /// <summary>
        /// Gets the last update time for a cloned repository.
        /// </summary>
        public static DateTime? GetLastUpdateTime(string owner, string repo)
        {
            var localPath = MarketplaceStorageService.GetMarketplaceDirectory(owner, repo);
            var fetchHead = Path.Combine(localPath, ".git", "FETCH_HEAD");

            if (File.Exists(fetchHead))
            {
                return File.GetLastWriteTimeUtc(fetchHead);
            }

            // Fall back to .git folder modification time
            var gitDir = Path.Combine(localPath, ".git");
            if (Directory.Exists(gitDir))
            {
                return Directory.GetLastWriteTimeUtc(gitDir);
            }

            return null;
        }

        /// <summary>
        /// Checks if a repository needs updating based on the configured interval.
        /// </summary>
        public static bool NeedsUpdate(string owner, string repo, int intervalHours = 24)
        {
            var lastUpdate = GetLastUpdateTime(owner, repo);
            if (!lastUpdate.HasValue)
            {
                return true;
            }

            return DateTime.UtcNow - lastUpdate.Value > TimeSpan.FromHours(intervalHours);
        }

        /// <summary>
        /// Clones a repository.
        /// </summary>
        private static async Task<GitResult> CloneAsync(
            string cloneUrl,
            string localPath,
            string branch,
            CancellationToken cancellationToken)
        {
            // Ensure parent directory exists and target doesn't
            var parentDir = Path.GetDirectoryName(localPath);
            if (!string.IsNullOrEmpty(parentDir))
            {
                Directory.CreateDirectory(parentDir);
            }

            if (Directory.Exists(localPath))
            {
                // Clean up partial clone
                try
                {
                    MarketplaceStorageService.DeleteMarketplaceClone(
                        Path.GetFileName(Path.GetDirectoryName(localPath)),
                        Path.GetFileName(localPath));
                }
                catch
                {
                    // Best effort
                }
            }

            // Clone with depth 1 for faster download
            var args = $"clone --depth 1 --branch {branch} \"{cloneUrl}\" \"{localPath}\"";
            return await RunGitAsync(args, workingDirectory: null, cancellationToken);
        }

        /// <summary>
        /// Pulls the latest changes from a repository.
        /// </summary>
        private static async Task<GitResult> PullAsync(
            string localPath,
            string branch,
            CancellationToken cancellationToken)
        {
            // First, fetch
            var fetchResult = await RunGitAsync("fetch --depth 1", localPath, cancellationToken);
            if (!fetchResult.Success)
            {
                return fetchResult;
            }

            // Then reset to origin/branch (handles force pushes)
            var resetResult = await RunGitAsync($"reset --hard origin/{branch}", localPath, cancellationToken);
            return resetResult;
        }

        /// <summary>
        /// Runs a git command and returns the result.
        /// </summary>
        private static async Task<GitResult> RunGitAsync(
            string arguments,
            string workingDirectory,
            CancellationToken cancellationToken)
        {
            var result = new GitResult();
            var outputBuilder = new StringBuilder();
            var errorBuilder = new StringBuilder();

            try
            {
                var startInfo = new ProcessStartInfo
                {
                    FileName = "git",
                    Arguments = arguments,
                    WorkingDirectory = workingDirectory ?? "",
                    UseShellExecute = false,
                    RedirectStandardOutput = true,
                    RedirectStandardError = true,
                    CreateNoWindow = true,
                    StandardOutputEncoding = Encoding.UTF8,
                    StandardErrorEncoding = Encoding.UTF8
                };

                using (var process = new Process { StartInfo = startInfo })
                {
                    process.OutputDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            outputBuilder.AppendLine(e.Data);
                        }
                    };

                    process.ErrorDataReceived += (s, e) =>
                    {
                        if (e.Data != null)
                        {
                            errorBuilder.AppendLine(e.Data);
                        }
                    };

                    process.Start();
                    process.BeginOutputReadLine();
                    process.BeginErrorReadLine();

                    var completedTask = await Task.WhenAny(
                        Task.Run(() => process.WaitForExit(), cancellationToken),
                        Task.Delay(TimeSpan.FromSeconds(_gitTimeoutSeconds), cancellationToken));

                    if (cancellationToken.IsCancellationRequested)
                    {
                        TryKillProcess(process);
                        result.Success = false;
                        result.Error = "Operation was cancelled.";
                        result.ExitCode = -1;
                        return result;
                    }

                    if (!process.HasExited)
                    {
                        TryKillProcess(process);
                        result.Success = false;
                        result.Error = $"Git operation timed out after {_gitTimeoutSeconds} seconds.";
                        result.ExitCode = -1;
                        return result;
                    }

                    result.ExitCode = process.ExitCode;
                    result.Success = process.ExitCode == 0;
                    result.Output = outputBuilder.ToString().Trim();
                    result.Error = errorBuilder.ToString().Trim();
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceGitService.RunGitAsync failed: {ex}");
                result.Success = false;
                result.Error = ex.Message;
                result.ExitCode = -1;

                // Check if git is not installed
                if (ex is System.ComponentModel.Win32Exception win32Ex && win32Ex.NativeErrorCode == 2)
                {
                    result.Error = "Git is not installed or not found in PATH. Please install Git and try again.";
                }
            }

            return result;
        }

        private static void TryKillProcess(Process process)
        {
            try
            {
                if (!process.HasExited)
                {
                    process.Kill();
                }
            }
            catch
            {
                // Best effort
            }
        }
    }
}
