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
        /// Returns lightweight placeholder MarketplaceInfo objects for every
        /// marketplace registered in the on-disk config (built-in + user-added),
        /// in the exact order they appear in the config file. This method is
        /// fully synchronous and does no I/O beyond reading the config file, so
        /// it is safe to call directly from the UI thread to populate a list
        /// instantly before any cache hydration or background sync work runs.
        /// Each returned item exposes any in-memory cached state when available
        /// (so previously hydrated marketplaces appear as already cloned/synced),
        /// but no disk parse, no git fetch, and no discovery is performed.
        /// </summary>
        public static List<MarketplaceInfo> GetAllMarketplacePlaceholders()
        {
            var entries = MarketplaceStorageService.GetAllMarketplaceEntries();
            var placeholders = new List<MarketplaceInfo>(entries.Count);

            foreach (var entry in entries)
            {
                var placeholder = CreatePlaceholderFromEntry(entry);
                if (placeholder != null)
                {
                    placeholders.Add(placeholder);
                }
            }

            return placeholders;
        }

        private static MarketplaceInfo CreatePlaceholderFromEntry(MarketplaceEntry entry)
        {
            if (entry == null)
            {
                return null;
            }

            if (entry.SourceKind == MarketplaceSourceKind.WellKnownDiscovery)
            {
                if (!WellKnownDiscoveryService.TryCreateOriginUri(entry.WellKnownIndexUrl ?? entry.RepositoryUrl, out var originUri))
                {
                    return null;
                }

                var sourceId = WellKnownDiscoveryService.GetSourceId(originUri);
                lock (_cacheLock)
                {
                    if (_marketplaceCache.TryGetValue(sourceId, out var cached) && cached != null)
                    {
                        return cached;
                    }
                }

                var displayUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri);
                return new MarketplaceInfo
                {
                    Id = sourceId,
                    Owner = originUri.Host,
                    RepoName = "well-known",
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    RepositoryUrl = displayUrl,
                    SourceUrl = displayUrl,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName)
                        ? WellKnownDiscoveryService.GetDisplayName(originUri)
                        : entry.DisplayName,
                    IconPath = MarketplaceStorageService.FindExistingWellKnownIconPath(originUri),
                    IsCloned = false
                };
            }

            var owner = entry.Owner;
            var repo = entry.Repo;
            var repositoryUrl = entry.RepositoryUrl;
            var id = MarketplaceStorageService.GetMarketplaceId(owner, repo, repositoryUrl);

            lock (_cacheLock)
            {
                if (_marketplaceCache.TryGetValue(id, out var cached) && cached != null)
                {
                    return cached;
                }
            }

            return new MarketplaceInfo
            {
                Id = id,
                Owner = owner,
                RepoName = repo,
                RepositoryUrl = string.IsNullOrWhiteSpace(repositoryUrl) ? null : MarketplaceRepositoryUrl.GetRepositoryUrl(owner, repo, repositoryUrl),
                Branch = entry.Branch,
                DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? $"{owner}/{repo}" : entry.DisplayName,
                IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo, repositoryUrl),
                IconPath = MarketplaceStorageService.FindExistingIconPath(owner, repo, repositoryUrl),
                IsCloned = false
            };
        }

        /// <summary>
        /// Gets all registered marketplaces (built-in and user-added).
        /// Clones repositories if not already cloned.
        /// </summary>
        /// <param name="cacheOnly">
        /// When true, only returns data already available locally (in-memory cache
        /// or already-cloned repositories on disk). No git fetch, clone, or
        /// discovery network calls are performed. Use this for fast startup loads
        /// where the user can request a refresh explicitly.
        /// </param>
        /// <param name="progress">
        /// Optional progress receiver. When provided, each marketplace is reported
        /// as soon as it finishes loading, allowing callers (such as the marketplace
        /// tool window) to render results incrementally instead of waiting for the
        /// slowest entry.
        /// </param>
        public static async Task<List<MarketplaceInfo>> GetAllMarketplacesAsync(
            bool forceRefresh = false,
            CancellationToken cancellationToken = default,
            IProgress<MarketplaceInfo> progress = null,
            bool cacheOnly = false)
        {
            var entries = MarketplaceStorageService.GetAllMarketplaceEntries();
            var config = MarketplaceStorageService.LoadConfig();

            // Fetch all marketplaces in parallel for better performance.
            // Report each one to the progress receiver as soon as it completes so
            // the UI can render cached entries instantly while slower ones (clone,
            // fetch, parse) finish in the background.
            var tasks = entries.Select(async entry =>
            {
                MarketplaceInfo marketplace;
                if (cacheOnly)
                {
                    marketplace = await GetMarketplaceFromCacheAsync(entry, cancellationToken);
                }
                else
                {
                    marketplace = await GetMarketplaceAsync(entry, forceRefresh, config.UpdateIntervalHours, cancellationToken);
                }

                if (marketplace != null)
                {
                    progress?.Report(marketplace);
                }
                return marketplace;
            });

            var results = await Task.WhenAll(tasks);

            if (!cacheOnly)
            {
                _initialLoadComplete = true;
            }

            return results.Where(m => m != null).ToList();
        }

        /// <summary>
        /// Returns a marketplace using only locally available data: the in-memory
        /// cache, or a previously cloned repository on disk. Never performs git
        /// fetch, clone, or discovery network calls. Returns a lightweight
        /// placeholder (IsCloned = false) when nothing is locally available, so
        /// the UI can still show the entry and let the user trigger a refresh.
        /// </summary>
        /// <summary>
        /// Hydrates a single marketplace from local data only (in-memory cache,
        /// cloned-on-disk repository, or cached well-known manifest). Performs no
        /// git fetch, clone, or discovery network calls. Returns null when the
        /// marketplace has no local data yet so the caller can keep the
        /// placeholder it already rendered.
        /// </summary>
        public static async Task<MarketplaceInfo> GetMarketplaceFromLocalCacheAsync(
            string id,
            string owner,
            string repo,
            string repositoryUrl,
            bool isWellKnownDiscovery,
            CancellationToken cancellationToken)
        {
            var entry = new MarketplaceEntry
            {
                Owner = owner,
                Repo = repo,
                RepositoryUrl = repositoryUrl,
                SourceKind = isWellKnownDiscovery ? MarketplaceSourceKind.WellKnownDiscovery : MarketplaceSourceKind.Repository,
                WellKnownIndexUrl = isWellKnownDiscovery ? repositoryUrl : null
            };

            var info = await GetMarketplaceFromCacheAsync(entry, cancellationToken);
            return info;
        }

        private static async Task<MarketplaceInfo> GetMarketplaceFromCacheAsync(
            MarketplaceEntry entry,
            CancellationToken cancellationToken)
        {
            if (entry.SourceKind == MarketplaceSourceKind.WellKnownDiscovery)
            {
                if (WellKnownDiscoveryService.TryCreateOriginUri(entry.WellKnownIndexUrl ?? entry.RepositoryUrl, out var originUri))
                {
                    var sourceId = WellKnownDiscoveryService.GetSourceId(originUri);
                    lock (_cacheLock)
                    {
                        if (_marketplaceCache.TryGetValue(sourceId, out var cached) && cached.IsCloned)
                        {
                            return cached;
                        }
                    }

                    // Try to hydrate from the on-disk cache manifest written by
                    // the previous discovery run so the UI can show the source
                    // as Synced without any network calls.
                    var fromDisk = TryLoadWellKnownMarketplaceFromDisk(entry, originUri);
                    if (fromDisk != null)
                    {
                        lock (_cacheLock)
                        {
                            _marketplaceCache[sourceId] = fromDisk;
                        }

                        return fromDisk;
                    }

                    return new MarketplaceInfo
                    {
                        Id = sourceId,
                        Owner = originUri.Host,
                        RepoName = "well-known",
                        SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                        RepositoryUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri),
                        SourceUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri),
                        DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? WellKnownDiscoveryService.GetDisplayName(originUri) : entry.DisplayName,
                        IsCloned = false
                    };
                }

                return null;
            }

            var owner = entry.Owner;
            var repo = entry.Repo;
            var repositoryUrl = entry.RepositoryUrl;
            var id = MarketplaceStorageService.GetMarketplaceId(owner, repo, repositoryUrl);

            lock (_cacheLock)
            {
                if (_marketplaceCache.TryGetValue(id, out var cached) && cached.IsCloned)
                {
                    return cached;
                }
            }

            // Not in memory cache yet, but we may have a previous clone on disk.
            // Parse it without fetching so the user sees content instantly.
            if (MarketplaceGitService.IsCloned(owner, repo, repositoryUrl))
            {
                var localPath = MarketplaceStorageService.GetMarketplaceDirectory(owner, repo, repositoryUrl);
                try
                {
                    var marketplaceInfo = await MarketplaceParserService.ParseMarketplaceAsync(
                        owner, repo, localPath, repositoryUrl, cloneLinkedRepos: false, cancellationToken);
                    marketplaceInfo.Branch = entry.Branch;

                    lock (_cacheLock)
                    {
                        _marketplaceCache[id] = marketplaceInfo;
                    }

                    return marketplaceInfo;
                }
                catch (OperationCanceledException)
                {
                    throw;
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MarketplaceService.GetMarketplaceFromCacheAsync parse failed for {id}: {ex}");
                    _ = ex.LogAsync();
                }
            }

            // Nothing locally available; return a placeholder so the entry still
            // shows up in the UI and the user can request a refresh.
            return new MarketplaceInfo
            {
                Id = id,
                Owner = owner,
                RepoName = repo,
                RepositoryUrl = string.IsNullOrWhiteSpace(repositoryUrl) ? null : MarketplaceRepositoryUrl.GetRepositoryUrl(owner, repo, repositoryUrl),
                Branch = entry.Branch,
                DisplayName = $"{owner}/{repo}",
                IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo, repositoryUrl),
                IsCloned = false
            };
        }

        private static async Task<MarketplaceInfo> GetMarketplaceAsync(
            MarketplaceEntry entry,
            bool forceRefresh,
            int updateIntervalHours,
            CancellationToken cancellationToken)
        {
            if (entry.SourceKind == MarketplaceSourceKind.WellKnownDiscovery)
            {
                return await GetWellKnownDiscoveryMarketplaceAsync(entry, forceRefresh, cancellationToken);
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
                        // If cached entry is missing IconPath, try to download avatar asynchronously
                        // This handles cases like built-in repos that were cached before avatar download was implemented
                        if (string.IsNullOrEmpty(cached.IconPath))
                        {
                            _ = Task.Run(async () =>
                            {
                                try
                                {
                                    if (MarketplaceParserService.IsGitHubRepository(repositoryUrl))
                                    {
                                        var avatarPath = await MarketplaceParserService.DownloadGitHubAvatarAsync(owner, repo, repositoryUrl, cancellationToken);
                                        if (!string.IsNullOrEmpty(avatarPath))
                                        {
                                            cached.IconPath = avatarPath;
                                        }
                                    }
                                }
                                catch (Exception ex)
                                {
                                    Debug.WriteLine($"MarketplaceService: Failed to fetch avatar for cached {id}: {ex}");
                                }
                            }, cancellationToken);
                        }

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
            if (WellKnownDiscoveryService.TryCreateOriginUri(input, out var originUri))
            {
                var entry = new MarketplaceEntry
                {
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    Owner = originUri.Host,
                    Repo = "well-known",
                    RepositoryUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri),
                    WellKnownIndexUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri),
                    DisplayName = WellKnownDiscoveryService.GetDisplayName(originUri),
                    IsTrusted = true
                };

                var discoveredMarketplace = await GetWellKnownDiscoveryMarketplaceAsync(entry, forceRefresh: true, cancellationToken);
                if (!discoveredMarketplace.IsCloned)
                {
                    return (false, discoveredMarketplace.ErrorMessage ?? "Failed to load Well-Known Discovery source.", null);
                }

                if (WellKnownDiscoveryService.TryCreateOriginUri(discoveredMarketplace.SourceUrl, out var discoveredOriginUri))
                {
                    MarketplaceStorageService.AddWellKnownDiscoverySource(discoveredOriginUri, discoveredMarketplace.DisplayName, trusted: true);
                }

                return (true, null, discoveredMarketplace);
            }

            // Parse input (supports "owner/repo" or "https://github.com/owner/repo")
            var (owner, repo, branch, repositoryUrl) = ParseMarketplaceInput(input);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return (false, "Invalid format. Use 'owner/repo', a repository URL, a domain, or an Well-Known Discovery index URL.", null);
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
            if (WellKnownDiscoveryService.TryCreateOriginUri(repositoryUrl, out var originUri))
            {
                MarketplaceStorageService.RemoveWellKnownDiscoverySource(originUri.AbsoluteUri);

                var sourceId = WellKnownDiscoveryService.GetSourceId(originUri);
                lock (_cacheLock)
                {
                    _marketplaceCache.Remove(sourceId);
                }

                if (deleteClone)
                {
                    DeleteDirectory(MarketplaceStorageService.GetWellKnownDiscoveryDirectory(originUri));
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

            if (WellKnownDiscoveryService.TryCreateOriginUri(repositoryUrl, out var requestedOriginUri))
            {
                var requestedSourceId = WellKnownDiscoveryService.GetSourceId(requestedOriginUri);
                foreach (var entry in config.Marketplaces)
                {
                    if (entry.SourceKind == MarketplaceSourceKind.WellKnownDiscovery &&
                        WellKnownDiscoveryService.TryCreateOriginUri(entry.WellKnownIndexUrl ?? entry.RepositoryUrl, out var entryOriginUri) &&
                        string.Equals(WellKnownDiscoveryService.GetSourceId(entryOriginUri), requestedSourceId, StringComparison.OrdinalIgnoreCase))
                    {
                        return await GetWellKnownDiscoveryMarketplaceAsync(entry, forceRefresh: true, cancellationToken);
                    }
                }

                return await GetWellKnownDiscoveryMarketplaceAsync(new MarketplaceEntry
                {
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    Owner = requestedOriginUri.Host,
                    Repo = "well-known",
                    RepositoryUrl = WellKnownDiscoveryService.GetDisplayUrl(requestedOriginUri),
                    WellKnownIndexUrl = WellKnownDiscoveryService.GetDisplayUrl(requestedOriginUri),
                    DisplayName = WellKnownDiscoveryService.GetDisplayName(requestedOriginUri),
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

        private static async Task<MarketplaceInfo> GetWellKnownDiscoveryMarketplaceAsync(
            MarketplaceEntry entry,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            if (!WellKnownDiscoveryService.TryCreateOriginUri(entry.WellKnownIndexUrl ?? entry.RepositoryUrl, out var originUri))
            {
                return new MarketplaceInfo
                {
                    Id = entry.WellKnownIndexUrl ?? entry.RepositoryUrl ?? entry.Owner,
                    Owner = entry.Owner,
                    RepoName = entry.Repo,
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    RepositoryUrl = entry.RepositoryUrl,
                    SourceUrl = entry.WellKnownIndexUrl ?? entry.RepositoryUrl,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? "Well-Known" : entry.DisplayName,
                    IsCloned = false,
                    ErrorMessage = "Invalid Well-Known Discovery URL."
                };
            }

            var displayUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri);
            var id = WellKnownDiscoveryService.GetSourceId(originUri);
            if (!entry.IsTrusted)
            {
                return new MarketplaceInfo
                {
                    Id = id,
                    Owner = originUri.Host,
                    RepoName = "well-known",
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    RepositoryUrl = displayUrl,
                    SourceUrl = displayUrl,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? WellKnownDiscoveryService.GetDisplayName(originUri) : entry.DisplayName,
                    IsCloned = false,
                    ErrorMessage = "Well-Known Discovery source is not trusted. Remove and add it again to confirm trust."
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
                var result = await WellKnownDiscoveryService.DiscoverAsync(entry, forceRefresh, cancellationToken);

                // Also try to discover MCP servers from the same origin. This is
                // a separate well-known sub type that is surfaced as additional
                // assets on the same marketplace entry.
                McpServerDiscoveryResult mcpResult = null;
                try
                {
                    mcpResult = await McpServerDiscoveryService.DiscoverAsync(originUri, forceRefresh: false, cancellationToken: cancellationToken);
                }
                catch
                {
                    // MCP discovery is optional; if it fails, continue with just skills
                }

                // Create a generic plugin that can contain assets of any
                // well-known sub type (skills, MCP servers, ...).
                var plugin = new MarketplacePlugin
                {
                    Name = "Well-Known Marketplace",
                    Description = $"Discovered from {result.Origin}",
                    Source = displayUrl,
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

                if (mcpResult != null && mcpResult.Servers != null && mcpResult.Servers.Count > 0)
                {
                    string mcpConfigPath = WriteWellKnownMcpConfig(result.CacheDirectory, mcpResult.Servers);

                    foreach (var server in mcpResult.Servers)
                    {
                        plugin.Assets.Add(new PluginAsset
                        {
                            Type = AssetType.McpServer,
                            Name = server.Name,
                            Description = server.Description,
                            RelativePath = server.Name,
                            LocalPath = mcpConfigPath ?? server.ArtifactUri?.AbsoluteUri,
                            PluginName = result.DisplayName,
                            MarketplaceId = result.Id
                        });
                    }
                }

                var marketplace = new MarketplaceInfo
                {
                    Id = result.Id,
                    Owner = originUri.Host,
                    RepoName = "well-known",
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    RepositoryUrl = displayUrl,
                    SourceUrl = displayUrl,
                    DisplayName = result.DisplayName,
                    Description = $"Well-Known Marketplace source at {result.Origin}",
                    IsBuiltIn = false,
                    LocalPath = result.CacheDirectory,
                    IconPath = result.IconPath,
                    IsCloned = true,
                    LastUpdated = result.LastUpdated,
                    ErrorMessage = result.Skills.Count == 0 && (mcpResult?.Servers.Count ?? 0) == 0 && result.Warnings.Count > 0 ? string.Join(" ", result.Warnings) : null,
                    Plugins = new List<MarketplacePlugin> { plugin }
                };

                SaveWellKnownMarketplaceManifest(marketplace);

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
                Debug.WriteLine($"MarketplaceService.GetWellKnownDiscoveryMarketplaceAsync failed for '{originUri}': {ex}");
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
                    Owner = originUri.Host,
                    RepoName = "well-known",
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    RepositoryUrl = displayUrl,
                    SourceUrl = displayUrl,
                    DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? WellKnownDiscoveryService.GetDisplayName(originUri) : entry.DisplayName,
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
        /// Writes the discovered well-known MCP servers to a local mcp.json file in the
        /// cache directory using the standard "mcpServers" object format. Returns the path
        /// of the written file, or null if it could not be written.
        /// </summary>
        private static string WriteWellKnownMcpConfig(string cacheDirectory, IList<McpServerDefinition> servers)
        {
            if (string.IsNullOrWhiteSpace(cacheDirectory) || servers == null || servers.Count == 0)
            {
                return null;
            }

            try
            {
                Directory.CreateDirectory(cacheDirectory);
                string targetPath = Path.Combine(cacheDirectory, "well-known.mcp.json");

                var mcpServers = new Dictionary<string, object>(StringComparer.Ordinal);
                foreach (var server in servers)
                {
                    if (string.IsNullOrWhiteSpace(server.Name) || server.ArtifactUri == null)
                    {
                        continue;
                    }

                    // Well-known MCP discovery currently surfaces remote servers; emit the
                    // standard HTTP transport entry with the resolved URL.
                    mcpServers[server.Name] = new Dictionary<string, object>(StringComparer.Ordinal)
                    {
                        ["url"] = server.ArtifactUri.AbsoluteUri
                    };
                }

                if (mcpServers.Count == 0)
                {
                    return null;
                }

                var root = new Dictionary<string, object>(StringComparer.Ordinal)
                {
                    ["mcpServers"] = mcpServers
                };

                string json = System.Text.Json.JsonSerializer.Serialize(root, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(targetPath, json);
                return targetPath;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceService.WriteWellKnownMcpConfig failed: {ex}");
                _ = ex.LogAsync();
                return null;
            }
        }

        private const string WellKnownManifestFileName = "_marketplace.json";

        /// <summary>
        /// Persists a snapshot of the discovered well-known marketplace to disk
        /// so that the next tool-window load can render it as Synced without any
        /// network calls.
        /// </summary>
        private static void SaveWellKnownMarketplaceManifest(MarketplaceInfo marketplace)
        {
            if (marketplace == null || string.IsNullOrWhiteSpace(marketplace.LocalPath))
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(marketplace.LocalPath);
                var manifestPath = Path.Combine(marketplace.LocalPath, WellKnownManifestFileName);

                var manifest = new WellKnownMarketplaceManifest
                {
                    Id = marketplace.Id,
                    DisplayName = marketplace.DisplayName,
                    Description = marketplace.Description,
                    Owner = marketplace.Owner,
                    SourceUrl = marketplace.SourceUrl,
                    RepositoryUrl = marketplace.RepositoryUrl,
                    IconPath = marketplace.IconPath,
                    LastUpdated = marketplace.LastUpdated,
                    ErrorMessage = marketplace.ErrorMessage
                };

                foreach (var plugin in marketplace.Plugins)
                {
                    foreach (var asset in plugin.Assets)
                    {
                        manifest.Assets.Add(new WellKnownMarketplaceManifestAsset
                        {
                            Type = asset.Type.ToString(),
                            Name = asset.Name,
                            Description = asset.Description,
                            RelativePath = asset.RelativePath,
                            LocalPath = asset.LocalPath,
                            PluginName = asset.PluginName
                        });
                    }
                }

                var json = System.Text.Json.JsonSerializer.Serialize(manifest, new System.Text.Json.JsonSerializerOptions
                {
                    WriteIndented = true
                });

                File.WriteAllText(manifestPath, json);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceService.SaveWellKnownMarketplaceManifest failed: {ex}");
                _ = ex.LogAsync();
            }
        }

        /// <summary>
        /// Tries to rebuild a well-known MarketplaceInfo from the on-disk
        /// manifest written by a previous discovery run. Returns null if no
        /// usable manifest is found.
        /// </summary>
        private static MarketplaceInfo TryLoadWellKnownMarketplaceFromDisk(MarketplaceEntry entry, Uri originUri)
        {
            try
            {
                var cacheDirectory = MarketplaceStorageService.GetWellKnownDiscoveryDirectory(originUri);
                var manifestPath = Path.Combine(cacheDirectory, WellKnownManifestFileName);
                if (!File.Exists(manifestPath))
                {
                    return null;
                }

                var json = File.ReadAllText(manifestPath);
                var manifest = System.Text.Json.JsonSerializer.Deserialize<WellKnownMarketplaceManifest>(json, new System.Text.Json.JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });

                if (manifest == null)
                {
                    return null;
                }

                var sourceId = WellKnownDiscoveryService.GetSourceId(originUri);
                var displayUrl = WellKnownDiscoveryService.GetDisplayUrl(originUri);

                var plugin = new MarketplacePlugin
                {
                    Name = "Well-Known Marketplace",
                    Description = manifest.Description,
                    Source = displayUrl,
                    MarketplaceId = sourceId
                };

                if (manifest.Assets != null)
                {
                    foreach (var asset in manifest.Assets)
                    {
                        if (!Enum.TryParse(asset.Type, ignoreCase: true, out AssetType assetType))
                        {
                            continue;
                        }

                        plugin.Assets.Add(new PluginAsset
                        {
                            Type = assetType,
                            Name = asset.Name,
                            Description = asset.Description,
                            RelativePath = asset.RelativePath,
                            LocalPath = asset.LocalPath,
                            PluginName = asset.PluginName,
                            MarketplaceId = sourceId
                        });
                    }
                }

                var iconPath = manifest.IconPath;
                if (string.IsNullOrWhiteSpace(iconPath) || !File.Exists(iconPath))
                {
                    var faviconCandidate = MarketplaceStorageService.GetWellKnownDiscoveryIconPath(originUri, ".ico");
                    iconPath = File.Exists(faviconCandidate) ? faviconCandidate : null;
                }

                return new MarketplaceInfo
                {
                    Id = sourceId,
                    Owner = originUri.Host,
                    RepoName = "well-known",
                    SourceKind = MarketplaceSourceKind.WellKnownDiscovery,
                    RepositoryUrl = displayUrl,
                    SourceUrl = displayUrl,
                    DisplayName = string.IsNullOrWhiteSpace(manifest.DisplayName) ? (string.IsNullOrWhiteSpace(entry.DisplayName) ? WellKnownDiscoveryService.GetDisplayName(originUri) : entry.DisplayName) : manifest.DisplayName,
                    Description = manifest.Description,
                    IsBuiltIn = false,
                    LocalPath = cacheDirectory,
                    IconPath = iconPath,
                    IsCloned = true,
                    LastUpdated = manifest.LastUpdated,
                    ErrorMessage = manifest.ErrorMessage,
                    Plugins = new List<MarketplacePlugin> { plugin }
                };
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceService.TryLoadWellKnownMarketplaceFromDisk failed: {ex}");
                _ = ex.LogAsync();
                return null;
            }
        }

        private sealed class WellKnownMarketplaceManifest
        {
            public string Id { get; set; }

            public string DisplayName { get; set; }

            public string Description { get; set; }

            public string Owner { get; set; }

            public string SourceUrl { get; set; }

            public string RepositoryUrl { get; set; }

            public string IconPath { get; set; }

            public DateTime? LastUpdated { get; set; }

            public string ErrorMessage { get; set; }

            public List<WellKnownMarketplaceManifestAsset> Assets { get; set; } = new List<WellKnownMarketplaceManifestAsset>();
        }

        private sealed class WellKnownMarketplaceManifestAsset
        {
            public string Type { get; set; }

            public string Name { get; set; }

            public string Description { get; set; }

            public string RelativePath { get; set; }

            public string LocalPath { get; set; }

            public string PluginName { get; set; }
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
