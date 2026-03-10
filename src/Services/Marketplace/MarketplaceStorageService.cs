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
        public static string GetMarketplaceDirectory(string owner, string repo)
        {
            // Sanitize names for filesystem
            var safeName = $"{SanitizePathComponent(owner)}_{SanitizePathComponent(repo)}";
            return Path.Combine(MarketplacesDirectory, safeName);
        }

        /// <summary>
        /// Gets the MarketplaceInfo ID from owner and repo.
        /// </summary>
        public static string GetMarketplaceId(string owner, string repo)
        {
            return $"{owner}/{repo}";
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
        public static bool AddMarketplace(string owner, string repo, string branch = "main")
        {
            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return false;
            }

            var config = LoadConfig();

            // Check if already exists
            foreach (var existing in config.Marketplaces)
            {
                if (string.Equals(existing.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(existing.Repo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Already exists
                }
            }

            // Check if it's a built-in (shouldn't add as user MarketplaceInfo)
            foreach (var builtIn in BuiltInMarketplaces)
            {
                if (string.Equals(builtIn.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(builtIn.Repo, repo, StringComparison.OrdinalIgnoreCase))
                {
                    return false; // Is built-in
                }
            }

            config.Marketplaces.Add(new MarketplaceEntry
            {
                Owner = owner,
                Repo = repo,
                Branch = string.IsNullOrWhiteSpace(branch) ? "main" : branch
            });

            SaveConfig(config);
            return true;
        }

        /// <summary>
        /// Removes a MarketplaceInfo from the user's configuration.
        /// </summary>
        public static bool RemoveMarketplace(string owner, string repo)
        {
            var config = LoadConfig();

            for (int i = config.Marketplaces.Count - 1; i >= 0; i--)
            {
                var entry = config.Marketplaces[i];
                if (string.Equals(entry.Owner, owner, StringComparison.OrdinalIgnoreCase) &&
                    string.Equals(entry.Repo, repo, StringComparison.OrdinalIgnoreCase))
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
        public static bool DeleteMarketplaceClone(string owner, string repo)
        {
            var directory = GetMarketplaceDirectory(owner, repo);

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
        public static bool IsBuiltIn(string owner, string repo)
        {
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
