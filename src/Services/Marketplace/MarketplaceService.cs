using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Main service for interacting with marketplaces.
    /// Provides a unified API combining storage, git, and parsing services.
    /// </summary>
    internal static class MarketplaceService
    {
        private static readonly object _cacheLock = new object();
        private static readonly Dictionary<string, MarketplaceInfo> _marketplaceCache = new Dictionary<string, MarketplaceInfo>(StringComparer.OrdinalIgnoreCase);
        private static volatile bool _initialLoadComplete;

        /// <summary>
        /// Gets all registered marketplaces (built-in and user-added).
        /// Clones repositories if not already cloned.
        /// </summary>
        public static async Task<List<MarketplaceInfo>> GetAllMarketplacesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var entries = MarketplaceStorageService.GetAllMarketplaceEntries();
            var config = MarketplaceStorageService.LoadConfig();

            // Fetch all marketplaces in parallel for better performance
            var tasks = entries.Select(entry =>
                GetMarketplaceAsync(
                    entry.Owner,
                    entry.Repo,
                    entry.Branch,
                    entry.RepositoryUrl,
                    forceRefresh,
                    config.UpdateIntervalHours,
                    cancellationToken));

            var results = await Task.WhenAll(tasks);

            _initialLoadComplete = true;
            return results.Where(m => m != null).ToList();
        }

        /// <summary>
        /// Gets a specific MarketplaceInfo by owner and repo.
        /// </summary>
        public static async Task<MarketplaceInfo> GetMarketplaceAsync(
            string owner,
            string repo,
            string branch = null,
            string repositoryUrl = null,
            bool forceRefresh = false,
            int updateIntervalHours = 24,
            CancellationToken cancellationToken = default)
        {
            var id = MarketplaceStorageService.GetMarketplaceId(owner, repo, repositoryUrl);
            var localPath = MarketplaceStorageService.GetMarketplaceDirectory(owner, repo, repositoryUrl);

            // Check cache
            if (!forceRefresh)
            {
                lock (_cacheLock)
                {
                    if (_marketplaceCache.TryGetValue(id, out var cached) && cached.IsCloned)
                    {
                        return cached;
                    }
                }
            }

            // If branch is not specified, detect it from the remote
            if (string.IsNullOrEmpty(branch))
            {
                branch = await MarketplaceGitService.GetDefaultBranchAsync(owner, repo, repositoryUrl, cancellationToken);
            }

            // Determine if we need to clone/update
            var needsUpdate = forceRefresh || !MarketplaceGitService.IsCloned(owner, repo, repositoryUrl);
            if (!needsUpdate && _initialLoadComplete)
            {
                needsUpdate = MarketplaceGitService.NeedsUpdate(owner, repo, repositoryUrl, updateIntervalHours);
            }

            if (needsUpdate)
            {
                var gitResult = await MarketplaceGitService.CloneOrUpdateAsync(owner, repo, branch, repositoryUrl, cancellationToken);
                if (!gitResult.Success)
                {
                    Debug.WriteLine($"MarketplaceService: Git operation failed for {id}: {gitResult.Error}");

                    // Return cached version if available
                    lock (_cacheLock)
                    {
                        if (_marketplaceCache.TryGetValue(id, out var cached))
                        {
                            cached.ErrorMessage = gitResult.Error;
                            return cached;
                        }
                    }

                    // Return placeholder with error
                    return new MarketplaceInfo
                    {
                        Id = id,
                        Owner = owner,
                        RepoName = repo,
                        RepositoryUrl = string.IsNullOrWhiteSpace(repositoryUrl) ? null : MarketplaceRepositoryUrl.GetRepositoryUrl(owner, repo, repositoryUrl),
                        Branch = branch,
                        DisplayName = $"{owner}/{repo}",
                        IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo, repositoryUrl),
                        IsCloned = false,
                        ErrorMessage = gitResult.Error
                    };
                }
            }

            // Parse the MarketplaceInfo asynchronously (supports linked repos)
            var marketplaceInfo = await MarketplaceParserService.ParseMarketplaceAsync(
                owner, repo, localPath, repositoryUrl, cloneLinkedRepos: true, cancellationToken);
            marketplaceInfo.Branch = branch;

            // Update cache
            lock (_cacheLock)
            {
                _marketplaceCache[id] = marketplaceInfo;
            }

            return marketplaceInfo;
        }

        /// <summary>
        /// Adds a new user MarketplaceInfo by GitHub URL or owner/repo format.
        /// </summary>
        public static async Task<(bool success, string error, MarketplaceInfo Marketplace)> AddMarketplaceAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            // Parse input (supports "owner/repo" or "https://github.com/owner/repo")
            var (owner, repo, branch, repositoryUrl) = ParseMarketplaceInput(input);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return (false, "Invalid format. Use 'owner/repo' or a repository URL.", null);
            }

            // Check if it's a built-in
            if (MarketplaceStorageService.IsBuiltIn(owner, repo, repositoryUrl))
            {
                return (false, $"{owner}/{repo} is a built-in MarketplaceInfo and is already available.", null);
            }

            // Try to clone and validate
            var marketplace = await GetMarketplaceAsync(owner, repo, branch, repositoryUrl, forceRefresh: true, cancellationToken: cancellationToken);

            if (!marketplace.IsCloned)
            {
                return (false, marketplace.ErrorMessage ?? "Failed to clone repository.", null);
            }

            // Add to user config
            if (!MarketplaceStorageService.AddMarketplace(owner, repo, branch, repositoryUrl))
            {
                // Already exists in config
                return (true, null, marketplace);
            }

            return (true, null, marketplace);
        }

        /// <summary>
        /// Removes a user MarketplaceInfo.
        /// </summary>
        public static bool RemoveMarketplace(string owner, string repo, string repositoryUrl = null, bool deleteClone = true)
        {
            // Can't remove built-in marketplaces
            if (MarketplaceStorageService.IsBuiltIn(owner, repo, repositoryUrl))
            {
                return false;
            }

            // Remove from config
            MarketplaceStorageService.RemoveMarketplace(owner, repo, repositoryUrl);

            // Remove from cache
            var id = MarketplaceStorageService.GetMarketplaceId(owner, repo, repositoryUrl);
            lock (_cacheLock)
            {
                _marketplaceCache.Remove(id);
            }

            // Optionally delete the clone
            if (deleteClone)
            {
                MarketplaceStorageService.DeleteMarketplaceClone(owner, repo, repositoryUrl);
            }

            return true;
        }

        /// <summary>
        /// Gets all assets of a specific type across all marketplaces.
        /// </summary>
        public static async Task<List<PluginAsset>> GetAllAssetsAsync(
            AssetType type,
            CancellationToken cancellationToken = default)
        {
            var assets = new List<PluginAsset>();
            var marketplaces = await GetAllMarketplacesAsync(forceRefresh: false, cancellationToken);

            foreach (var marketplace in marketplaces)
            {
                foreach (var asset in marketplace.GetAllAssets(type))
                {
                    assets.Add(asset);
                }
            }

            return assets;
        }

        /// <summary>
        /// Gets marketplaces that have assets of a specific type.
        /// </summary>
        public static async Task<List<MarketplaceInfo>> GetMarketplacesWithAssetTypeAsync(
            AssetType type,
            CancellationToken cancellationToken = default)
        {
            var result = new List<MarketplaceInfo>();
            var marketplaces = await GetAllMarketplacesAsync(forceRefresh: false, cancellationToken);

            foreach (var marketplace in marketplaces)
            {
                if (marketplace.HasAssetType(type))
                {
                    result.Add(marketplace);
                }
            }

            return result;
        }

        /// <summary>
        /// Reads the content of an asset file.
        /// </summary>
        public static string GetAssetContent(PluginAsset asset)
        {
            if (asset == null || string.IsNullOrEmpty(asset.LocalPath))
            {
                return null;
            }

            try
            {
                if (File.Exists(asset.LocalPath))
                {
                    return File.ReadAllText(asset.LocalPath);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceService.GetAssetContent failed: {ex}");
            }

            return null;
        }

        /// <summary>
        /// Refreshes a specific MarketplaceInfo (git pull).
        /// </summary>
        public static async Task<MarketplaceInfo> RefreshMarketplaceAsync(
            string owner,
            string repo,
            string repositoryUrl = null,
            CancellationToken cancellationToken = default)
        {
            // Get the branch from existing config or detect from remote
            var config = MarketplaceStorageService.LoadConfig();
            string branch = null;
            string configuredRepositoryUrl = repositoryUrl;

            foreach (var entry in config.Marketplaces)
            {
                if (string.Equals(MarketplaceStorageService.GetMarketplaceId(entry.Owner, entry.Repo, entry.RepositoryUrl),
                        MarketplaceStorageService.GetMarketplaceId(owner, repo, repositoryUrl),
                        StringComparison.OrdinalIgnoreCase))
                {
                    branch = entry.Branch;
                    configuredRepositoryUrl = entry.RepositoryUrl;
                    break;
                }
            }

            if (string.IsNullOrEmpty(branch))
            {
                foreach (var builtIn in MarketplaceStorageService.BuiltInMarketplaces)
                {
                    if (string.Equals(builtIn.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                        string.Equals(builtIn.Repo, repo, StringComparison.OrdinalIgnoreCase))
                    {
                        branch = builtIn.Branch;
                        break;
                    }
                }
            }

            return await GetMarketplaceAsync(owner, repo, branch, configuredRepositoryUrl, forceRefresh: true, cancellationToken: cancellationToken);
        }

        /// <summary>
        /// Clears the in-memory cache.
        /// </summary>
        public static void ClearCache()
        {
            lock (_cacheLock)
            {
                _marketplaceCache.Clear();
                _initialLoadComplete = false;
            }
        }

        /// <summary>
        /// Parses MarketplaceInfo input in various formats.
        /// </summary>
        private static (string owner, string repo, string branch, string repositoryUrl) ParseMarketplaceInput(string input)
        {
            return MarketplaceRepositoryUrl.TryParseInput(input, out var owner, out var repo, out var branch, out var repositoryUrl)
                ? (owner, repo, branch, repositoryUrl)
                : (null, null, null, null);
        }
    }
}
