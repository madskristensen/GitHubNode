using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text.Json;
using System.Text.Json.Serialization;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Services.Marketplace
{
    internal static class WellKnownDiscoveryService
    {
        public const string CurrentSchemaUri = "https://schemas.agentskills.io/discovery/0.2.0/schema.json";
        public const string SkillsWellKnownIndexPath = "/.well-known/agent-skills/index.json";
        public const string LegacySkillsWellKnownIndexPath = "/.well-known/skills/index.json";

        // Kept for backward source compatibility with earlier code paths.
        public const string WellKnownIndexPath = SkillsWellKnownIndexPath;
        public const string LegacyWellKnownIndexPath = LegacySkillsWellKnownIndexPath;

        private static readonly Regex _skillNameRegex = new Regex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.Compiled);
        private static readonly Regex _digestRegex = new Regex("^sha256:[0-9a-f]{64}$", RegexOptions.Compiled);
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Tries to parse user input into a Well-Known origin URI such as
        /// https://docs.stripe.com/. Accepts a domain, an http(s) origin, or a
        /// legacy /.well-known/... URL (in which case the origin is extracted).
        /// </summary>
        internal static bool TryCreateOriginUri(string input, out Uri originUri)
        {
            originUri = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var trimmed = input.Trim();
            if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri))
            {
                if (!IsHttpUri(absoluteUri))
                {
                    return false;
                }

                if (IsWellKnownDiscoveryUri(absoluteUri))
                {
                    originUri = new Uri(absoluteUri.GetLeftPart(UriPartial.Authority) + "/");
                    return true;
                }

                if (string.IsNullOrEmpty(absoluteUri.AbsolutePath) || absoluteUri.AbsolutePath == "/")
                {
                    originUri = new Uri(absoluteUri.GetLeftPart(UriPartial.Authority) + "/");
                    return true;
                }

                return false;
            }

            if (!trimmed.Contains("/") && trimmed.Contains("."))
            {
                originUri = new Uri($"https://{trimmed.TrimEnd('/')}/");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Backwards-compatible wrapper. Returns the origin URI rather than the
        /// agent-skills index URL because the well-known marketplace is no longer
        /// limited to the agent-skills sub type.
        /// </summary>
        internal static bool TryCreateIndexUri(string input, out Uri indexUri)
        {
            return TryCreateOriginUri(input, out indexUri);
        }

        internal static string GetSourceId(Uri originUri)
        {
            if (originUri == null)
            {
                throw new ArgumentNullException(nameof(originUri));
            }

            return $"well-known:{originUri.GetLeftPart(UriPartial.Authority)}/";
        }

        internal static string GetDisplayName(Uri originUri)
        {
            if (originUri == null)
            {
                return "Well-Known Marketplace";
            }

            return originUri.Host;
        }

        /// <summary>
        /// Gets the origin URL displayed in the marketplace list (no
        /// agent-skills, mcp, or other sub-type path is shown).
        /// </summary>
        internal static string GetDisplayUrl(Uri originUri)
        {
            if (originUri == null)
            {
                return null;
            }

            return originUri.GetLeftPart(UriPartial.Authority) + "/";
        }

        internal static bool IsValidSkillName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && _skillNameRegex.IsMatch(name)
                && !name.Contains("--");
        }

        internal static bool IsValidDigest(string digest)
        {
            return !string.IsNullOrWhiteSpace(digest) && _digestRegex.IsMatch(digest);
        }

        internal static Uri ResolveArtifactUri(Uri indexUri, string url)
        {
            if (indexUri == null)
            {
                throw new ArgumentNullException(nameof(indexUri));
            }

            if (string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return Uri.TryCreate(indexUri, url, out var resolvedUri) && IsHttpUri(resolvedUri)
                ? resolvedUri
                : null;
        }

        public static async Task<WellKnownDiscoveryResult> DiscoverAsync(
            MarketplaceEntry entry,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (entry == null)
            {
                throw new ArgumentNullException(nameof(entry));
            }

            if (!TryCreateOriginUri(entry.WellKnownIndexUrl ?? entry.RepositoryUrl, out var originUri))
            {
                throw new InvalidOperationException("Invalid Well-Known Discovery URL.");
            }

            if (!IsTrustedHttpUri(originUri))
            {
                throw new InvalidOperationException("Well-Known Discovery sources must use HTTPS unless they are hosted on localhost.");
            }

            var skillsIndexUri = new Uri(originUri.GetLeftPart(UriPartial.Authority) + SkillsWellKnownIndexPath);

            var result = new WellKnownDiscoveryResult
            {
                Id = GetSourceId(originUri),
                IndexUri = originUri,
                Origin = originUri.GetLeftPart(UriPartial.Authority),
                DisplayName = string.IsNullOrWhiteSpace(entry.DisplayName) ? GetDisplayName(originUri) : entry.DisplayName,
                CacheDirectory = MarketplaceStorageService.GetWellKnownDiscoveryDirectory(originUri),
                LastUpdated = DateTime.UtcNow
            };

            Directory.CreateDirectory(result.CacheDirectory);
            result.IconPath = await CacheFaviconAsync(originUri, forceRefresh, cancellationToken);

            var (indexBytes, resolvedIndexUri) = await DownloadIndexBytesAsync(skillsIndexUri, cancellationToken);

            if (indexBytes == null)
            {
                // No skills sub-type at this origin. The well-known marketplace
                // can still surface other sub-types (such as MCP servers) that
                // are discovered separately by the caller.
                return result;
            }

            // Use the URI that actually returned the index (which may be the
            // legacy /.well-known/skills/ path) as the base for resolving
            // relative skill artifact URLs. Using the original agent-skills URI
            // here would produce 404s for sources that only publish the legacy
            // skills index (for example docs.stripe.com).
            var indexUri = resolvedIndexUri ?? skillsIndexUri;

            var rawIndex = ParseIndex(indexBytes);
            var isLegacyIndex = string.IsNullOrWhiteSpace(rawIndex.Schema);

            if (!isLegacyIndex && !string.Equals(rawIndex.Schema, CurrentSchemaUri, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Unsupported Well-Known Discovery schema '{rawIndex.Schema ?? "<missing>"}'.");
            }

            if (isLegacyIndex)
            {
                result.Warnings.Add("This source uses the legacy /.well-known/skills index format without published digests. Artifacts are cached after download but cannot be verified against publisher-provided digests.");
            }

            if (rawIndex.Skills == null)
            {
                throw new InvalidOperationException("Well-Known Discovery index does not contain a skills array.");
            }

            foreach (var rawSkill in rawIndex.Skills)
            {
                cancellationToken.ThrowIfCancellationRequested();
                string validationError;
                Uri artifactUri;
                string localSkillPath;

                if (isLegacyIndex)
                {
                    validationError = ValidateLegacySkillEntry(indexUri, rawSkill, out artifactUri);
                    if (!string.IsNullOrEmpty(validationError))
                    {
                        result.Warnings.Add(validationError);
                        continue;
                    }

                    localSkillPath = await CacheLegacySkillAsync(result.CacheDirectory, rawSkill, indexUri, forceRefresh, cancellationToken);
                }
                else
                {
                    validationError = ValidateSkillEntry(indexUri, rawSkill, out artifactUri);
                    if (!string.IsNullOrEmpty(validationError))
                    {
                        result.Warnings.Add(validationError);
                        continue;
                    }

                    localSkillPath = string.Equals(rawSkill.Type, "archive", StringComparison.OrdinalIgnoreCase)
                        ? await CacheSkillArchiveAsync(result.CacheDirectory, rawSkill, artifactUri, forceRefresh, cancellationToken)
                        : await CacheSkillMarkdownAsync(result.CacheDirectory, rawSkill, artifactUri, forceRefresh, cancellationToken);
                }
                result.Skills.Add(new WellKnownDiscoverySkill
                {
                    Name = rawSkill.Name,
                    Description = rawSkill.Description,
                    Type = string.IsNullOrWhiteSpace(rawSkill.Type) ? "skill-md" : rawSkill.Type,
                    ArtifactUri = artifactUri,
                    Digest = rawSkill.Digest,
                    LocalSkillPath = localSkillPath
                });
            }

            return result;
        }

        private static RawDiscoveryIndex ParseIndex(byte[] indexBytes)
        {
            try
            {
                return JsonSerializer.Deserialize<RawDiscoveryIndex>(indexBytes, new JsonSerializerOptions
                {
                    PropertyNameCaseInsensitive = true
                }) ?? throw new InvalidOperationException("Well-Known Discovery index is empty.");
            }
            catch (JsonException ex)
            {
                Debug.WriteLine($"WellKnownDiscoveryService.ParseIndex failed: {ex}");
                _ = ex.LogAsync();
                throw new InvalidOperationException("Well-Known Discovery index is not valid JSON.", ex);
            }
        }

        private static string ValidateLegacySkillEntry(Uri indexUri, RawDiscoverySkill rawSkill, out Uri artifactUri)
        {
            artifactUri = null;

            if (rawSkill == null)
            {
                return "Skipped an empty skill entry.";
            }

            if (!IsValidSkillName(rawSkill.Name))
            {
                return $"Skipped skill with invalid name '{rawSkill.Name}'.";
            }

            if (string.IsNullOrWhiteSpace(rawSkill.Description) || rawSkill.Description.Length > 1024)
            {
                return $"Skipped skill '{rawSkill.Name}' because its description is missing or too long.";
            }

            if (rawSkill.Files == null || !rawSkill.Files.Contains("SKILL.md"))
            {
                return $"Skipped skill '{rawSkill.Name}' because its legacy files list does not include SKILL.md.";
            }

            artifactUri = ResolveArtifactUri(indexUri, rawSkill.Name + "/SKILL.md");
            return artifactUri == null || !IsTrustedHttpUri(artifactUri)
                ? $"Skipped skill '{rawSkill.Name}' because its SKILL.md URL is invalid."
                : null;
        }

        private static string ValidateSkillEntry(Uri indexUri, RawDiscoverySkill rawSkill, out Uri artifactUri)
        {
            artifactUri = null;

            if (rawSkill == null)
            {
                return "Skipped an empty skill entry.";
            }

            if (!IsValidSkillName(rawSkill.Name))
            {
                return $"Skipped skill with invalid name '{rawSkill.Name}'.";
            }

            if (!string.Equals(rawSkill.Type, "skill-md", StringComparison.OrdinalIgnoreCase) &&
                !string.Equals(rawSkill.Type, "archive", StringComparison.OrdinalIgnoreCase))
            {
                return $"Skipped skill '{rawSkill.Name}' because type '{rawSkill.Type}' is not supported by the RFC.";
            }

            if (string.IsNullOrWhiteSpace(rawSkill.Description) || rawSkill.Description.Length > 1024)
            {
                return $"Skipped skill '{rawSkill.Name}' because its description is missing or too long.";
            }

            if (!IsValidDigest(rawSkill.Digest))
            {
                return $"Skipped skill '{rawSkill.Name}' because its digest is invalid.";
            }

            artifactUri = ResolveArtifactUri(indexUri, rawSkill.Url);
            if (artifactUri == null)
            {
                return $"Skipped skill '{rawSkill.Name}' because its artifact URL is invalid.";
            }

            if (!IsTrustedHttpUri(artifactUri))
            {
                return $"Skipped skill '{rawSkill.Name}' because its artifact URL is not HTTPS or localhost.";
            }

            return null;
        }

        private static async Task<string> CacheFaviconAsync(Uri indexUri, bool forceRefresh, CancellationToken cancellationToken)
        {
            var faviconUri = new Uri(indexUri.GetLeftPart(UriPartial.Authority) + "/favicon.ico");
            var iconPath = MarketplaceStorageService.GetWellKnownDiscoveryIconPath(indexUri, ".ico");

            if (!forceRefresh && File.Exists(iconPath))
            {
                return iconPath;
            }

            try
            {
                var (success, bytes) = await DownloadBytesAsync(faviconUri, cancellationToken);
                if (!success || bytes == null)
                {
                    return null;
                }

                Directory.CreateDirectory(Path.GetDirectoryName(iconPath));
                File.WriteAllBytes(iconPath, bytes);
                return iconPath;
            }
            catch (OperationCanceledException)
            {
                throw;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WellKnownDiscoveryService.CacheFaviconAsync failed for '{faviconUri}': {ex}");
                _ = ex.LogAsync();
                return null;
            }
        }

        private static async Task<string> CacheLegacySkillAsync(
            string sourceCacheDirectory,
            RawDiscoverySkill rawSkill,
            Uri indexUri,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var skillDirectory = Path.Combine(sourceCacheDirectory, rawSkill.Name, "legacy");
            var skillPath = Path.Combine(skillDirectory, "SKILL.md");

            if (!forceRefresh && File.Exists(skillPath))
            {
                return skillPath;
            }

            Directory.CreateDirectory(skillDirectory);
            foreach (var file in rawSkill.Files)
            {
                var destinationPath = GetSafeLegacySkillPath(skillDirectory, file);
                var fileUri = ResolveArtifactUri(indexUri, rawSkill.Name + "/" + file);
                if (fileUri == null || !IsTrustedHttpUri(fileUri))
                {
                    throw new InvalidOperationException($"Legacy skill '{rawSkill.Name}' contains an invalid file URL for '{file}'.");
                }

                var (success, bytes) = await DownloadBytesAsync(fileUri, cancellationToken);
                if (!success || bytes == null)
                {
                    throw new InvalidOperationException($"Failed to download file '{file}' for skill '{rawSkill.Name}'.");
                }

                Directory.CreateDirectory(Path.GetDirectoryName(destinationPath));
                File.WriteAllBytes(destinationPath, bytes);
            }

            return skillPath;
        }

        private static string GetSafeLegacySkillPath(string skillDirectory, string relativePath)
        {
            if (string.IsNullOrWhiteSpace(relativePath))
            {
                throw new InvalidOperationException("Legacy skill contains an empty file path.");
            }

            var normalizedPath = relativePath.Replace('/', Path.DirectorySeparatorChar).Replace('\\', Path.DirectorySeparatorChar);
            if (Path.IsPathRooted(normalizedPath))
            {
                throw new InvalidOperationException($"Legacy skill contains an unsafe absolute path: {relativePath}");
            }

            foreach (var part in normalizedPath.Split(Path.DirectorySeparatorChar))
            {
                if (part == "..")
                {
                    throw new InvalidOperationException($"Legacy skill contains an unsafe path: {relativePath}");
                }
            }

            var root = Path.GetFullPath(skillDirectory.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar);
            var destinationPath = Path.GetFullPath(Path.Combine(skillDirectory, normalizedPath));
            if (!destinationPath.StartsWith(root, StringComparison.OrdinalIgnoreCase))
            {
                throw new InvalidOperationException($"Legacy skill path escapes the skill directory: {relativePath}");
            }

            return destinationPath;
        }

        private static async Task<string> CacheSkillMarkdownAsync(
            string sourceCacheDirectory,
            RawDiscoverySkill rawSkill,
            Uri artifactUri,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var digestHex = rawSkill.Digest.Substring("sha256:".Length);
            var skillDirectory = Path.Combine(sourceCacheDirectory, rawSkill.Name, digestHex);
            var skillPath = Path.Combine(skillDirectory, "SKILL.md");

            if (!forceRefresh && File.Exists(skillPath))
            {
                return skillPath;
            }

            Directory.CreateDirectory(skillDirectory);
            var artifactBytes = await DownloadAndVerifyArtifactAsync(rawSkill, artifactUri, cancellationToken);
            File.WriteAllBytes(skillPath, artifactBytes);
            return skillPath;
        }

        private static async Task<string> CacheSkillArchiveAsync(
            string sourceCacheDirectory,
            RawDiscoverySkill rawSkill,
            Uri artifactUri,
            bool forceRefresh,
            CancellationToken cancellationToken)
        {
            var digestHex = rawSkill.Digest.Substring("sha256:".Length);
            var skillDirectory = Path.Combine(sourceCacheDirectory, rawSkill.Name, digestHex);
            var skillPath = Path.Combine(skillDirectory, "SKILL.md");

            if (!forceRefresh && File.Exists(skillPath))
            {
                return skillPath;
            }

            var artifactBytes = await DownloadAndVerifyArtifactAsync(rawSkill, artifactUri, cancellationToken);
            return WellKnownArchiveService.ExtractArchive(artifactBytes, artifactUri, skillDirectory);
        }

        private static async Task<byte[]> DownloadAndVerifyArtifactAsync(RawDiscoverySkill rawSkill, Uri artifactUri, CancellationToken cancellationToken)
        {
            var (success, artifactBytes) = await DownloadBytesAsync(artifactUri, cancellationToken);
            if (!success || artifactBytes == null)
            {
                throw new InvalidOperationException($"Failed to download artifact for skill '{rawSkill.Name}' from {artifactUri}.");
            }

            var actualDigest = ComputeSha256Digest(artifactBytes);
            if (!string.Equals(actualDigest, rawSkill.Digest, StringComparison.Ordinal))
            {
                throw new InvalidOperationException($"Digest verification failed for skill '{rawSkill.Name}'. Expected {rawSkill.Digest}, got {actualDigest}.");
            }

            return artifactBytes;
        }

        private static async Task<(byte[] Bytes, Uri ResolvedIndexUri)> DownloadIndexBytesAsync(Uri indexUri, CancellationToken cancellationToken)
        {
            var (success, bytes) = await DownloadBytesAsync(indexUri, cancellationToken);
            if (success)
            {
                return (bytes, indexUri);
            }

            // If the provided URI is the newer format, try legacy format
            if (string.Equals(indexUri.AbsolutePath, WellKnownIndexPath, StringComparison.OrdinalIgnoreCase))
            {
                var legacyIndexUri = new Uri($"{indexUri.Scheme}://{indexUri.Authority}{LegacyWellKnownIndexPath}");
                var (legacySuccess, legacyBytes) = await DownloadBytesAsync(legacyIndexUri, cancellationToken);
                if (legacySuccess)
                {
                    return (legacyBytes, legacyIndexUri);
                }
            }

            // Return null bytes to indicate failure
            return (null, indexUri);
        }

        private static async Task<(bool success, byte[] bytes)> DownloadBytesAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(uri, HttpCompletionOption.ResponseContentRead, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    return (true, bytes);
                }
                else
                {
                    Debug.WriteLine($"WellKnownDiscoveryService.DownloadBytesAsync: HTTP {(int)response.StatusCode} for {uri}");
                    return (false, null);
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"WellKnownDiscoveryService.DownloadBytesAsync error for {uri}: {ex.Message}");
                return (false, null);
            }
        }

        private static string ComputeSha256Digest(byte[] bytes)
        {
            using var sha256 = SHA256.Create();
            var hash = sha256.ComputeHash(bytes);
            return "sha256:" + BitConverter.ToString(hash).Replace("-", string.Empty).ToLowerInvariant();
        }

        private static bool IsWellKnownDiscoveryUri(Uri uri)
        {
            return uri.AbsolutePath.IndexOf("/.well-known/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static Uri NormalizeIndexUri(Uri uri)
        {
            if (uri.AbsolutePath.EndsWith("/index.json", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            var basePath = uri.AbsolutePath.TrimEnd('/');
            return new Uri($"{uri.Scheme}://{uri.Authority}{basePath}/index.json");
        }

        private static bool IsHttpUri(Uri uri)
        {
            return uri != null &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        private static bool IsTrustedHttpUri(Uri uri)
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }

        private sealed class RawDiscoveryIndex
        {
            [JsonPropertyName("$schema")]
            public string Schema { get; set; }

            public List<RawDiscoverySkill> Skills { get; set; }
        }

        private sealed class RawDiscoverySkill
        {
            public string Name { get; set; }

            public string Type { get; set; }

            public string Description { get; set; }

            public string Url { get; set; }

            public string Digest { get; set; }

            public List<string> Files { get; set; }
        }
    }
}
