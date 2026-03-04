using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text.Json;
using System.Threading;
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
        private const string _emptyCacheMarker = "__EMPTY__";
        private const int _cacheExpirationDays = 7;

        private static readonly HttpClient _httpClient = CreateHttpClient();
        private static readonly List<TemplateProvider> _providers = TemplateProviderRegistry.CreateProviders();
        private static readonly object _lastFetchIssueGate = new object();
        private static readonly Dictionary<string, string> _lastFetchIssues = new Dictionary<string, string>(StringComparer.Ordinal);
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
        /// Gets the latest fetch issue message for the specified template type and provider.
        /// </summary>
        public static string GetLastFetchIssue(TemplateType templateType, TemplateProvider provider)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            var key = GetFetchIssueKey(templateType, provider.Id);
            lock (_lastFetchIssueGate)
            {
                return _lastFetchIssues.TryGetValue(key, out var message) ? message : null;
            }
        }

        /// <summary>
        /// Gets templates for the specified type from the default provider.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(TemplateType templateType, CancellationToken cancellationToken = default)
        {
            TemplateProvider provider = GetDefaultProvider(templateType);
            if (provider == null)
            {
                return new List<TemplateInfo>();
            }

            return await GetTemplatesAsync(templateType, provider, forceRefresh: false, cancellationToken);
        }

        /// <summary>
        /// Gets templates for the specified type from the selected provider.
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(TemplateType templateType, TemplateProvider provider, bool forceRefresh = false, CancellationToken cancellationToken = default)
        {
            if (provider == null)
            {
                throw new ArgumentNullException(nameof(provider));
            }

            ClearLastFetchIssue(templateType, provider.Id);

            TemplateSearchRule rule = provider.GetRule(templateType);
            if (rule == null)
            {
                return new List<TemplateInfo>();
            }

            var cacheFile = GetCacheFilePath(templateType, provider.Id);

            // Check cache first unless caller requested a forced refresh.
            List<TemplateInfo> cached = forceRefresh ? null : LoadFromCache(cacheFile, expiredOk: false);
            if (cached != null)
            {
                return cached;
            }

            // Fetch from GitHub API
            cancellationToken.ThrowIfCancellationRequested();

            List<TemplateInfo> templates = await FetchTemplatesFromGitHubAsync(provider, rule, templateType, cancellationToken);

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

                if (string.IsNullOrWhiteSpace(GetLastFetchIssue(templateType, provider)))
                {
                    SetLastFetchIssue(templateType, provider.Id, "No templates found in the provider response. This can happen due to API limits or provider changes.");
                }
            }

            return templates;
        }

        /// <summary>
        /// Gets the content of a template file from GitHub.
        /// </summary>
        public static async Task<string> GetTemplateContentAsync(TemplateInfo template, CancellationToken cancellationToken = default)
        {
            TemplateContentResult result = await GetTemplateContentResultAsync(template, cancellationToken);
            return result.Content;
        }

        /// <summary>
        /// Gets the content of a template file from GitHub with status details.
        /// </summary>
        public static async Task<TemplateContentResult> GetTemplateContentResultAsync(TemplateInfo template, CancellationToken cancellationToken = default)
        {
            if (string.IsNullOrEmpty(template?.DownloadUrl))
            {
                return TemplateContentResult.Empty;
            }

            try
            {
                using HttpResponseMessage response = await _httpClient.GetAsync(template.DownloadUrl, cancellationToken);
                if (!response.IsSuccessStatusCode)
                {
                    string friendlyMessage = GetFriendlyHttpErrorMessage(response.StatusCode, response.Headers)
                        ?? $"Failed to load template content from GitHub ({(int)response.StatusCode}).";

                    return TemplateContentResult.FromError(friendlyMessage);
                }

                string content = await response.Content.ReadAsStringAsync();
                return TemplateContentResult.FromContent(content);
            }
            catch (HttpRequestException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.GetTemplateContentAsync failed for '{template?.DownloadUrl}': {ex}");

                return TemplateContentResult.FromError("Failed to fetch template content from GitHub.");
            }
            catch (TaskCanceledException ex)
            {
                if (cancellationToken.IsCancellationRequested)
                {
                    throw;
                }

                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.GetTemplateContentAsync timed out for '{template?.DownloadUrl}': {ex}");

                return TemplateContentResult.FromError("GitHub request timed out while loading template content.");
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

                if (lines.Length == 1 && string.Equals(lines[0], _emptyCacheMarker, StringComparison.Ordinal))
                {
                    return new List<TemplateInfo>();
                }

                var templates = new List<TemplateInfo>();

                foreach (var line in lines)
                {
                    var parts = line.Split('\t');
                    if (parts.Length < 4)
                    {
                        continue;
                    }

                    if (!int.TryParse(parts[3], out var templateTypeValue) ||
                        !Enum.IsDefined(typeof(TemplateType), templateTypeValue))
                    {
                        continue;
                    }

                    var name = parts[0];
                    var fileName = parts[1];
                    var downloadUrl = parts[2];

                    if (string.IsNullOrWhiteSpace(name) ||
                        string.IsNullOrWhiteSpace(fileName) ||
                        string.IsNullOrWhiteSpace(downloadUrl))
                    {
                        continue;
                    }

                    templates.Add(new TemplateInfo
                    {
                        Name = name,
                        FileName = fileName,
                        DownloadUrl = downloadUrl,
                        TemplateType = (TemplateType)templateTypeValue,
                        ProviderId = parts.Length >= 5 && !string.IsNullOrWhiteSpace(parts[4])
                            ? parts[4]
                            : AwesomeCopilotTemplateProvider.ProviderId,
                        DisplayName = parts.Length >= 6 && !string.IsNullOrWhiteSpace(parts[5])
                            ? parts[5]
                            : name
                    });
                }

                return templates;
            }
            catch (IOException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.LoadFromCache failed for '{cacheFile}': {ex}");
                return null;
            }
            catch (UnauthorizedAccessException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.LoadFromCache failed for '{cacheFile}': {ex}");
                return null;
            }
        }

        private static void SaveToCache(string cacheFile, List<TemplateInfo> templates)
        {
            if (templates == null)
            {
                return;
            }

            try
            {
                Directory.CreateDirectory(Path.GetDirectoryName(cacheFile));

                var lines = new List<string>();
                if (templates.Count == 0)
                {
                    lines.Add(_emptyCacheMarker);
                }

                foreach (TemplateInfo t in templates)
                {
                    lines.Add($"{t.Name}\t{t.FileName}\t{t.DownloadUrl}\t{(int)t.TemplateType}\t{t.ProviderId}\t{t.DisplayName}");
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

        private static async Task<List<TemplateInfo>> FetchTemplatesFromGitHubAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType, CancellationToken cancellationToken)
        {
            if (rule.Recursive)
            {
                return await FetchTemplatesFromTreeAsync(provider, rule, templateType, cancellationToken);
            }

            return await FetchTemplatesFromDirectoryAsync(provider, rule, templateType, cancellationToken);
        }

        private static async Task<List<TemplateInfo>> FetchTemplatesFromDirectoryAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType, CancellationToken cancellationToken)
        {
            var templates = new List<TemplateInfo>();

            try
            {
                // Get directory contents from GitHub API
                var url = $"{_gitHubApiBase}/repos/{provider.RepoOwner}/{provider.RepoName}/contents/{rule.RootPath}?ref={provider.Branch}";
                var response = await GetGitHubApiResponseContentAsync(url, templateType, provider, cancellationToken);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return templates;
                }

                List<GitHubContentItem> items = ParseGitHubContentsJson(response);
                if (items.Count == 0)
                {
                    _ = new InvalidOperationException($"GitHub template response could not be parsed for contents endpoint: {provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}").LogAsync();
                }

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
                        return await FetchFolderTemplatesFromTreeAsync(provider, rule, templateType, cancellationToken);
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromDirectoryAsync failed for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to fetch templates from GitHub.");
            }
            catch (TaskCanceledException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromDirectoryAsync timed out for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "GitHub request timed out while fetching templates.");
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromDirectoryAsync failed to parse response for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to parse template response from GitHub.");
            }
            catch (JsonException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromDirectoryAsync failed to parse response for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to parse template response from GitHub.");
            }

            return templates;
        }

        private static async Task<List<TemplateInfo>> FetchFolderTemplatesFromTreeAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType, CancellationToken cancellationToken)
        {
            var templates = new List<TemplateInfo>();

            try
            {
                var url = $"{_gitHubApiBase}/repos/{provider.RepoOwner}/{provider.RepoName}/git/trees/{provider.Branch}?recursive=1";
                var response = await GetGitHubApiResponseContentAsync(url, templateType, provider, cancellationToken);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return templates;
                }

                List<GitHubTreeItem> treeItems = ParseGitHubTreeJson(response);
                if (treeItems.Count == 0)
                {
                    _ = new InvalidOperationException($"GitHub template response could not be parsed for tree endpoint: {provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}").LogAsync();
                }

                var rootPrefix = NormalizePath(rule.RootPath) + "/";
                var selectedFileByFolder = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);

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

                    var relativePath = normalizedPath.Substring(rootPrefix.Length);
                    var segments = relativePath.Split('/');
                    if (segments.Length != 2)
                    {
                        continue;
                    }

                    var folderName = segments[0];
                    var fileName = segments[1];
                    if (!IsRuleMatch(fileName, rule) &&
                        !fileName.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase))
                    {
                        continue;
                    }

                    if (!selectedFileByFolder.ContainsKey(folderName) ||
                        fileName.Equals(rule.FileName, StringComparison.OrdinalIgnoreCase))
                    {
                        selectedFileByFolder[folderName] = fileName;
                    }
                }

                foreach (KeyValuePair<string, string> entry in selectedFileByFolder)
                {
                    templates.Add(new TemplateInfo
                    {
                        Name = entry.Key,
                        FileName = entry.Key,
                        DisplayName = entry.Key,
                        DownloadUrl = $"{_gitHubRawBase}/{provider.RepoOwner}/{provider.RepoName}/{provider.Branch}/{rule.RootPath}/{entry.Key}/{entry.Value}",
                        TemplateType = templateType,
                        ProviderId = provider.Id
                    });
                }
            }
            catch (HttpRequestException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchFolderTemplatesFromTreeAsync failed for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to fetch templates from GitHub.");
            }
            catch (TaskCanceledException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchFolderTemplatesFromTreeAsync timed out for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "GitHub request timed out while fetching templates.");
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchFolderTemplatesFromTreeAsync failed to parse response for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to parse template response from GitHub.");
            }
            catch (JsonException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchFolderTemplatesFromTreeAsync failed to parse response for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to parse template response from GitHub.");
            }

            return templates;
        }

        private static async Task<List<TemplateInfo>> FetchTemplatesFromTreeAsync(TemplateProvider provider, TemplateSearchRule rule, TemplateType templateType, CancellationToken cancellationToken)
        {
            var templates = new List<TemplateInfo>();

            try
            {
                var url = $"{_gitHubApiBase}/repos/{provider.RepoOwner}/{provider.RepoName}/git/trees/{provider.Branch}?recursive=1";
                var response = await GetGitHubApiResponseContentAsync(url, templateType, provider, cancellationToken);
                if (string.IsNullOrWhiteSpace(response))
                {
                    return templates;
                }

                List<GitHubTreeItem> treeItems = ParseGitHubTreeJson(response);
                if (treeItems.Count == 0)
                {
                    _ = new InvalidOperationException($"GitHub template response could not be parsed for recursive tree endpoint: {provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}").LogAsync();
                }

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

                    var relativePath = normalizedPath.Substring(rootPrefix.Length);
                    var displayName = GetTemplateDisplayName(provider, templateType, relativePath, fileName);

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
                SetLastFetchIssue(templateType, provider.Id, "Failed to fetch templates from GitHub.");
            }
            catch (TaskCanceledException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromTreeAsync timed out for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "GitHub request timed out while fetching templates.");
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromTreeAsync failed to parse response for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to parse template response from GitHub.");
            }
            catch (JsonException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromTreeAsync failed to parse response for '{provider.RepoOwner}/{provider.RepoName}/{rule.RootPath}': {ex}");
                SetLastFetchIssue(templateType, provider.Id, "Failed to parse template response from GitHub.");
            }

            return templates;
        }

        private static string GetTemplateDisplayName(TemplateProvider provider, TemplateType templateType, string relativePath, string fileName)
        {
            if (provider != null &&
                templateType == TemplateType.Agent &&
                string.Equals(provider.Id, DotNetSkillsTemplateProvider.ProviderId, StringComparison.OrdinalIgnoreCase))
            {
                return fileName;
            }

            return relativePath;
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
        /// Uses a lightweight token scan to extract name and type values.
        /// </summary>
        private static List<GitHubContentItem> ParseGitHubContentsJson(string json)
        {
            var items = new List<GitHubContentItem>();
            if (string.IsNullOrWhiteSpace(json))
            {
                return items;
            }

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;

            if (root.ValueKind == JsonValueKind.Object)
            {
                AddContentItem(items, root);
                return items;
            }

            if (root.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            foreach (JsonElement item in root.EnumerateArray())
            {
                AddContentItem(items, item);
            }

            return items;
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

            using JsonDocument document = JsonDocument.Parse(json);
            JsonElement root = document.RootElement;
            if (root.ValueKind != JsonValueKind.Object ||
                !root.TryGetProperty("tree", out JsonElement treeItems) ||
                treeItems.ValueKind != JsonValueKind.Array)
            {
                return items;
            }

            foreach (JsonElement treeItem in treeItems.EnumerateArray())
            {
                if (treeItem.ValueKind != JsonValueKind.Object)
                {
                    continue;
                }

                string path = GetStringProperty(treeItem, "path");
                string type = GetStringProperty(treeItem, "type");

                if (!string.IsNullOrWhiteSpace(path) && !string.IsNullOrWhiteSpace(type))
                {
                    items.Add(new GitHubTreeItem
                    {
                        Path = path,
                        Type = type
                    });
                }
            }

            return items;
        }

        private static void AddContentItem(List<GitHubContentItem> items, JsonElement contentObject)
        {
            string name = GetStringProperty(contentObject, "name");
            string type = GetStringProperty(contentObject, "type");
            if (string.IsNullOrWhiteSpace(name) || string.IsNullOrWhiteSpace(type))
            {
                return;
            }

            items.Add(new GitHubContentItem
            {
                Name = name,
                Type = type
            });
        }

        private static string GetStringProperty(JsonElement element, string key)
        {
            if (element.ValueKind != JsonValueKind.Object ||
                !element.TryGetProperty(key, out JsonElement value) ||
                value.ValueKind != JsonValueKind.String)
            {
                return null;
            }

            return value.GetString();
        }

        private static async Task<string> GetGitHubApiResponseContentAsync(string url, TemplateType templateType, TemplateProvider provider, CancellationToken cancellationToken)
        {
            using (HttpResponseMessage response = await _httpClient.GetAsync(url, cancellationToken))
            {
                if (response.IsSuccessStatusCode)
                {
                    return await response.Content.ReadAsStringAsync();
                }

                string message = GetFriendlyHttpErrorMessage(response.StatusCode, response.Headers) ?? "Failed to fetch templates from GitHub.";
                SetLastFetchIssue(templateType, provider.Id, message);
                Debug.WriteLine($"AwesomeCopilotService.GetGitHubApiResponseContentAsync returned {(int)response.StatusCode} ({response.ReasonPhrase}) for '{url}'.");

                return null;
            }
        }

        private static string GetFriendlyHttpErrorMessage(HttpStatusCode statusCode, HttpResponseHeaders headers)
        {
            if (IsRateLimitResponse(statusCode, headers))
            {
                return "GitHub API rate limit reached. Please wait and try again.";
            }

            if (statusCode == HttpStatusCode.Unauthorized)
            {
                return "GitHub API authentication failed. Please check your GitHub credentials and try again.";
            }

            if ((int)statusCode >= 500)
            {
                return "GitHub API is temporarily unavailable. Please try again later.";
            }

            return null;
        }

        private static bool IsRateLimitResponse(HttpStatusCode statusCode, HttpResponseHeaders headers)
        {
            if (statusCode != HttpStatusCode.Forbidden && (int)statusCode != 429)
            {
                return false;
            }

            if (TryGetHeaderValue(headers, "X-RateLimit-Remaining", out var remaining) &&
                remaining == "0")
            {
                return true;
            }

            return TryGetHeaderValue(headers, "Retry-After", out _);
        }

        private static bool TryGetHeaderValue(HttpResponseHeaders headers, string name, out string value)
        {
            value = null;

            if (headers == null || !headers.TryGetValues(name, out IEnumerable<string> values))
            {
                return false;
            }

            foreach (var headerValue in values)
            {
                if (!string.IsNullOrWhiteSpace(headerValue))
                {
                    value = headerValue;
                    return true;
                }
            }

            return false;
        }

        private static void ClearLastFetchIssue(TemplateType templateType, string providerId)
        {
            var key = GetFetchIssueKey(templateType, providerId);
            lock (_lastFetchIssueGate)
            {
                _lastFetchIssues.Remove(key);
            }
        }

        private static void SetLastFetchIssue(TemplateType templateType, string providerId, string message)
        {
            if (string.IsNullOrWhiteSpace(message))
            {
                return;
            }

            var key = GetFetchIssueKey(templateType, providerId);
            lock (_lastFetchIssueGate)
            {
                _lastFetchIssues[key] = message;
            }
        }

        private static string GetFetchIssueKey(TemplateType templateType, string providerId)
            => $"{providerId}_{templateType}";
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

    internal sealed class TemplateContentResult
    {
        public static TemplateContentResult Empty { get; } = new TemplateContentResult();

        public string Content { get; private set; }

        public string ErrorMessage { get; private set; }

        public bool HasError => !string.IsNullOrWhiteSpace(ErrorMessage);

        public static TemplateContentResult FromContent(string content)
            => new TemplateContentResult { Content = content };

        public static TemplateContentResult FromError(string errorMessage)
            => new TemplateContentResult { ErrorMessage = errorMessage };
    }
}
