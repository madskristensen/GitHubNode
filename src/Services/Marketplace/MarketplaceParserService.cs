using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Parses marketplace.json files and scans plugin directories for assets.
    /// </summary>
    internal static class MarketplaceParserService
    {
        /// <summary>
        /// Known locations for marketplace.json within a repository.
        /// </summary>
        private static readonly string[] MarketplaceJsonPaths = new[]
        {
            ".github/plugin/marketplace.json",
            ".github/plugin/MarketplaceInfo.json",
            ".claude-plugin/marketplace.json",
            ".claude-plugin/MarketplaceInfo.json",
            "marketplace.json",
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
            public string pluginRoot { get; set; }
        }

        /// <summary>
        /// Represents an external repository source for a linked plugin.
        /// Used when a plugin references content from a different GitHub repository.
        /// </summary>
        private sealed class RawPluginSource
        {
            /// <summary>
            /// The source type (e.g., "github").
            /// </summary>
            public string source { get; set; }

            /// <summary>
            /// The repository in "owner/repo" format.
            /// </summary>
            public string repo { get; set; }

            /// <summary>
            /// The path within the external repository.
            /// </summary>
            public string path { get; set; }
        }

        private sealed class RawPlugin
        {
            public string name { get; set; }
            public string description { get; set; }
            public string version { get; set; }

            /// <summary>
            /// Source can be either a string (relative path) or a RawPluginSource object (external repo).
            /// Use TryGetLinkedSource() to check for the external repo format.
            /// </summary>
            public JsonElement? source { get; set; }
            public List<string> skills { get; set; }

            /// <summary>
            /// Tries to get the source as a simple string path (local to parent repo).
            /// </summary>
            public string GetLocalSourcePath()
            {
                if (source == null || source.Value.ValueKind == JsonValueKind.Null)
                {
                    return null;
                }

                if (source.Value.ValueKind == JsonValueKind.String)
                {
                    return source.Value.GetString();
                }

                return null;
            }

            /// <summary>
            /// Tries to get the source as an external repository reference.
            /// </summary>
            public RawPluginSource GetLinkedSource()
            {
                if (source == null || source.Value.ValueKind != JsonValueKind.Object)
                {
                    return null;
                }

                try
                {
                    return JsonSerializer.Deserialize<RawPluginSource>(source.Value.GetRawText(), new JsonSerializerOptions
                    {
                        PropertyNameCaseInsensitive = true
                    });
                }
                catch
                {
                    return null;
                }
            }
        }

        /// <summary>
        /// Parses a cloned MarketplaceInfo repository and returns the MarketplaceInfo object.
        /// This is a synchronous version that does not clone linked repositories.
        /// Use ParseMarketplaceAsync for full linked repository support.
        /// </summary>
        public static MarketplaceInfo ParseMarketplace(string owner, string repo, string localPath)
        {
            return ParseMarketplaceAsync(owner, repo, localPath, cloneLinkedRepos: false, CancellationToken.None)
                .GetAwaiter().GetResult();
        }

        /// <summary>
        /// Parses a cloned MarketplaceInfo repository and returns the MarketplaceInfo object.
        /// Supports async cloning of linked repositories.
        /// </summary>
        public static async Task<MarketplaceInfo> ParseMarketplaceAsync(
            string owner,
            string repo,
            string localPath,
            bool cloneLinkedRepos = true,
            CancellationToken cancellationToken = default)
        {
            var marketplaceInfo = new MarketplaceInfo
            {
                Id = MarketplaceStorageService.GetMarketplaceId(owner, repo),
                Owner = owner,
                RepoName = repo,
                LocalPath = localPath,
                IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo),
                IsCloned = Directory.Exists(Path.Combine(localPath, ".git")),
                LastUpdated = MarketplaceGitService.GetLastUpdateTime(owner, repo)
            };

            if (!marketplaceInfo.IsCloned)
            {
                marketplaceInfo.ErrorMessage = "Repository not cloned.";
                return marketplaceInfo;
            }

            // Try to find and parse marketplace.json
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
                // Populate from marketplace.json
                marketplaceInfo.DisplayName = rawJson.name ?? $"{owner}/{repo}";
                marketplaceInfo.OwnerInfo = rawJson.owner != null
                    ? new MarketplaceOwner { Name = rawJson.owner.name, Email = rawJson.owner.email }
                    : null;
                marketplaceInfo.Metadata = rawJson.metadata != null
                    ? new MarketplaceMetadata { Description = rawJson.metadata.description, Version = rawJson.metadata.version }
                    : null;
                marketplaceInfo.Description = rawJson.metadata?.description;

                // Get the plugin root directory (default to repo root if not specified)
                // Only trim leading slashes, not dots (to preserve .github folder names)
                string pluginRoot = rawJson.metadata?.pluginRoot?.TrimStart('/', '\\') ?? string.Empty;

                // Parse plugins
                if (rawJson.plugins != null)
                {
                    foreach (var rawPlugin in rawJson.plugins)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var plugin = await ParsePluginAsync(rawPlugin, localPath, pluginRoot, marketplaceInfo.Id, cloneLinkedRepos, cancellationToken);
                        if (plugin != null)
                        {
                            marketplaceInfo.Plugins.Add(plugin);
                        }
                    }
                }
            }
            else
            {
                // No marketplace.json - scan entire repo for assets (legacy mode)
                marketplaceInfo.DisplayName = $"{owner}/{repo}";
                var legacyPlugin = ScanDirectoryForAssets(localPath, repo, marketplaceInfo.Id);
                if (legacyPlugin != null && legacyPlugin.Assets.Count > 0)
                {
                    legacyPlugin.Description = "Assets discovered in repository root";
                    marketplaceInfo.Plugins.Add(legacyPlugin);
                }
            }

            return marketplaceInfo;
        }

        /// <summary>
        /// Parses a single plugin from the raw JSON and scans for assets.
        /// </summary>
        private static async Task<MarketplacePlugin> ParsePluginAsync(
            RawPlugin raw,
            string repoPath,
            string pluginRoot,
            string marketplaceId,
            bool cloneLinkedRepos,
            CancellationToken cancellationToken)
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
                Source = raw.GetLocalSourcePath(),
                Skills = raw.skills ?? new List<string>(),
                MarketplaceId = marketplaceId
            };

            // Check if this is a linked (external) repository source
            RawPluginSource linkedSource = raw.GetLinkedSource();
            if (linkedSource != null && 
                !string.IsNullOrWhiteSpace(linkedSource.repo) &&
                string.Equals(linkedSource.source, "github", StringComparison.OrdinalIgnoreCase))
            {
                // Parse owner/repo from "owner/repo" format
                string[] repoParts = linkedSource.repo.Split('/');
                if (repoParts.Length == 2)
                {
                    string linkedOwner = repoParts[0];
                    string linkedRepo = repoParts[1];

                    // Extract parent marketplace owner/repo from marketplaceId
                    string[] parentParts = marketplaceId.Split('/');
                    if (parentParts.Length == 2)
                    {
                        string parentOwner = parentParts[0];
                        string parentRepo = parentParts[1];

                        // Check if linked repo is already cloned (fast path)
                        string linkedLocalPath = MarketplaceStorageService.GetLinkedRepositoryDirectory(
                            parentOwner, parentRepo, linkedOwner, linkedRepo);

                        bool needsClone = !Directory.Exists(Path.Combine(linkedLocalPath, ".git"));

                        if (needsClone && cloneLinkedRepos)
                        {
                            // Clone the linked repository asynchronously
                            try
                            {
                                var (result, clonedPath) = await MarketplaceGitService.CloneLinkedRepositoryAsync(
                                    parentOwner, parentRepo, linkedOwner, linkedRepo, cancellationToken: cancellationToken);

                                if (!result.Success)
                                {
                                    Debug.WriteLine($"MarketplaceParserService: Failed to clone linked repo {linkedSource.repo}: {result.Error}");
                                }

                                linkedLocalPath = clonedPath;
                            }
                            catch (OperationCanceledException)
                            {
                                throw;
                            }
                            catch (Exception ex)
                            {
                                Debug.WriteLine($"MarketplaceParserService: Exception cloning linked repo {linkedSource.repo}: {ex.Message}");
                            }
                        }

                        // Scan if the linked repo directory exists
                        if (Directory.Exists(linkedLocalPath))
                        {
                            // Determine the plugin directory within the linked repo
                            // Only trim leading slashes, not dots (to preserve .github folder names)
                            string linkedPluginPath = string.IsNullOrWhiteSpace(linkedSource.path)
                                ? linkedLocalPath
                                : Path.Combine(linkedLocalPath, linkedSource.path.TrimStart('/', '\\'));

                            if (Directory.Exists(linkedPluginPath))
                            {
                                // Scan the linked plugin directory for assets
                                ScanPluginDirectory(linkedPluginPath, plugin, linkedLocalPath);
                                plugin.Source = $"{linkedSource.repo}:{linkedSource.path}";
                            }
                        }
                    }
                }

                return plugin;
            }

            // Standard local source path handling
            // Combine: repoPath / pluginRoot / sourcePath
            // Only trim leading slashes, not dots (to preserve .github folder names)
            var sourcePath = raw.GetLocalSourcePath()?.TrimStart('/', '\\') ?? raw.name;

            string pluginDir;
            if (!string.IsNullOrEmpty(pluginRoot))
            {
                pluginDir = Path.Combine(repoPath, pluginRoot, sourcePath);
            }
            else
            {
                pluginDir = Path.Combine(repoPath, sourcePath);
            }

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
