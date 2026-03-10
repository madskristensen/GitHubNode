using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Parses MarketplaceInfo.json files and scans plugin directories for assets.
    /// </summary>
    internal static class MarketplaceParserService
    {
        /// <summary>
        /// Known locations for MarketplaceInfo.json within a repository.
        /// </summary>
        private static readonly string[] MarketplaceJsonPaths = new[]
        {
            ".github/plugin/MarketplaceInfo.json",
            ".claude-plugin/MarketplaceInfo.json",
            "MarketplaceInfo.json"
        };

        /// <summary>
        /// Raw MarketplaceInfo.json structure for deserialization.
        /// </summary>
        private sealed class RawMarketplaceJson
        {
            public string name { get; set; }
            public RawOwner owner { get; set; }
            public RawMetadata metadata { get; set; }
            public List<RawPlugin> plugins { get; set; }
        }

        private sealed class RawOwner
        {
            public string name { get; set; }
            public string email { get; set; }
        }

        private sealed class RawMetadata
        {
            public string description { get; set; }
            public string version { get; set; }
        }

        private sealed class RawPlugin
        {
            public string name { get; set; }
            public string description { get; set; }
            public string version { get; set; }
            public string source { get; set; }
            public List<string> skills { get; set; }
        }

        /// <summary>
        /// Parses a cloned MarketplaceInfo repository and returns the MarketplaceInfo object.
        /// </summary>
        public static MarketplaceInfo ParseMarketplace(string owner, string repo, string localPath)
        {
            var MarketplaceInfo = new MarketplaceInfo
            {
                Id = MarketplaceStorageService.GetMarketplaceId(owner, repo),
                Owner = owner,
                RepoName = repo,
                LocalPath = localPath,
                IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo),
                IsCloned = Directory.Exists(Path.Combine(localPath, ".git")),
                LastUpdated = MarketplaceGitService.GetLastUpdateTime(owner, repo)
            };

            if (!MarketplaceInfo.IsCloned)
            {
                MarketplaceInfo.ErrorMessage = "Repository not cloned.";
                return MarketplaceInfo;
            }

            // Try to find and parse MarketplaceInfo.json
            RawMarketplaceJson rawJson = null;
            foreach (var relativePath in MarketplaceJsonPaths)
            {
                var fullPath = Path.Combine(localPath, relativePath);
                if (File.Exists(fullPath))
                {
                    rawJson = TryParseMarketplaceJson(fullPath);
                    if (rawJson != null)
                    {
                        break;
                    }
                }
            }

            if (rawJson != null)
            {
                // Populate from MarketplaceInfo.json
                MarketplaceInfo.DisplayName = rawJson.name ?? $"{owner}/{repo}";
                MarketplaceInfo.OwnerInfo = rawJson.owner != null
                    ? new MarketplaceOwner { Name = rawJson.owner.name, Email = rawJson.owner.email }
                    : null;
                MarketplaceInfo.Metadata = rawJson.metadata != null
                    ? new MarketplaceMetadata { Description = rawJson.metadata.description, Version = rawJson.metadata.version }
                    : null;
                MarketplaceInfo.Description = rawJson.metadata?.description;

                // Parse plugins
                if (rawJson.plugins != null)
                {
                    foreach (var rawPlugin in rawJson.plugins)
                    {
                        var plugin = ParsePlugin(rawPlugin, localPath, MarketplaceInfo.Id);
                        if (plugin != null)
                        {
                            MarketplaceInfo.Plugins.Add(plugin);
                        }
                    }
                }
            }
            else
            {
                // No MarketplaceInfo.json - scan entire repo for assets (legacy mode)
                MarketplaceInfo.DisplayName = $"{owner}/{repo}";
                var legacyPlugin = ScanDirectoryForAssets(localPath, "root", MarketplaceInfo.Id);
                if (legacyPlugin != null && legacyPlugin.Assets.Count > 0)
                {
                    legacyPlugin.Name = repo;
                    legacyPlugin.Description = "Assets discovered in repository root";
                    MarketplaceInfo.Plugins.Add(legacyPlugin);
                }
            }

            return MarketplaceInfo;
        }

        /// <summary>
        /// Parses a single plugin from the raw JSON and scans for assets.
        /// </summary>
        private static MarketplacePlugin ParsePlugin(RawPlugin raw, string repoPath, string marketplaceId)
        {
            if (string.IsNullOrWhiteSpace(raw?.name))
            {
                return null;
            }

            var plugin = new MarketplacePlugin
            {
                Name = raw.name,
                Description = raw.description,
                Version = raw.version,
                Source = raw.source,
                Skills = raw.skills ?? new List<string>(),
                MarketplaceId = marketplaceId
            };

            // Determine the plugin directory
            var sourcePath = raw.source?.TrimStart('.', '/', '\\') ?? raw.name;
            var pluginDir = Path.Combine(repoPath, sourcePath);

            if (Directory.Exists(pluginDir))
            {
                // Scan the plugin directory for assets
                ScanPluginDirectory(pluginDir, plugin, repoPath);
            }

            return plugin;
        }

        /// <summary>
        /// Scans a plugin directory for assets.
        /// </summary>
        private static void ScanPluginDirectory(string pluginDir, MarketplacePlugin plugin, string repoPath)
        {
            try
            {
                // Scan for agents (*.agent.md)
                foreach (var file in Directory.GetFiles(pluginDir, "*.agent.md", SearchOption.AllDirectories))
                {
                    plugin.Assets.Add(CreateAsset(file, repoPath, AssetType.Agent, plugin.Name, plugin.MarketplaceId));
                }

                // Scan for prompts (*.prompt.md)
                foreach (var file in Directory.GetFiles(pluginDir, "*.prompt.md", SearchOption.AllDirectories))
                {
                    plugin.Assets.Add(CreateAsset(file, repoPath, AssetType.Prompt, plugin.Name, plugin.MarketplaceId));
                }

                // Scan for instructions (*.instructions.md or instructions.md)
                foreach (var file in Directory.GetFiles(pluginDir, "*.instructions.md", SearchOption.AllDirectories))
                {
                    plugin.Assets.Add(CreateAsset(file, repoPath, AssetType.Instructions, plugin.Name, plugin.MarketplaceId));
                }

                foreach (var file in Directory.GetFiles(pluginDir, "instructions.md", SearchOption.AllDirectories))
                {
                    // Avoid duplicates if it also matches *.instructions.md pattern
                    var relativePath = GetRelativePath(file, repoPath);
                    bool alreadyAdded = false;
                    foreach (var existing in plugin.Assets)
                    {
                        if (string.Equals(existing.RelativePath, relativePath, StringComparison.OrdinalIgnoreCase))
                        {
                            alreadyAdded = true;
                            break;
                        }
                    }

                    if (!alreadyAdded)
                    {
                        plugin.Assets.Add(CreateAsset(file, repoPath, AssetType.Instructions, plugin.Name, plugin.MarketplaceId));
                    }
                }

                // Scan for skills (skill.md or SKILL.md in subdirectories)
                foreach (var dir in Directory.GetDirectories(pluginDir, "*", SearchOption.AllDirectories))
                {
                    var skillMdLower = Path.Combine(dir, "skill.md");
                    var skillMdUpper = Path.Combine(dir, "SKILL.md");

                    if (File.Exists(skillMdLower))
                    {
                        var asset = CreateAsset(skillMdLower, repoPath, AssetType.Skill, plugin.Name, plugin.MarketplaceId);
                        asset.Name = Path.GetFileName(dir); // Use folder name as skill name
                        plugin.Assets.Add(asset);
                    }
                    else if (File.Exists(skillMdUpper))
                    {
                        var asset = CreateAsset(skillMdUpper, repoPath, AssetType.Skill, plugin.Name, plugin.MarketplaceId);
                        asset.Name = Path.GetFileName(dir);
                        plugin.Assets.Add(asset);
                    }
                }

                // Check root of plugin for skill.md
                var rootSkillLower = Path.Combine(pluginDir, "skill.md");
                var rootSkillUpper = Path.Combine(pluginDir, "SKILL.md");
                if (File.Exists(rootSkillLower))
                {
                    var asset = CreateAsset(rootSkillLower, repoPath, AssetType.Skill, plugin.Name, plugin.MarketplaceId);
                    asset.Name = plugin.Name;
                    plugin.Assets.Add(asset);
                }
                else if (File.Exists(rootSkillUpper))
                {
                    var asset = CreateAsset(rootSkillUpper, repoPath, AssetType.Skill, plugin.Name, plugin.MarketplaceId);
                    asset.Name = plugin.Name;
                    plugin.Assets.Add(asset);
                }

                // Scan for MCP servers (mcp.json)
                foreach (var file in Directory.GetFiles(pluginDir, "mcp.json", SearchOption.AllDirectories))
                {
                    plugin.Assets.Add(CreateAsset(file, repoPath, AssetType.McpServer, plugin.Name, plugin.MarketplaceId));
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.ScanPluginDirectory failed for '{pluginDir}': {ex}");
            }
        }

        /// <summary>
        /// Scans a directory for assets without a MarketplaceInfo.json (legacy mode).
        /// </summary>
        private static MarketplacePlugin ScanDirectoryForAssets(string directory, string pluginName, string marketplaceId)
        {
            var plugin = new MarketplacePlugin
            {
                Name = pluginName,
                MarketplaceId = marketplaceId
            };

            // Check for common folder structures
            var agentsDir = Path.Combine(directory, "agents");
            var skillsDir = Path.Combine(directory, "skills");
            var promptsDir = Path.Combine(directory, "prompts");
            var instructionsDir = Path.Combine(directory, "instructions");
            var pluginsDir = Path.Combine(directory, "plugins");

            // Scan agents folder
            if (Directory.Exists(agentsDir))
            {
                ScanFolderForType(agentsDir, directory, plugin, AssetType.Agent, "*.agent.md", "*.md");
            }

            // Scan prompts folder
            if (Directory.Exists(promptsDir))
            {
                ScanFolderForType(promptsDir, directory, plugin, AssetType.Prompt, "*.prompt.md", "*.md");
            }

            // Scan instructions folder
            if (Directory.Exists(instructionsDir))
            {
                ScanFolderForType(instructionsDir, directory, plugin, AssetType.Instructions, "*.instructions.md", "*.md");
            }

            // Scan skills folder
            if (Directory.Exists(skillsDir))
            {
                ScanSkillsFolder(skillsDir, directory, plugin);
            }

            // Scan plugins folder (for dotnet/skills style repos)
            if (Directory.Exists(pluginsDir))
            {
                foreach (var subDir in Directory.GetDirectories(pluginsDir))
                {
                    ScanPluginDirectory(subDir, plugin, directory);
                }
            }

            return plugin;
        }

        private static void ScanFolderForType(
            string folder,
            string repoPath,
            MarketplacePlugin plugin,
            AssetType type,
            params string[] patterns)
        {
            try
            {
                var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                foreach (var pattern in patterns)
                {
                    foreach (var file in Directory.GetFiles(folder, pattern, SearchOption.AllDirectories))
                    {
                        if (addedPaths.Add(file))
                        {
                            plugin.Assets.Add(CreateAsset(file, repoPath, type, plugin.Name, plugin.MarketplaceId));
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.ScanFolderForType failed: {ex}");
            }
        }

        private static void ScanSkillsFolder(string skillsDir, string repoPath, MarketplacePlugin plugin)
        {
            try
            {
                foreach (var dir in Directory.GetDirectories(skillsDir))
                {
                    var skillMd = Path.Combine(dir, "skill.md");
                    var skillMdUpper = Path.Combine(dir, "SKILL.md");

                    string skillFile = null;
                    if (File.Exists(skillMd))
                    {
                        skillFile = skillMd;
                    }
                    else if (File.Exists(skillMdUpper))
                    {
                        skillFile = skillMdUpper;
                    }

                    if (skillFile != null)
                    {
                        var asset = CreateAsset(skillFile, repoPath, AssetType.Skill, plugin.Name, plugin.MarketplaceId);
                        asset.Name = Path.GetFileName(dir);
                        plugin.Assets.Add(asset);
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.ScanSkillsFolder failed: {ex}");
            }
        }

        private static PluginAsset CreateAsset(
            string filePath,
            string repoPath,
            AssetType type,
            string pluginName,
            string marketplaceId)
        {
            var relativePath = GetRelativePath(filePath, repoPath);
            var name = GetAssetName(filePath, type);

            return new PluginAsset
            {
                Type = type,
                Name = name,
                LocalPath = filePath,
                RelativePath = relativePath,
                PluginName = pluginName,
                MarketplaceId = marketplaceId
            };
        }

        private static string GetAssetName(string filePath, AssetType type)
        {
            var fileName = Path.GetFileNameWithoutExtension(filePath);

            // Remove known suffixes
            if (fileName.EndsWith(".agent", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - ".agent".Length);
            }
            else if (fileName.EndsWith(".prompt", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - ".prompt".Length);
            }
            else if (fileName.EndsWith(".instructions", StringComparison.OrdinalIgnoreCase))
            {
                fileName = fileName.Substring(0, fileName.Length - ".instructions".Length);
            }

            // For skill.md files, use parent folder name
            if (type == AssetType.Skill &&
                (string.Equals(fileName, "skill", StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(fileName, "SKILL", StringComparison.OrdinalIgnoreCase)))
            {
                fileName = Path.GetFileName(Path.GetDirectoryName(filePath));
            }

            return fileName;
        }

        private static string GetRelativePath(string fullPath, string basePath)
        {
            if (string.IsNullOrEmpty(basePath))
            {
                return fullPath;
            }

            var baseUri = new Uri(basePath.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var fullUri = new Uri(fullPath);
            var relativeUri = baseUri.MakeRelativeUri(fullUri);

            return Uri.UnescapeDataString(relativeUri.ToString().Replace('/', Path.DirectorySeparatorChar));
        }

        private static RawMarketplaceJson TryParseMarketplaceJson(string path)
        {
            try
            {
                var json = File.ReadAllText(path);
                return JsonSerializer.Deserialize<RawMarketplaceJson>(json, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                });
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.TryParseMarketplaceJson failed for '{path}': {ex}");
                return null;
            }
        }
    }
}
