using System.Collections.Generic;
using System.Collections;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Reflection;
using System.Threading.Tasks;

namespace GitHubNode.Services
{
    /// <summary>
    /// Service for fetching templates from github/awesome-copilot repository.
    /// Caches results to disk with weekly expiration.
    /// </summary>
    internal static class AwesomeCopilotService
    {
        private const string _repoOwner = "github";
        private const string _repoName = "awesome-copilot";
        private const string _branch = "main";
        private const string _gitHubApiBase = "https://api.github.com";
        private const string _gitHubRawBase = "https://raw.githubusercontent.com";
        private const int _cacheExpirationDays = 7;

        private static readonly HttpClient _httpClient = CreateHttpClient();
        private static readonly string _cacheDirectory = Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GitHubNode",
            "TemplateCache");

        private static HttpClient CreateHttpClient()
        {
            // Ensure TLS 1.2 is enabled for GitHub API (required for .NET Framework 4.8)
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
        /// Gets templates for the specified type (agents, prompts, skills, instructions).
        /// </summary>
        public static async Task<List<TemplateInfo>> GetTemplatesAsync(TemplateType templateType)
        {
            var folderName = GetFolderName(templateType);
            var cacheFile = GetCacheFilePath(templateType);

            // Check cache first
            List<TemplateInfo> cached = LoadFromCache(cacheFile, expiredOk: false);
            if (cached != null)
            {
                return cached;
            }

            // Fetch from GitHub API
            List<TemplateInfo> templates = await FetchTemplatesFromGitHubAsync(folderName, templateType);

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
                Debug.WriteLine($"AwesomeCopilotService.GetTemplateContentAsync failed for '{template?.DownloadUrl}': {ex}");
                return null;
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.GetTemplateContentAsync timed out for '{template?.DownloadUrl}': {ex}");
                return null;
            }
        }

        /// <summary>
        /// Clears the cache for the specified template type.
        /// </summary>
        public static void ClearCache(TemplateType templateType)
        {
            try
            {
                var cacheFile = GetCacheFilePath(templateType);
                if (File.Exists(cacheFile))
                {
                    File.Delete(cacheFile);
                }
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.ClearCache failed for '{templateType}': {ex}");
            }
        }

        private static string GetFolderName(TemplateType templateType)
        {
            return templateType switch
            {
                TemplateType.Agent => "agents",
                TemplateType.Prompt => "prompts",
                TemplateType.Skill => "skills",
                TemplateType.Instructions => "instructions",
                _ => throw new ArgumentException($"Unknown template type: {templateType}"),
            };
        }

