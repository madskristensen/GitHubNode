using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
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
        private static bool _initialLoadComplete;

        /// <summary>
        /// Gets all registered marketplaces (built-in and user-added).
        /// Clones repositories if not already cloned.
        /// </summary>
        public static async Task<List<MarketplaceInfo>> GetAllMarketplacesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            var entries = MarketplaceStorageService.GetAllMarketplaceEntries();
            var marketplaces = new List<MarketplaceInfo>();
            var config = MarketplaceStorageService.LoadConfig();

            foreach (var entry in entries)
            {
                cancellationToken.ThrowIfCancellationRequested();

                var MarketplaceInfo = await GetMarketplaceAsync(
                    entry.Owner,
                    entry.Repo,
                    entry.Branch,
                    forceRefresh,
                    config.UpdateIntervalHours,
                    cancellationToken);

                if (MarketplaceInfo != null)
                {
                    marketplaces.Add(MarketplaceInfo);
                }
            }

            _initialLoadComplete = true;
            return marketplaces;
        }

        /// <summary>
        /// Gets a specific MarketplaceInfo by owner and repo.
        /// </summary>
        public static async Task<MarketplaceInfo> GetMarketplaceAsync(
            string owner,
            string repo,
            string branch = "main",
            bool forceRefresh = false,
            int updateIntervalHours = 24,
            CancellationToken cancellationToken = default)
        {
            var id = MarketplaceStorageService.GetMarketplaceId(owner, repo);
            var localPath = MarketplaceStorageService.GetMarketplaceDirectory(owner, repo);

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

            // Determine if we need to clone/update
            var needsUpdate = forceRefresh || !MarketplaceGitService.IsCloned(owner, repo);
            if (!needsUpdate && _initialLoadComplete)
            {
                needsUpdate = MarketplaceGitService.NeedsUpdate(owner, repo, updateIntervalHours);
            }

            if (needsUpdate)
            {
                var gitResult = await MarketplaceGitService.CloneOrUpdateAsync(owner, repo, branch, cancellationToken);
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
                        Branch = branch,
                        DisplayName = $"{owner}/{repo}",
                        IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo),
                        IsCloned = false,
                        ErrorMessage = gitResult.Error
                    };
                }
            }

            // Parse the MarketplaceInfo
            var MarketplaceInfo = MarketplaceParserService.ParseMarketplace(owner, repo, localPath);
            MarketplaceInfo.Branch = branch;

            // Update cache
            lock (_cacheLock)
            {
                _marketplaceCache[id] = MarketplaceInfo;
            }

            return MarketplaceInfo;
        }

        /// <summary>
        /// Adds a new user MarketplaceInfo by GitHub URL or owner/repo format.
        /// </summary>
        public static async Task<(bool success, string error, MarketplaceInfo MarketplaceInfo)> AddMarketplaceAsync(
            string input,
            CancellationToken cancellationToken = default)
        {
            // Parse input (supports "owner/repo" or "https://github.com/owner/repo")
            var (owner, repo, branch) = ParseMarketplaceInput(input);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return (false, "Invalid format. Use 'owner/repo' or a GitHub URL.", null);
            }

            // Check if it's a built-in
            if (MarketplaceStorageService.IsBuiltIn(owner, repo))
            {
                return (false, $"{owner}/{repo} is a built-in MarketplaceInfo and is already available.", null);
            }

            // Try to clone and validate
            var MarketplaceInfo = await GetMarketplaceAsync(owner, repo, branch, forceRefresh: true, cancellationToken: cancellationToken);

            if (!MarketplaceInfo.IsCloned)
            {
                return (false, MarketplaceInfo.ErrorMessage ?? "Failed to clone repository.", null);
            }

            // Add to user config
            if (!MarketplaceStorageService.AddMarketplace(owner, repo, branch))
            {
                // Already exists in config
                return (true, null, MarketplaceInfo);
            }

            return (true, null, MarketplaceInfo);
        }

        /// <summary>
        /// Removes a user MarketplaceInfo.
        /// </summary>
        public static bool RemoveMarketplace(string owner, string repo, bool deleteClone = true)
        {
            // Can't remove built-in marketplaces
            if (MarketplaceStorageService.IsBuiltIn(owner, repo))
            {
                return false;
            }

            // Remove from config
            MarketplaceStorageService.RemoveMarketplace(owner, repo);

            // Remove from cache
            var id = MarketplaceStorageService.GetMarketplaceId(owner, repo);
            lock (_cacheLock)
            {
                _marketplaceCache.Remove(id);
            }

            // Optionally delete the clone
            if (deleteClone)
            {
                MarketplaceStorageService.DeleteMarketplaceClone(owner, repo);
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

            foreach (var MarketplaceInfo in marketplaces)
            {
                foreach (var asset in MarketplaceInfo.GetAllAssets(type))
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

            foreach (var MarketplaceInfo in marketplaces)
            {
                if (MarketplaceInfo.HasAssetType(type))
                {
                    result.Add(MarketplaceInfo);
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
            CancellationToken cancellationToken = default)
        {
            // Get the branch from existing config or default
            var config = MarketplaceStorageService.LoadConfig();
            var branch = "main";

            foreach (var entry in config.Marketplaces)
            {
                if (string.Equals(entry.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Repo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    branch = entry.Branch ?? "main";
                    break;
                }
            }

            foreach (var builtIn in MarketplaceStorageService.BuiltInMarketplaces)
            {
                if (string.Equals(builtIn.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(builtIn.Repo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    branch = builtIn.Branch ?? "main";
                    break;
                }
            }

            return await GetMarketplaceAsync(owner, repo, branch, forceRefresh: true, cancellationToken: cancellationToken);
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
        private static (string owner, string repo, string branch) ParseMarketplaceInput(string input)
        {
            if (string.IsNullOrWhiteSpace(input))
            {
                return (null, null, "main");
            }

            input = input.Trim();

            // Handle GitHub URLs
            if (input.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase) ||
                input.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
            {
                // Remove protocol and domain
                var path = input;
                if (path.StartsWith("https://github.com/", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring("https://github.com/".Length);
                }
                else if (path.StartsWith("http://github.com/", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring("http://github.com/".Length);
                }

                // Remove .git suffix if present
                if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(0, path.Length - 4);
                }

                // Remove trailing slashes
                path = path.TrimEnd('/');

                input = path;
            }

            // Handle git@ URLs
            if (input.StartsWith("git@github.com:", StringComparison.OrdinalIgnoreCase))
            {
                var path = input.Substring("git@github.com:".Length);
                if (path.EndsWith(".git", StringComparison.OrdinalIgnoreCase))
                {
                    path = path.Substring(0, path.Length - 4);
                }

                input = path;
            }

            // Parse owner/repo format
            var parts = input.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length >= 2)
            {
                // Could have branch: owner/repo/tree/branch
                var owner = parts[0];
                var repo = parts[1];
                var branch = "main";

                if (parts.Length >= 4 && string.Equals(parts[2], "tree", StringComparison.OrdinalIgnoreCase))
                {
                    branch = parts[3];
                }

                return (owner, repo, branch);
            }

            return (null, null, "main");
        }
    }
}
