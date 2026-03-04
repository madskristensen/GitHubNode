using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;

namespace GitHubNode.Services
{
    /// <summary>
    /// Service for fetching templates from GitHub repositories.
    /// Caches results to disk with weekly expiration.
    /// </summary>
    internal static class AwesomeCopilotService
    {
        private const string _gitHubApiBase = "https://api.github.com";
        private const string _gitHubRawBase = "https://raw.githubusercontent.com";
        private const int _cacheExpirationDays = 7;

        private static readonly HttpClient _httpClient = CreateHttpClient();
        private static readonly List<TemplateProvider> _providers = TemplateProviderRegistry.CreateProviders();
        private static readonly string _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitHubNode",
            "TemplateCache");

        private static HttpClient CreateHttpClient()
        {
            // Ensure TLS 1.2 is enabled for GitHub API requests in .NET Framework host processes.
            System.Net.ServicePointManager.SecurityProtocol |= System.Net.SecurityProtocolType.Tls12;

            var client = new HttpClient
            {
                Timeout = TimeSpan.FromSeconds(30)
            };
            client.DefaultRequestHeaders.Add("User-Agent", "GitHubNode-VSExtension");
            client.DefaultRequestHeaders.Add("Accept", "application/vnd.github.v3+json");
            return client;
        }

        /// <summary>
        /// Gets providers that support a specific template type.
        /// </summary>
        public static IReadOnlyList<TemplateProvider> GetProvidersForTemplateType(TemplateType templateType)
        {
            var providers = new List<TemplateProvider>();

            foreach (TemplateProvider provider in _providers)
            {
                if (provider.GetRule(templateType) != null)
                {
                    providers.Add(provider);
                }
            }

            return providers;
        }

        /// <summary>
        /// Gets templates for the specified type from the default provider.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(TemplateType templateType)
        {
            TemplateProvider provider = GetDefaultProvider(templateType);
            if (provider == null)
            {
                return new List<TemplateInfo>();
            }

            return await GetTemplatesAsync(templateType, provider);
        }

        /// <summary>
        /// Gets templates for the specified type from the selected provider.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(TemplateType templateType, TemplateProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            TemplateSearchRule rule = provider.GetRule(templateType);
            if (rule == null)
            {
                return new List<TemplateInfo>();
            }

            var cacheFile = GetCacheFilePath(templateType, provider.Id);

            // Check cache first
            List<TemplateInfo> cached = LoadFromCache(cacheFile, expiredOk: false);
            if (cached != null)
            {
                return cached;
            }

            // Fetch from GitHub API
            List<TemplateInfo> templates = await FetchTemplatesFromGitHubAsync(provider, rule, templateType);

            if (templates.Count > 0)
            {
                // Save to cache only if we got results
                SaveToCache(cacheFile, templates);
            }
            else
            {
                // API failed or returned empty - try expired cache as fallback
                List<TemplateInfo> expiredCache = LoadFromCache(cacheFile, expiredOk: true);
                if (expiredCache != null && expiredCache.Count > 0)
                {
                    return expiredCache;
                }
            }

