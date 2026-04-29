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
            public string icon { get; set; }
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
            return ParseMarketplaceAsync(owner, repo, localPath, repositoryUrl: null, cloneLinkedRepos: false, cancellationToken: CancellationToken.None)
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
            string repositoryUrl = null,
            bool cloneLinkedRepos = true,
            CancellationToken cancellationToken = default)
        {
            var marketplaceInfo = new MarketplaceInfo
            {
                Id = MarketplaceStorageService.GetMarketplaceId(owner, repo, repositoryUrl),
                Owner = owner,
                RepoName = repo,
                RepositoryUrl = string.IsNullOrWhiteSpace(repositoryUrl) ? null : MarketplaceRepositoryUrl.GetRepositoryUrl(owner, repo, repositoryUrl),
                LocalPath = localPath,
                IsBuiltIn = MarketplaceStorageService.IsBuiltIn(owner, repo, repositoryUrl),
                IsCloned = Directory.Exists(Path.Combine(localPath, ".git")),
                LastUpdated = MarketplaceGitService.GetLastUpdateTime(owner, repo, repositoryUrl)
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

                // Copy icon to local storage if specified
                marketplaceInfo.IconPath = await CopyIconToStorageAsync(owner, repo, localPath, rawJson.metadata?.icon, repositoryUrl, cancellationToken);

                // Get the plugin root directory (default to repo root if not specified)
                // Only trim leading slashes, not dots (to preserve .github folder names)
                string pluginRoot = rawJson.metadata?.pluginRoot?.TrimStart('/', '\\') ?? string.Empty;

                // Parse plugins
                if (rawJson.plugins != null)
                {
                    foreach (var rawPlugin in rawJson.plugins)
                    {
                        cancellationToken.ThrowIfCancellationRequested();

                        var plugin = await ParsePluginAsync(rawPlugin, localPath, pluginRoot, marketplaceInfo.Id, owner, repo, repositoryUrl, cloneLinkedRepos, cancellationToken);
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

            // Try to discover MCP servers from well-known marketplace URLs
            if (!string.IsNullOrWhiteSpace(repositoryUrl) && Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var marketplaceUri))
            {
                await DiscoverAndAddMcpServersAsync(marketplaceUri, marketplaceInfo, cancellationToken);
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
            string parentOwner,
            string parentRepo,
            string parentRepositoryUrl,
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
            if (linkedSource != null && !string.IsNullOrWhiteSpace(linkedSource.repo))
            {
                if (MarketplaceRepositoryUrl.TryParseInput(linkedSource.repo, out var linkedOwner, out var linkedRepo, out _, out var linkedRepositoryUrl))
                {
                    if (linkedRepositoryUrl == null &&
                        !string.IsNullOrWhiteSpace(linkedSource.source) &&
                        !string.Equals(linkedSource.source, "github", StringComparison.OrdinalIgnoreCase))
                    {
                        return plugin;
                    }

                    string linkedLocalPath = MarketplaceStorageService.GetLinkedRepositoryDirectory(
                        parentOwner, parentRepo, linkedOwner, linkedRepo, parentRepositoryUrl, linkedRepositoryUrl);

                    bool needsClone = !Directory.Exists(Path.Combine(linkedLocalPath, ".git"));

                    if (needsClone && cloneLinkedRepos)
                    {
                        // Clone the linked repository asynchronously
                        try
                        {
                            var (result, clonedPath) = await MarketplaceGitService.CloneLinkedRepositoryAsync(
                                parentOwner, parentRepo, linkedOwner, linkedRepo, linkedRepositoryUrl: linkedRepositoryUrl, parentRepositoryUrl: parentRepositoryUrl, cancellationToken: cancellationToken);

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
                            plugin.Source = string.IsNullOrWhiteSpace(linkedSource.path)
                                ? linkedSource.repo
                                : $"{linkedSource.repo}:{linkedSource.path}";
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

                // Scan for MCP servers (.mcp.json and mcp.json)
                foreach (var file in Directory.GetFiles(pluginDir, ".mcp.json", SearchOption.AllDirectories))
                {
                    plugin.Assets.Add(CreateAsset(file, repoPath, AssetType.McpServer, plugin.Name, plugin.MarketplaceId));
                }
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

            // Scan for MCP servers (.mcp.json and mcp.json) at root and in subdirectories
            ScanForMcpServers(directory, plugin);

            return plugin;
        }

        private static void ScanForMcpServers(string directory, MarketplacePlugin plugin)
        {
            try
            {
                var addedPaths = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

                // Scan for .mcp.json files
                foreach (var file in Directory.GetFiles(directory, ".mcp.json", SearchOption.AllDirectories))
                {
                    if (addedPaths.Add(file))
                    {
                        plugin.Assets.Add(CreateAsset(file, directory, AssetType.McpServer, plugin.Name, plugin.MarketplaceId));
                    }
                }

                // Scan for mcp.json files (without dot prefix)
                foreach (var file in Directory.GetFiles(directory, "mcp.json", SearchOption.AllDirectories))
                {
                    if (addedPaths.Add(file))
                    {
                        plugin.Assets.Add(CreateAsset(file, directory, AssetType.McpServer, plugin.Name, plugin.MarketplaceId));
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.ScanForMcpServers failed: {ex}");
            }
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
                Description = GetAssetDescription(filePath, type),
                LocalPath = filePath,
                RelativePath = relativePath,
                PluginName = pluginName,
                MarketplaceId = marketplaceId
            };
        }

        private static string GetAssetDescription(string filePath, AssetType type)
        {
            if (type == AssetType.McpServer || !filePath.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
            {
                return null;
            }

            try
            {
                var content = File.ReadAllText(filePath);
                if (string.IsNullOrWhiteSpace(content))
                {
                    return null;
                }

                return ExtractDescriptionFromMarkdown(content);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.GetAssetDescription failed for '{filePath}': {ex}");
                return null;
            }
        }

        private static string ExtractDescriptionFromMarkdown(string content)
        {
            var lines = content.Replace("\r\n", "\n").Split('\n');
            var bodyStartIndex = 0;

            if (lines.Length > 0 && string.Equals(lines[0].Trim(), "---", StringComparison.Ordinal))
            {
                for (var i = 1; i < lines.Length; i++)
                {
                    var line = lines[i];
                    if (string.Equals(line.Trim(), "---", StringComparison.Ordinal))
                    {
                        bodyStartIndex = i + 1;
                        break;
                    }

                    var trimmedLine = line.Trim();
                    if (trimmedLine.StartsWith("description:", StringComparison.OrdinalIgnoreCase))
                    {
                        var description = trimmedLine.Substring("description:".Length).Trim().Trim('"', '\'');
                        if (!string.IsNullOrWhiteSpace(description))
                        {
                            return description;
                        }
                    }
                }
            }

            var paragraphLines = new List<string>();
            for (var i = bodyStartIndex; i < lines.Length; i++)
            {
                var trimmed = lines[i].Trim();

                if (string.IsNullOrWhiteSpace(trimmed))
                {
                    if (paragraphLines.Count > 0)
                    {
                        break;
                    }

                    continue;
                }

                if (trimmed.StartsWith("#", StringComparison.Ordinal))
                {
                    continue;
                }

                paragraphLines.Add(trimmed);
            }

            return paragraphLines.Count == 0
                ? null
                : string.Join(" ", paragraphLines);
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

        /// <summary>
        /// Copies the marketplace icon to local storage if specified and valid.
        /// </summary>
        private static async Task<string> CopyIconToStorageAsync(string owner, string repo, string localPath, string iconRelativePath, string repositoryUrl, CancellationToken cancellationToken)
        {
            try
            {
                // Try to use the specified icon from metadata first
                if (!string.IsNullOrWhiteSpace(iconRelativePath))
                {
                    // Normalize path separators
                    var normalizedPath = iconRelativePath.TrimStart('/', '\\').Replace('/', Path.DirectorySeparatorChar);
                    var sourceIconPath = Path.Combine(localPath, normalizedPath);

                    if (File.Exists(sourceIconPath))
                    {
                        var extension = Path.GetExtension(sourceIconPath);
                        var destIconPath = MarketplaceStorageService.GetIconPath(owner, repo, extension, repositoryUrl);

                        // Copy if source is newer or dest doesn't exist
                        var sourceInfo = new FileInfo(sourceIconPath);
                        var destInfo = new FileInfo(destIconPath);

                        if (!destInfo.Exists || sourceInfo.LastWriteTimeUtc > destInfo.LastWriteTimeUtc)
                        {
                            File.Copy(sourceIconPath, destIconPath, overwrite: true);
                            Debug.WriteLine($"MarketplaceParserService.CopyIconToStorageAsync: Copied icon to '{destIconPath}'");
                        }

                        return destIconPath;
                    }

                    Debug.WriteLine($"MarketplaceParserService.CopyIconToStorageAsync: Icon not found at '{sourceIconPath}'");
                }

                // Fall back to GitHub avatar if no icon is specified or found
                return await DownloadGitHubAvatarAsync(owner, repo, repositoryUrl, cancellationToken);
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.CopyIconToStorageAsync failed: {ex}");
                _ = ex.LogAsync();
                return null;
            }
        }

        internal static async Task<string> DownloadGitHubAvatarAsync(string owner, string repo, string repositoryUrl, CancellationToken cancellationToken)
        {
            try
            {
                // Check if this is a GitHub repo
                if (!IsGitHubRepository(repositoryUrl))
                {
                    return null;
                }

                var destIconPath = MarketplaceStorageService.GetIconPath(owner, repo, ".png", repositoryUrl);
                var destDir = Path.GetDirectoryName(destIconPath);

                // Try multiple avatar URLs in order of preference
                var avatarUrls = new[]
                {
                    // Try API endpoint first - more reliable for orgs
                    $"https://api.github.com/users/{owner}",
                    // Fallback to direct avatar URL
                    $"https://github.com/{owner}.png?size=64",
                    // Try avatar CDN
                    $"https://avatars.githubusercontent.com/{owner}?v=4",
                };

                using (var client = new System.Net.Http.HttpClient())
                {
                    client.Timeout = TimeSpan.FromSeconds(10);

                    // Set user agent to avoid API rate limiting issues
                    client.DefaultRequestHeaders.Add("User-Agent", "GitHubNode/1.0");

                    foreach (var url in avatarUrls)
                    {
                        try
                        {
                            byte[] bytes;

                            if (url.Contains("api.github.com"))
                            {
                                // For API endpoint, get JSON and extract avatar_url
                                var json = await client.GetStringAsync(url);

                                // Simple JSON parsing for avatar_url
                                var match = System.Text.RegularExpressions.Regex.Match(json, @"""avatar_url""\s*:\s*""([^""]+)""");
                                if (match.Success)
                                {
                                    var avatarUrl = match.Groups[1].Value;
                                    bytes = await client.GetByteArrayAsync(avatarUrl);

                                    if (bytes != null && bytes.Length > 0)
                                    {
                                        Directory.CreateDirectory(destDir);
                                        await Task.Run(() => File.WriteAllBytes(destIconPath, bytes), cancellationToken);
                                        Debug.WriteLine($"MarketplaceParserService.DownloadGitHubAvatarAsync: Downloaded avatar for {owner} from API to '{destIconPath}'");
                                        return destIconPath;
                                    }
                                }
                            }
                            else
                            {
                                // Direct image download
                                bytes = await client.GetByteArrayAsync(url);

                                if (bytes != null && bytes.Length > 0)
                                {
                                    Directory.CreateDirectory(destDir);
                                    await Task.Run(() => File.WriteAllBytes(destIconPath, bytes), cancellationToken);
                                    Debug.WriteLine($"MarketplaceParserService.DownloadGitHubAvatarAsync: Downloaded avatar for {owner} from {url} to '{destIconPath}'");
                                    return destIconPath;
                                }
                            }
                        }
                        catch (Exception ex)
                        {
                            Debug.WriteLine($"MarketplaceParserService.DownloadGitHubAvatarAsync: Failed to download from {url}: {ex.Message}");
                            // Continue to next URL
                            continue;
                        }
                    }
                }
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.DownloadGitHubAvatarAsync failed: {ex}");
                _ = ex.LogAsync();
            }

            return null;
        }

        internal static bool IsGitHubRepository(string repositoryUrl)
        {
            // If no URL is specified, it defaults to github.com
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return true;
            }

            if (Uri.TryCreate(repositoryUrl, UriKind.Absolute, out var uri))
            {
                return string.Equals(uri.Host, "github.com", StringComparison.OrdinalIgnoreCase) ||
                       uri.Host.EndsWith(".ghe.com", StringComparison.OrdinalIgnoreCase);
            }

            return false;
        }

        /// <summary>
        /// Discovers MCP servers from the marketplace's well-known locations and adds them as assets.
        /// </summary>
        private static async Task DiscoverAndAddMcpServersAsync(
            Uri marketplaceUri,
            MarketplaceInfo marketplaceInfo,
            CancellationToken cancellationToken)
        {
            try
            {
                if (!McpServerDiscoveryService.TryCreateDiscoveryUri(marketplaceUri.AbsoluteUri, out var discoveryUri))
                {
                    return;
                }

                var discoveryResult = await McpServerDiscoveryService.DiscoverAsync(
                    discoveryUri,
                    McpServerDiscoveryService.GetDisplayName(discoveryUri),
                    forceRefresh: false,
                    cancellationToken);

                if (discoveryResult?.Servers == null || discoveryResult.Servers.Count == 0)
                {
                    return;
                }

                // Create a plugin to hold the discovered MCP servers
                var plugin = new MarketplacePlugin
                {
                    Name = "MCP Servers (Well-Known)",
                    Description = discoveryResult.DisplayName,
                    MarketplaceId = marketplaceInfo.Id
                };

                // Convert each discovered server to an asset
                foreach (var server in discoveryResult.Servers)
                {
                    try
                    {
                        var asset = new PluginAsset
                        {
                            Type = AssetType.McpServer,
                            Name = server.Name,
                            RelativePath = server.ArtifactUri?.AbsolutePath ?? server.Name,
                            LocalPath = server.ArtifactUri?.AbsoluteUri ?? server.Name,
                            PluginName = plugin.Name,
                            MarketplaceId = marketplaceInfo.Id,
                            Description = server.Description
                        };

                        plugin.Assets.Add(asset);
                    }
                    catch (Exception ex)
                    {
                        Debug.WriteLine($"MarketplaceParserService.DiscoverAndAddMcpServersAsync: Failed to add MCP server {server.Name}: {ex}");
                    }
                }

                if (plugin.Assets.Count > 0)
                {
                    marketplaceInfo.Plugins.Add(plugin);
                }

                // Log any warnings
                if (discoveryResult.Warnings.Count > 0)
                {
                    Debug.WriteLine($"MarketplaceParserService.DiscoverAndAddMcpServersAsync warnings from {discoveryUri.Host}:");
                    foreach (var warning in discoveryResult.Warnings)
                    {
                        Debug.WriteLine($"  - {warning}");
                    }
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"MarketplaceParserService.DiscoverAndAddMcpServersAsync failed: {ex}");
            }
        }
    }
}
