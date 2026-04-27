using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Manages MarketplaceInfo configuration and storage paths.
    /// </summary>
    internal static class MarketplaceStorageService
    {
        private static readonly object _configLock = new object();

        /// <summary>
        /// Gets the base directory for MarketplaceInfo data.
        /// </summary>
        public static string BaseDirectory { get; } = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitHubNode");

        /// <summary>
        /// Gets the directory where MarketplaceInfo repositories are cloned.
        /// </summary>
        public static string MarketplacesDirectory { get; } = Path.Combine(BaseDirectory, "Marketplaces");

        /// <summary>
        /// Gets the directory where Agent Skills Discovery artifacts are cached.
        /// </summary>
        public static string AgentSkillsDirectory { get; } = Path.Combine(BaseDirectory, "AgentSkills");

        /// <summary>
        /// Gets the path to the user's MarketplaceInfo configuration file.
        /// </summary>
        public static string ConfigFilePath { get; } = Path.Combine(BaseDirectory, "marketplaces.json");

        /// <summary>
        /// Built-in marketplaces that are always available.
        /// </summary>
        public static IReadOnlyList<MarketplaceEntry> BuiltInMarketplaces { get; } = new List<MarketplaceEntry>
        {
            new MarketplaceEntry { Owner = "github", Repo = "awesome-copilot", Branch = "main" },
            new MarketplaceEntry { Owner = "github", Repo = "copilot-plugins", Branch = "main" },
            new MarketplaceEntry { Owner = "dotnet", Repo = "skills", Branch = "main" },
            new MarketplaceEntry { Owner = "anthropics", Repo = "skills", Branch = "main" }
        };

        /// <summary>
        /// Gets the local clone directory for a MarketplaceInfo.
        /// </summary>
        public static string GetMarketplaceDirectory(string owner, string repo, string repositoryUrl = null)
        {
            var host = MarketplaceRepositoryUrl.GetHost(repositoryUrl);
            var safeName = string.IsNullOrWhiteSpace(host)
                ? $"{SanitizePathComponent(owner)}_{SanitizePathComponent(repo)}"
                : $"{SanitizePathComponent(host)}_{SanitizePathComponent(owner)}_{SanitizePathComponent(repo)}";

            return Path.Combine(MarketplacesDirectory, safeName);
        }

        /// <summary>
        /// Gets the local cache directory for an Agent Skills Discovery source.
        /// </summary>
        public static string GetAgentSkillsDiscoveryDirectory(Uri indexUri)
        {
            if (indexUri == null)
            {
                throw new ArgumentNullException(nameof(indexUri));
            }

            var safeName = SanitizePathComponent(indexUri.Host + indexUri.AbsolutePath.Replace('/', '_'));
            return Path.Combine(AgentSkillsDirectory, safeName);
        }

        /// <summary>
        /// Gets the path where the marketplace icon should be stored.
        /// </summary>
        public static string GetIconPath(string owner, string repo, string iconExtension, string repositoryUrl = null)
        {
            var marketplaceDir = GetMarketplaceDirectory(owner, repo, repositoryUrl);
            return Path.Combine(marketplaceDir, $"_icon{iconExtension}");
        }

        /// <summary>
        /// Gets the path where an Agent Skills Discovery favicon should be stored.
        /// </summary>
        public static string GetAgentSkillsDiscoveryIconPath(Uri indexUri, string iconExtension)
        {
            var discoveryDir = GetAgentSkillsDiscoveryDirectory(indexUri);
            return Path.Combine(discoveryDir, $"_favicon{iconExtension}");
        }

        /// <summary>
        /// Gets the local directory for a linked repository within a parent marketplace.
        /// Linked repositories are stored in a _linked subfolder to keep them separate from the parent's content.
        /// </summary>
        /// <param name="parentOwner">Owner of the parent marketplace.</param>
        /// <param name="parentRepo">Repository name of the parent marketplace.</param>
        /// <param name="linkedOwner">Owner of the linked repository.</param>
        /// <param name="linkedRepo">Repository name of the linked repository.</param>
        public static string GetLinkedRepositoryDirectory(string parentOwner, string parentRepo, string linkedOwner, string linkedRepo, string parentRepositoryUrl = null, string linkedRepositoryUrl = null)
        {
            var parentDir = GetMarketplaceDirectory(parentOwner, parentRepo, parentRepositoryUrl);
            var linkedHost = MarketplaceRepositoryUrl.GetHost(linkedRepositoryUrl);
            var linkedSafeName = string.IsNullOrWhiteSpace(linkedHost)
                ? $"{SanitizePathComponent(linkedOwner)}_{SanitizePathComponent(linkedRepo)}"
                : $"{SanitizePathComponent(linkedHost)}_{SanitizePathComponent(linkedOwner)}_{SanitizePathComponent(linkedRepo)}";
            return Path.Combine(parentDir, "_linked", linkedSafeName);
        }

        /// <summary>
        /// Gets the MarketplaceInfo ID from owner and repo.
        /// </summary>
        public static string GetMarketplaceId(string owner, string repo, string repositoryUrl = null)
        {
            var host = MarketplaceRepositoryUrl.GetHost(repositoryUrl);
            return string.IsNullOrWhiteSpace(host)
                ? $"{owner}/{repo}"
                : $"{host}/{owner}/{repo}";
        }

        /// <summary>
        /// Loads the user's MarketplaceInfo configuration.
        /// </summary>
        public static MarketplaceConfig LoadConfig()
        {
            lock (_configLock)
            {
                try
                {
                    if (File.Exists(ConfigFilePath))
                    {
                        var json = File.ReadAllText(ConfigFilePath);
                        var config = JsonSerializer.Deserialize<MarketplaceConfig>(json);
                        return config ?? new MarketplaceConfig();
                    }
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MarketplaceStorageService.LoadConfig failed: {ex}");
                    _ = ex.LogAsync();
                }

                return new MarketplaceConfig();
            }
        }

        /// <summary>
        /// Saves the user's MarketplaceInfo configuration.
        /// </summary>
        public static void SaveConfig(MarketplaceConfig config)
        {
            if (config == null)
            {
                throw new ArgumentNullException(nameof(config));
            }

            lock (_configLock)
            {
                try
                {
                    EnsureDirectoriesExist();

                    var options = new JsonSerializerOptions
                    {
                        WriteIndented = true
                    };

                    var json = JsonSerializer.Serialize(config, options);
                    File.WriteAllText(ConfigFilePath, json);
                }
                catch (Exception ex)
                {
                    Debug.WriteLine($"MarketplaceStorageService.SaveConfig failed: {ex}");
                    _ = ex.LogAsync();
                    throw;
                }
            }
        }

        /// <summary>
        /// Adds a MarketplaceInfo to the user's configuration.
        /// </summary>
        public static bool AddMarketplace(string owner, string repo, string branch = "main", string repositoryUrl = null)
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return false;
            }

            var config = LoadConfig();
            var marketplaceId = GetMarketplaceId(owner, repo, repositoryUrl);

            // Check if already exists
            foreach (var existing in config.Marketplaces)
            {
                if (existing.SourceKind != MarketplaceSourceKind.Repository)
                {
                    continue;
                }

                if (string.Equals(GetMarketplaceId(existing.Owner, existing.Repo, existing.RepositoryUrl), marketplaceId, StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Already exists
                }
            }

            // Check if it's a built-in (shouldn't add as user MarketplaceInfo)
            if (IsBuiltIn(owner, repo, repositoryUrl))
            {
                return false; // Is built-in
            }

            config.Marketplaces.Add(new MarketplaceEntry
            {
                Owner = owner,
                Repo = repo,
                RepositoryUrl = string.IsNullOrWhiteSpace(repositoryUrl) ? null : MarketplaceRepositoryUrl.GetRepositoryUrl(owner, repo, repositoryUrl),
                Branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch
            });

            SaveConfig(config);
            return true;
        }

        /// <summary>
        /// Removes a MarketplaceInfo from the user's configuration.
        /// </summary>
        public static bool RemoveMarketplace(string owner, string repo, string repositoryUrl = null)
        {
            var config = LoadConfig();
            var marketplaceId = GetMarketplaceId(owner, repo, repositoryUrl);

            for (int i = config.Marketplaces.Count - 1; i >= 0; i--)
            {
                var entry = config.Marketplaces[i];
                if (entry.SourceKind != MarketplaceSourceKind.Repository)
                {
                    continue;
                }

                if (string.Equals(GetMarketplaceId(entry.Owner, entry.Repo, entry.RepositoryUrl), marketplaceId, StringComparison.OrdinalIgnoreCase))
                {
                    config.Marketplaces.RemoveAt(i);
                    SaveConfig(config);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Adds an Agent Skills Discovery source to the user's configuration.
        /// </summary>
        public static bool AddAgentSkillsDiscoverySource(Uri indexUri, string displayName, bool trusted)
        {
            if (indexUri == null)
            {
                throw new ArgumentNullException(nameof(indexUri));
            }

            var config = LoadConfig();
            var sourceId = AgentSkillsDiscoveryService.GetSourceId(indexUri);

            foreach (var existing in config.Marketplaces)
            {
                if (existing.SourceKind == MarketplaceSourceKind.AgentSkillsDiscovery &&
                    AgentSkillsDiscoveryService.TryCreateIndexUri(existing.AgentSkillsIndexUrl ?? existing.RepositoryUrl, out var existingIndexUri) &&
                    string.Equals(AgentSkillsDiscoveryService.GetSourceId(existingIndexUri), sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    return false;
                }
            }

            config.Marketplaces.Add(new MarketplaceEntry
            {
                SourceKind = MarketplaceSourceKind.AgentSkillsDiscovery,
                Owner = indexUri.Host,
                Repo = "agent-skills",
                RepositoryUrl = indexUri.AbsoluteUri,
                AgentSkillsIndexUrl = indexUri.AbsoluteUri,
                DisplayName = string.IsNullOrWhiteSpace(displayName) ? AgentSkillsDiscoveryService.GetDisplayName(indexUri) : displayName,
                IsTrusted = trusted
            });

            SaveConfig(config);
            return true;
        }

        /// <summary>
        /// Removes an Agent Skills Discovery source from the user's configuration.
        /// </summary>
        public static bool RemoveAgentSkillsDiscoverySource(string indexUrl)
        {
            if (!AgentSkillsDiscoveryService.TryCreateIndexUri(indexUrl, out var indexUri))
            {
                return false;
            }

            var config = LoadConfig();
            var sourceId = AgentSkillsDiscoveryService.GetSourceId(indexUri);

            for (int i = config.Marketplaces.Count - 1; i >= 0; i--)
            {
                var entry = config.Marketplaces[i];
                if (entry.SourceKind == MarketplaceSourceKind.AgentSkillsDiscovery &&
                    AgentSkillsDiscoveryService.TryCreateIndexUri(entry.AgentSkillsIndexUrl ?? entry.RepositoryUrl, out var existingIndexUri) &&
                    string.Equals(AgentSkillsDiscoveryService.GetSourceId(existingIndexUri), sourceId, StringComparison.OrdinalIgnoreCase))
                {
                    config.Marketplaces.RemoveAt(i);
                    SaveConfig(config);
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Deletes the cloned repository for a MarketplaceInfo.
        /// </summary>
        public static bool DeleteMarketplaceClone(string owner, string repo, string repositoryUrl = null)
        {
            var directory = GetMarketplaceDirectory(owner, repo, repositoryUrl);

            if (!Directory.Exists(directory))
            {
                return true;
            }

            try
            {
                // Git directories have read-only files that need special handling
                SetAttributesNormal(directory);
                Directory.Delete(directory, recursive: true);
                return true;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceStorageService.DeleteMarketplaceClone failed: {ex}");
                _ = ex.LogAsync();
                return false;
            }
        }

        /// <summary>
        /// Gets all MarketplaceInfo entries (built-in + user-added).
        /// </summary>
        public static List<MarketplaceEntry> GetAllMarketplaceEntries()
        {
            var config = LoadConfig();
            var entries = new List<MarketplaceEntry>();

            if (config.EnableBuiltInMarketplaces)
            {
                entries.AddRange(BuiltInMarketplaces);
            }

            entries.AddRange(config.Marketplaces);
            return entries;
        }

        /// <summary>
        /// Checks if a MarketplaceInfo is a built-in MarketplaceInfo.
        /// </summary>
        public static bool IsBuiltIn(string owner, string repo, string repositoryUrl = null)
        {
            if (!string.IsNullOrWhiteSpace(MarketplaceRepositoryUrl.GetHost(repositoryUrl)))
            {
                return false;
            }

            foreach (var builtIn in BuiltInMarketplaces)
            {
                if (string.Equals(builtIn.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(builtIn.Repo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        /// <summary>
        /// Ensures required directories exist.
        /// </summary>
        public static void EnsureDirectoriesExist()
        {
            Directory.CreateDirectory(BaseDirectory);
            Directory.CreateDirectory(MarketplacesDirectory);
            Directory.CreateDirectory(AgentSkillsDirectory);
        }

        private static string SanitizePathComponent(string name)
        {
            if (string.IsNullOrWhiteSpace(name))
            {
                return "unknown";
            }

            // Replace invalid characters with underscore
            var invalid = Path.GetInvalidFileNameChars();
            var result = name;

            foreach (var c in invalid)
            {
                result = result.Replace(c, '_');
            }

            return result;
        }

        private static void SetAttributesNormal(string path)
        {
            try
            {
                foreach (var file in Directory.GetFiles(path))
                {
                    File.SetAttributes(file, FileAttributes.Normal);
                }

                foreach (var dir in Directory.GetDirectories(path))
                {
                    SetAttributesNormal(dir);
                }
            }
            catch
            {
                // Best effort
            }
        }
    }
}
