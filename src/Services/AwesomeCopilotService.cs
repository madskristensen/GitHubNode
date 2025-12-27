using System.Collections.Generic;
using System.IO;
using System.Net.Http;
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
            List<TemplateInfo> cached = LoadFromCache(cacheFile);
            if (cached != null)
            {
                return cached;
            }

            // Fetch from GitHub API
            List<TemplateInfo> templates = await FetchTemplatesFromGitHubAsync(folderName, templateType);

            // Save to cache
            SaveToCache(cacheFile, templates);

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
            catch
            {
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
            catch
            {
                // Ignore cache deletion failures
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

        private static List<TemplateInfo> LoadFromCache(string cacheFile)
        {
            try
            {
                if (!File.Exists(cacheFile))
                {
                    return null;
                }

                var fileInfo = new FileInfo(cacheFile);
                if (fileInfo.LastWriteTimeUtc < DateTime.UtcNow.AddDays(-_cacheExpirationDays))
                {
                    // Cache expired
                    return null;
                }

                var lines = File.ReadAllLines(cacheFile);

                // If cache is empty (0 templates), treat as expired to re-fetch
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
            catch
            {
                return null;
            }
        }

        private static void SaveToCache(string cacheFile, List<TemplateInfo> templates)
        {
            // Don't cache empty results - they might be due to API failures
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
            catch
            {
                // Ignore cache write failures
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
                        catch
                        {
                            // Skip this skill folder on error
                        }
                    }
                }
            }
            catch
            {
                // Return empty list on API failure
            }

            return templates;
        }

        /// <summary>
        /// Parses the GitHub contents API JSON response.
        /// Uses simple string parsing to extract name and type values.
        /// </summary>
        private static List<GitHubContentItem> ParseGitHubContentsJson(string json)
        {
            var items = new List<GitHubContentItem>();

            // Find all "name" and "type" pairs in the JSON
            var pos = 0;
            while (pos < json.Length)
            {
                // Find the next "name" key
                var nameKeyPos = json.IndexOf("\"name\"", pos, StringComparison.Ordinal);
                if (nameKeyPos < 0) break;

                // Find the colon after "name"
                var colonPos = json.IndexOf(':', nameKeyPos + 6);
                if (colonPos < 0) break;

                // Find the opening quote of the value (skip whitespace)
                var nameValueStart = json.IndexOf('"', colonPos + 1);
                if (nameValueStart < 0) break;
                nameValueStart++; // Skip opening quote

                var nameValueEnd = json.IndexOf('"', nameValueStart);
                if (nameValueEnd < 0) break;

                var name = json.Substring(nameValueStart, nameValueEnd - nameValueStart);

                // Find the "type" key after this name
                var typeKeyPos = json.IndexOf("\"type\"", nameValueEnd, StringComparison.Ordinal);
                if (typeKeyPos < 0) break;

                // Make sure we're still in the same object
                // If we hit another "name" before "type", skip this one
                var nextNamePos = json.IndexOf("\"name\"", nameValueEnd, StringComparison.Ordinal);
                if (nextNamePos > 0 && nextNamePos < typeKeyPos)
                {
                    pos = nextNamePos;
                    continue;
                }

                // Find the colon after "type"
                var typeColonPos = json.IndexOf(':', typeKeyPos + 6);
                if (typeColonPos < 0) break;

                // Find the opening quote of the type value
                var typeValueStart = json.IndexOf('"', typeColonPos + 1);
                if (typeValueStart < 0) break;
                typeValueStart++; // Skip opening quote

                var typeValueEnd = json.IndexOf('"', typeValueStart);
                if (typeValueEnd < 0) break;

                var type = json.Substring(typeValueStart, typeValueEnd - typeValueStart);

                items.Add(new GitHubContentItem
                {
                    Name = name,
                    Type = type
                });

                pos = typeValueEnd + 1;
            }

            return items;
        }

        private class GitHubContentItem
        {
            public string Name { get; set; }
            public string Type { get; set; }
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