        private static string GetCacheFilePath(TemplateType templateType)
        {
            return Path.Combine(_cacheDirectory, $"{templateType}.cache");
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
                            TemplateType = (TemplateType)int.Parse(parts[3])
                        });
                    }
                }

                return templates;
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.LoadFromCache failed for '{cacheFile}': {ex}");
                return null;
            }
            catch (InvalidOperationException ex)
            {
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
                    lines.Add($"{t.Name}\t{t.FileName}\t{t.DownloadUrl}\t{(int)t.TemplateType}");
                }

                File.WriteAllLines(cacheFile, lines);
            }
            catch (IOException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.SaveToCache failed for '{cacheFile}': {ex}");
            }
            catch (UnauthorizedAccessException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.SaveToCache failed for '{cacheFile}': {ex}");
            }
        }

        private static async Task<List<TemplateInfo>> FetchTemplatesFromGitHubAsync(string folderName, TemplateType templateType)
        {
            var templates = new List<TemplateInfo>();

            try
            {
                // Get directory contents from GitHub API
                var url = $"{_gitHubApiBase}/repos/{_repoOwner}/{_repoName}/contents/{folderName}?ref={_branch}";
                var response = await _httpClient.GetStringAsync(url);
                List<GitHubContentItem> items = ParseGitHubContentsJson(response);

                foreach (GitHubContentItem item in items)
                {
                    if (item.Type == "file" && item.Name.EndsWith(".md", StringComparison.OrdinalIgnoreCase))
                    {
                        templates.Add(new TemplateInfo
                        {
                            Name = Path.GetFileNameWithoutExtension(item.Name),
                            FileName = item.Name,
                            DownloadUrl = $"{_gitHubRawBase}/{_repoOwner}/{_repoName}/{_branch}/{folderName}/{item.Name}",
                            TemplateType = templateType
                        });
                    }
                    else if (item.Type == "dir" && templateType == TemplateType.Skill)
                    {
                        // Skills are folders - look for skill.md inside
                        var skillUrl = $"{_gitHubApiBase}/repos/{_repoOwner}/{_repoName}/contents/{folderName}/{item.Name}?ref={_branch}";
                        try
                        {
                            var skillResponse = await _httpClient.GetStringAsync(skillUrl);
                            List<GitHubContentItem> skillItems = ParseGitHubContentsJson(skillResponse);
                            GitHubContentItem skillFile = skillItems.Find(f =>
                                f.Name.Equals("skill.md", StringComparison.OrdinalIgnoreCase) ||
                                f.Name.EndsWith(".skill.md", StringComparison.OrdinalIgnoreCase));

                            if (skillFile != null)
                            {
                                templates.Add(new TemplateInfo
                                {
                                    Name = item.Name,
                                    FileName = item.Name,
                                    DownloadUrl = $"{_gitHubRawBase}/{_repoOwner}/{_repoName}/{_branch}/{folderName}/{item.Name}/{skillFile.Name}",
                                    TemplateType = templateType
                                });
                            }
                        }
                        catch (HttpRequestException ex)
                        {
                            Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromGitHubAsync failed to fetch skill folder '{item.Name}': {ex}");
                        }
                        catch (TaskCanceledException ex)
                        {
                            Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromGitHubAsync timed out for skill folder '{item.Name}': {ex}");
                        }
                    }
                }
            }
            catch (HttpRequestException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromGitHubAsync failed for '{folderName}': {ex}");
            }
            catch (TaskCanceledException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.FetchTemplatesFromGitHubAsync timed out for '{folderName}': {ex}");
            }

            return templates;
        }

        /// <summary>
        /// Parses the GitHub contents API JSON response.
        /// Uses JSON deserialization to extract name and type values.
        /// </summary>
        private static List<GitHubContentItem> ParseGitHubContentsJson(string json)
        {
            var items = new List<GitHubContentItem>();

            if (string.IsNullOrWhiteSpace(json))
            {
                return items;
            }

            try
            {
                object deserialized = DeserializeJson(json);
                if (deserialized is not IEnumerable entries)
                {
                    return items;
                }

                foreach (object entry in entries)
                {
                    if (entry is not IDictionary item)
                    {
                        continue;
                    }

                    var name = item.Contains("name") ? item["name"] as string : null;
                    var type = item.Contains("type") ? item["type"] as string : null;
                    if (!string.IsNullOrWhiteSpace(name) && !string.IsNullOrWhiteSpace(type))
                    {
                        items.Add(new GitHubContentItem
                        {
                            Name = name,
                            Type = type
                        });
                    }
                }
            }
            catch (InvalidOperationException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.ParseGitHubContentsJson failed: {ex}");
            }
            catch (ArgumentException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.ParseGitHubContentsJson failed: {ex}");
            }
            catch (TargetInvocationException ex)
            {
                Debug.WriteLine($"AwesomeCopilotService.ParseGitHubContentsJson failed: {ex}");
            }

            return items;
        }

        private sealed class GitHubContentItem
        {
            public string Name { get; set; }

            public string Type { get; set; }
        }

        private static object DeserializeJson(string json)
        {
            Type serializerType = Type.GetType("System.Web.Script.Serialization.JavaScriptSerializer, System.Web.Extensions", throwOnError: false);
            if (serializerType == null)
            {
                throw new InvalidOperationException("System.Web.Extensions is required for JSON parsing.");
            }

            object serializer = Activator.CreateInstance(serializerType);
            MethodInfo deserializeMethod = serializerType.GetMethod("DeserializeObject", [typeof(string)]);
            if (deserializeMethod == null)
            {
                throw new InvalidOperationException("JavaScriptSerializer.DeserializeObject method was not found.");
            }

            return deserializeMethod.Invoke(serializer, [json]);
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
        public string DownloadUrl { get; set; }
        public TemplateType TemplateType { get; set; }

        /// <summary>
        /// Cached content of the template file.
        /// </summary>
        public string Content { get; set; }
    }
}
