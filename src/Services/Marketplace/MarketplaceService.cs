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
            var tasks = entries.Select(entry => GetMarketplaceAsync(entry, forceRefresh, config.UpdateIntervalHours, cancellationToken));

            var results = await Task.WhenAll(tasks);

            _initialLoadComplete = true;
            return results.Where(m => m != null).ToList();
        }

        private static async Task<MarketplaceInfo> GetMarketplaceAsync(
            MarketplaceEntry entry,
            bool forceRefresh,
            int updateIntervalHours,
            CancellationToken cancellationToken)
        {
            if (entry.SourceKind == MarketplaceSourceKind.AgentSkillsDiscovery)
            {
                return await GetAgentSkillsDiscoveryMarketplaceAsync(entry, forceRefresh, cancellationToken);
            }

            return await GetMarketplaceAsync(
                entry.Owner,
                entry.Repo,
                entry.Branch,
                entry.RepositoryUrl,
                forceRefresh,
                updateIntervalHours,
                cancellationToken);
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
            if (AgentSkillsDiscoveryService.TryCreateIndexUri(input, out var indexUri))
            {
                var entry = new MarketplaceEntry
                {
                    SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                    Owner = indexUri.Host,
                    Repo = "agent-skills",
                    RepositoryUrl = indexUri.AbsoluteUri,
                    AgentSkillsIndexUrl = indexUri.AbsoluteUri,
                    DisplayName = AgentSkillsDiscoveryService.GetDisplayName(indexUri),
                    IsTrusted = true
                };

                var discoveredMarketplace = await GetAgentSkillsDiscoveryMarketplaceAsync(entry, forceRefresh: true, cancellationToken);
                if (!discoveredMarketplace.IsCloned)
                {
                    return (false, discoveredMarketplace.ErrorMessage ?? "Failed to load Agent Skills Discovery source.", null);
                }

                if (AgentSkillsDiscoveryService.TryCreateIndexUri(discoveredMarketplace.SourceUrl, out var discoveredIndexUri))
                {
                    MarketplaceStorageService.AddAgentSkillsDiscoverySource(discoveredIndexUri, discoveredMarketplace.DisplayName, trusted: true);
                }

                return (true, null, discoveredMarketplace);
            }

            // Parse input (supports "owner/repo" or "https://github.com/owner/repo")
            var (owner, repo, branch, repositoryUrl) = ParseMarketplaceInput(input);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return (false, "Invalid format. Use 'owner/repo', a repository URL, a domain, or an Agent Skills Discovery index URL.", null);
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
            if (AgentSkillsDiscoveryService.TryCreateIndexUri(repositoryUrl, out var indexUri))
            {
                MarketplaceStorageService.RemoveAgentSkillsDiscoverySource(indexUri.AbsoluteUri);

                var sourceId = AgentSkillsDiscoveryService.GetSourceId(indexUri);
                lock (_cacheLock)
                {
                    _marketplaceCache.Remove(sourceId);
                }

                if (deleteClone)
                {
                    DeleteDirectory(MarketplaceStorageService.GetAgentSkillsDiscoveryDirectory(indexUri));
                }

                return true;
            }

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
                _ = ex.LogAsync();
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

            if (AgentSkillsDiscoveryService.TryCreateIndexUri(repositoryUrl, out var requestedIndexUri))
            {
                var requestedSourceId = AgentSkillsDiscoveryService.GetSourceId(requestedIndexUri);
                foreach (var entry in config.Marketplaces)
                {
                    if (entry.SourceKind == MarketplaceSourceKind.AgentSkillsDiscovery &&
                        AgentSkillsDiscoveryService.TryCreateIndexUri(entry.AgentSkillsIndexUrl ?? entry.RepositoryUrl, out var entryIndexUri) &&
                        string.Equals(AgentSkillsDiscoveryService.GetSourceId(entryIndexUri), requestedSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return await GetAgentSkillsDiscoveryMarketplaceAsync(entry, forceRefresh: true, cancellationToken);
                    }
                }

                return await GetAgentSkillsDiscoveryMarketplaceAsync(new MarketplaceEntry
                {
                    SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                    Owner = requestedIndexUri.Host,
                    Repo = "agent-skills",
                    RepositoryUrl = requestedIndexUri.AbsoluteUri,
                    AgentSkillsIndexUrl = requestedIndexUri.AbsoluteUri,
                    DisplayName = AgentSkillsDiscoveryService.GetDisplayName(requestedIndexUri),
                    IsTrusted = true
                }, forceRefresh: true, cancellationToken);
            }

            foreach (var entry in config.Marketplaces)
            {
                if (entry.SourceKind != MarketplaceSourceKind.Repository)
                {
                    continue;
                }

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

        private static async Task<MarketplaceInfo> GetAgentSkillsDiscoveryMarketplaceAsync(
            MarketplaceEntry entry,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            if (!AgentSkillsDiscoveryService.TryCreateIndexUri(entry.AgentSkillsIndexUrl ?? entry.RepositoryUrl, out var indexUri))
            {
                return new MarketplaceInfo
                {
                    Id = entry.AgentSkillsIndexUrl ?? entry.RepositoryUrl ?? entry.Owner,
                    Owner = entry.Owner,
                    RepoName = entry.Repo,
                    SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                    RepositoryUrl = entry.RepositoryUrl,
                    SourceUrl = entry.AgentSkillsIndexUrl ?? entry.RepositoryUrl,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Agent Skills" : entry.DisplayName,
                    IsCloned = false,
                    ErrorMessage = "Invalid Agent Skills Discovery URL."
                };
            }

            var id = AgentSkillsDiscoveryService.GetSourceId(indexUri);
            if (!entry.IsTrusted)
            {
                return new MarketplaceInfo
                {
                    Id = id,
                    Owner = indexUri.Host,
                    RepoName = "agent-skills",
                    SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                    RepositoryUrl = indexUri.AbsoluteUri,
                    SourceUrl = indexUri.AbsoluteUri,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? AgentSkillsDiscoveryService.GetDisplayName(indexUri) : entry.DisplayName,
                    IsCloned = false,
                    ErrorMessage = "Agent Skills Discovery source is not trusted. Remove and add it again to confirm trust."
                };
            }

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

            try
            {
                var result = await AgentSkillsDiscoveryService.DiscoverAsync(entry, forceRefresh, cancellationToken);
                var plugin = new MarketplacePlugin
                {
                    Name = "Agent Skills",
                    Description = $"Discovered from {result.Origin}",
                    Source = result.IndexUri.AbsoluteUri,
                    MarketplaceId = result.Id
                };

                foreach (var skill in result.Skills)
                {
                    plugin.Assets.Add(new PluginAsset
                    {
                        Type = AssetType.Skill,
                        Name = skill.Name,
                        Description = skill.Description,
                        RelativePath = skill.Name,
                        LocalPath = skill.LocalSkillPath,
                        PluginName = result.DisplayName,
                        MarketplaceId = result.Id
                    });
                }

                var marketplace = new MarketplaceInfo
                {
                    Id = result.Id,
                    Owner = result.IndexUri.Host,
                    RepoName = "agent-skills",
                    SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                    RepositoryUrl = result.IndexUri.AbsoluteUri,
                    SourceUrl = result.IndexUri.AbsoluteUri,
                    DisplayName = result.DisplayName,
                    Description = $"Agent Skills Discovery source at {result.Origin}",
                    IsBuiltIn = false,
                    LocalPath = result.CacheDirectory,
                    IconPath = result.IconPath,
                    IsCloned = true,
                    LastUpdated = result.LastUpdated,
                    ErrorMessage = result.Skills.Count == 0 && result.Warnings.Count > 0 ? string.Join(" ", result.Warnings) : null,
                    Plugins = new List<MarketplacePlugin> { plugin }
                };

                lock (_cacheLock)
                {
                    _marketplaceCache[id] = marketplace;
                }

                return marketplace;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceService.GetAgentSkillsDiscoveryMarketplaceAsync failed for '{indexUri}': {ex}");
                _ = ex.LogAsync();

                lock (_cacheLock)
                {
                    if (_marketplaceCache.TryGetValue(id, out var cached))
                    {
                        cached.ErrorMessage = ex.Message;
                        return cached;
                    }
                }

                return new MarketplaceInfo
                {
                    Id = id,
                    Owner = indexUri.Host,
                    RepoName = "agent-skills",
                    SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                    RepositoryUrl = indexUri.AbsoluteUri,
                    SourceUrl = indexUri.AbsoluteUri,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? AgentSkillsDiscoveryService.GetDisplayName(indexUri) : entry.DisplayName,
                    IsCloned = false,
                    ErrorMessage = ex.Message
                };
            }
        }

        private static void DeleteDirectory(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
            {
                return;
            }

            try
            {
                Directory.Delete(path, recursive: true);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceService.DeleteDirectory failed for '{path}': {ex}");
                _ = ex.LogAsync();
            }
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