            return templates;
        }

        /// <summary>
        /// Gets the content of a template file from GitHub.
        /// </summary>
        public static async Task<string> GetTemplateContentAsync(TemplateInfo template)
        {
            if (string.IsNullOrEmpty(template?.DownloadUrl))
            {
                return null;
            }

            try
            {
                return await _httpClient.GetStringAsync(template.DownloadUrl);
            }
            catch (HttpRequestException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.GetTemplateContentAsync failed for '{template?.DownloadUrl}': {ex}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.GetTemplateContentAsync timed out for '{template?.DownloadUrl}': {ex}");
                return null;
            }
        }

        /// <summary>
        /// Clears the cache for the specified template type and default provider.
        /// </summary>
        public static void ClearCache(TemplateType templateType)
        {
            TemplateProvider provider = GetDefaultProvider(templateType);
            if (provider == null)
            {
                return;
            }

            ClearCache(templateType, provider);
        }

        /// <summary>
        /// Clears the cache for the specified template type and provider.
        /// </summary>
        public static void ClearCache(TemplateType templateType, TemplateProvider provider)
        {
            try
            {
                if (provider == null)
                {
                    throw new ArgumentNullException(nameof(provider));
                }

                var cacheFile = GetCacheFilePath(templateType, provider.Id);
                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                }
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.ClearCache failed for '{templateType}': {ex}");
            }
        }

        private static TemplateProvider GetDefaultProvider(TemplateType templateType)
        {
            foreach (TemplateProvider provider in _providers)
            {
                if (provider.GetRule(templateType) != null)
                {
                    return provider;
                }
            }

            return null;
        }

        private static string GetCacheFilePath(TemplateType templateType, string providerId)
        {
            return Path.Combine(_cacheDirectory, $"{providerId}_{templateType}.cache");
        }

        private static List<TemplateInfo> LoadFromCache(string cacheFile, bool expiredOk)
        {
            try
            {
                if (!File.Exists(cacheFile))
                {
                    return null;
                }

                var fileInfo = new FileInfo(cacheFile);
                if (!expiredOk && fileInfo.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-_cacheExpirationDays))
                {
                    // Cache expired and caller doesn't want expired data
                    return null;
                }

                var lines = File.ReadAllLines(cacheFile);

                // If cache is empty (0 templates), treat as invalid
                if (lines.Length == 0)
                {
                    return null;
                }

                var templates = new List<TemplateInfo>();

                foreach (var line in lines)
                {
                    var parts = line.Split('\t');
                    if (parts.Length >= 4)
                    {
                        templates.Add(new TemplateInfo
                        {
                            Name = parts[0],
                            FileName = parts[1],
                            DownloadUrl = parts[2],
                            TemplateType = (TemplateType)int.Parse(parts[3]),
                            ProviderId = parts.Length >= 5 ? parts[4] : AwesomeCopilotTemplateProvider.ProviderId,
                            DisplayName = parts[0]
                        });
                    }
                }

                return templates;
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.LoadFromCache failed for '{cacheFile}': {ex}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.LoadFromCache failed for '{cacheFile}': {ex}");
                return null;
            }
        }

        private static void SaveToCache(string cacheFile, List<TemplateInfo> templates)
        {
            if (templates == null || templates.Count == 0)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));

                var lines = new List<string>();
                foreach (TemplateInfo t in templates)
                {
                    lines.Add($"{t.Name}\t{t.FileName}\t{t.DownloadUrl}\t{(int)t.TemplateType}\t{t.ProviderId}");
                }

                File.WriteAllLines(cacheFile, lines);
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.SaveToCache failed for '{cacheFile}': {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.SaveToCache failed for '{cacheFile}': {ex}");
            }
        }

        private static async Task<List<TemplateInfo>> FetchTemplatesFromGitHubAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType)
        {
            if (rule.Recursive)
            {
                return await FetchTemplatesFromTreeAsync(provider, rule, templateType);
            }

            return await FetchTemplatesFromDirectoryAsync(provider, rule, templateType);
        }

        private static async Task<List<TemplateInfo>> FetchTemplatesFromDirectoryAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType)
        {
            var templates = new List<TemplateInfo>();

            try
            {
                // Get directory contents from GitHub API
                var url = $"{_gitHubApiBase}/repos/{provider.RepoOwner}/{provider.RepoName}/contents/{rule.RootPath}?ref={provider.Branch}";
                var response = await _httpClient.GetStringAsync(url);
                List<GitHubContentItem> items = ParseGitHubContentsJson(response);

                foreach (GitHubContentItem item in items)
                {
                    if (item.Type == "file" && IsRuleMatch(item.Name, rule))
                    {
                        templates.Add(new TemplateInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(item.Name),
                            FileName = item.Name,
                            DisplayName = item.Name,
                            DownloadUrl = $"{_gitHubRawBase}/{provider.RepoOwner}/{provider.RepoName}/{provider.Branch}/{rule.RootPath}/{item.Name}",
                            TemplateType = templateType,
                            ProviderId = provider.Id
                        });
                    }
                    else if (item.Type == "dir" && rule.UseFolderNameAsTemplateName)
                    {
                        // Skills are folders - look for skill.md inside
                        var skillUrl = $"{_gitHubApiBase}/repos/{provider.RepoOwner}/{provider.RepoName}/contents/{rule.RootPath}/{item.Name}?ref={provider.Branch}";
                        try
                        {
                            var skillResponse = await _httpClient.GetStringAsync(skillUrl);
                            List<GitHubContentItem> skillItems = ParseGitHubContentsJson(skillResponse);
                            GitHubContentItem skillFile = skillItems.Find(f =>
                                IsRuleMatch(f.Name, rule) ||
                                f.Name.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase));

                            if (skillFile != null)
                            {
                                templates.Add(new TemplateInfo
                                {
                                    Name = item.Name,
                                    FileName = item.Name,
                                    DisplayName = item.Name,
                                    DownloadUrl = $"{_gitHubRawBase}/{provider.RepoOwner}/{provider.RepoName}/{provider.Branch}/{rule.RootPath}/{item.Name}/{skillFile.Name}",
                                    TemplateType = templateType,
                                    ProviderId = provider.Id
                                });
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            _ = ex.LogAsync();
                            Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromGitHubAsync failed to fetch skill folder '{item.Name}': {ex}");
                        }
                        catch (TaskCanceledException ex)
                        {
                            _ = ex.LogAsync();
                            Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromGitHubAsync timed out for skill folder '{item.Name}': {ex}");
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromDirectoryAsync failed for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
            }
            catch (TaskCanceledException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromDirectoryAsync timed out for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
            }

            return templates;
        }

        private static async Task<List<TemplateInfo>> FetchTemplatesFromTreeAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType)
        {
            var templates = new List<TemplateInfo>();

            try
            {
                var url = $"{_gitHubApiBase}/repos/{provider.RepoOwner}/{provider.RepoName}/git/trees/{provider.Branch}?recursive=1";
                var response = await _httpClient.GetStringAsync(url);
                List<GitHubTreeItem> treeItems = ParseGitHubTreeJson(response);

                var rootPrefix = NormalizePath(rule.RootPath) + "/";

                foreach (GitHubTreeItem item in treeItems)
                {
                    if (!item.Type.Equals("blob", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var normalizedPath = NormalizePath(item.Path);
                    if (!normalizedPath.StartsWith(rootPrefix, StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    var fileName = Path.GetFileName(normalizedPath);
                    if (!IsRuleMatch(fileName, rule))
                    {
                        continue;
                    }

                    var suggestedFileName = templateType == TemplateType.Skill
                        ? Path.GetFileName(Path.GetDirectoryName(normalizedPath))
                        : fileName;

                    var displayName = normalizedPath.Substring(rootPrefix.Length);

                    templates.Add(new TemplateInfo
                    {
                        Name = Path.GetFileNameWithoutExtension(suggestedFileName),
                        FileName = suggestedFileName,
                        DisplayName = displayName,
                        DownloadUrl = $"{_gitHubRawBase}/{provider.RepoOwner}/{provider.RepoName}/{provider.Branch}/{normalizedPath}",
                        TemplateType = templateType,
                        ProviderId = provider.Id
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromTreeAsync failed for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
            }
            catch (TaskCanceledException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromTreeAsync timed out for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
            }

            return templates;
        }

        private static bool IsRuleMatch(string fileName, TemplateSearchRule rule)
        {
            if (!string.IsNullOrWhiteSpace(rule.FileName) &&
                fileName.Equals(rule.FileName, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            if (!string.IsNullOrWhiteSpace(rule.FileSuffix) &&
                fileName.EndsWith(rule.FileSuffix, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }

            return false;
        }

        private static string NormalizePath(string path)
            => path?.Replace('\\', '/').Trim('/') ?? string.Empty;

        /// <summary>
        /// Parses the GitHub contents API JSON response.
        /// Uses JSON deserialization to extract name and type values.
        /// </summary>
        private static List<GitHubContentItem> ParseGitHubContentsJson(string json)
        {
            return ParseGitHubContentsByTokenScan(json);
        }

        private sealed class GitHubContentItem
        {
            public string Name { get; set; }

            public string Type { get; set; }
        }

        private sealed class GitHubTreeItem
        {
            public string Path { get; set; }

            public string Type { get; set; }
        }

        private static List<GitHubTreeItem> ParseGitHubTreeJson(string json)
        {
            var items = new List<GitHubTreeItem>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return items;
            }

            var pos = 0;
            while (pos < json.Length)
            {
                var pathKeyPos = json.IndexOf("\"path\"", pos, StringComparison.Ordinal);
                if (pathKeyPos < 0)
                {
                    break;
                }

                var pathColonPos = json.IndexOf(':', pathKeyPos + 6);
                if (pathColonPos < 0)
                {
                    break;
                }

                var pathValueStart = json.IndexOf('"', pathColonPos + 1);
                if (pathValueStart < 0)
                {
                    break;
                }

                pathValueStart++;
                var pathValueEnd = json.IndexOf('"', pathValueStart);
                if (pathValueEnd < 0)
                {
                    break;
                }

                var path = json.Substring(pathValueStart, pathValueEnd - pathValueStart);

                var typeKeyPos = json.IndexOf("\"type\"", pathValueEnd, StringComparison.Ordinal);
                if (typeKeyPos < 0)
                {
                    break;
                }

                var nextPathPos = json.IndexOf("\"path\"", pathValueEnd, StringComparison.Ordinal);
                if (nextPathPos > 0 && nextPathPos < typeKeyPos)
                {
                    pos = nextPathPos;
                    continue;
                }

                var typeColonPos = json.IndexOf(':', typeKeyPos + 6);
                if (typeColonPos < 0)
                {
                    break;
                }

                var typeValueStart = json.IndexOf('"', typeColonPos + 1);
                if (typeValueStart < 0)
                {
                    break;
                }

                typeValueStart++;
                var typeValueEnd = json.IndexOf('"', typeValueStart);
                if (typeValueEnd < 0)
                {
                    break;
                }

                var type = json.Substring(typeValueStart, typeValueEnd - typeValueStart);

                if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(type))
                {
                    items.Add(new GitHubTreeItem
                    {
                        Path = path,
                        Type = type
                    });
                }

                pos = typeValueEnd + 1;
            }

            return items;
        }

        private static List<GitHubContentItem> ParseGitHubContentsByTokenScan(string json)
        {
            var items = new List<GitHubContentItem>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return items;
            }

            var pos = 0;
            while (pos < json.Length)
            {
                var nameKeyPos = json.IndexOf("\"name\"", pos, StringComparison.Ordinal);
                if (nameKeyPos < 0)
                {
                    break;
                }

                var nameColonPos = json.IndexOf(':', nameKeyPos + 6);
                if (nameColonPos < 0)
                {
                    break;
                }

                var nameValueStart = json.IndexOf('"', nameColonPos + 1);
                if (nameValueStart < 0)
                {
                    break;
                }

                nameValueStart++;
                var nameValueEnd = json.IndexOf('"', nameValueStart);
                if (nameValueEnd < 0)
                {
                    break;
                }

                var name = json.Substring(nameValueStart, nameValueEnd - nameValueStart);

                var typeKeyPos = json.IndexOf("\"type\"", nameValueEnd, StringComparison.Ordinal);
                if (typeKeyPos < 0)
                {
                    break;
                }

                var nextNamePos = json.IndexOf("\"name\"", nameValueEnd, StringComparison.Ordinal);
                if (nextNamePos > 0 && nextNamePos < typeKeyPos)
                {
                    pos = nextNamePos;
                    continue;
                }

                var typeColonPos = json.IndexOf(':', typeKeyPos + 6);
                if (typeColonPos < 0)
                {
                    break;
                }

                var typeValueStart = json.IndexOf('"', typeColonPos + 1);
                if (typeValueStart < 0)
                {
                    break;
                }

                typeValueStart++;
                var typeValueEnd = json.IndexOf('"', typeValueStart);
                if (typeValueEnd < 0)
                {
                    break;
                }

                var type = json.Substring(typeValueStart, typeValueEnd - typeValueStart);

                if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type))
                {
                    items.Add(new GitHubContentItem
                    {
                        Name = name,
                        Type = type
                    });
                }

                pos = typeValueEnd + 1;
            }

            return items;
        }
    }

    /// <summary>
    /// Types of templates available from awesome-copilot.
    /// </summary>
    internal enum TemplateType
    {
        Agent,
        Prompt,
        Skill,
        Instructions
    }

    /// <summary>
    /// Information about a template from the awesome-copilot repository.
    /// </summary>
    internal class TemplateInfo
    {
        public string Name { get; set; }
        public string FileName { get; set; }
        public string DisplayName { get; set; }
        public string DownloadUrl { get; set; }
        public TemplateType TemplateType { get; set; }
        public string ProviderId { get; set; }

        /// <summary>
        /// Cached content of the template file.
        /// </summary>
        public string Content { get; set; }
    }
}
