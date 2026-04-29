using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Net.Http;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading;
using System.Threading.Tasks;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Service for discovering MCP servers from well-known marketplace URLs.
    /// Supports both /mcp.json (legacy) and /mcp/server-card.json (newer) endpoints.
    /// </summary>
    internal static class McpServerDiscoveryService
    {
        public const string WellKnownMcpPath = "/.well-known/mcp/server-card.json";
        public const string LegacyWellKnownMcpPath = "/.well-known/mcp.json";

        private static readonly Regex _serverNameRegex = new Regex("^[a-z0-9](?:[a-z0-9-]{0,62}[a-z0-9])?$", RegexOptions.Compiled);
        private static readonly HttpClient _httpClient = new HttpClient();

        /// <summary>
        /// Tries to create a discovery URI from a marketplace URL.
        /// Converts a base marketplace URL to the MCP discovery endpoint.
        /// </summary>
        internal static bool TryCreateDiscoveryUri(string input, out Uri discoveryUri)
        {
            discoveryUri = null;

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

                // If it's already a .well-known MCP endpoint, use it as-is
                if (IsWellKnownMcpUri(absoluteUri))
                {
                    discoveryUri = NormalizeDiscoveryUri(absoluteUri);
                    return true;
                }

                // If it's just a domain or domain:port, generate the discovery URI
                if (string.IsNullOrEmpty(absoluteUri.AbsolutePath) || absoluteUri.AbsolutePath == "/")
                {
                    discoveryUri = new Uri($"{absoluteUri.Scheme}://{absoluteUri.Authority}{WellKnownMcpPath}");
                    return true;
                }

                return false;
            }

            // Handle simple domain input like "example.com"
            if (!trimmed.Contains("/") && trimmed.Contains("."))
            {
                discoveryUri = new Uri($"https://{trimmed.TrimEnd('/')}{WellKnownMcpPath}");
                return true;
            }

            return false;
        }

        /// <summary>
        /// Gets a source ID for the discovered servers.
        /// </summary>
        internal static string GetSourceId(Uri discoveryUri)
        {
            if (discoveryUri == null)
            {
                throw new ArgumentNullException(nameof(discoveryUri));
            }

            return $"mcp-servers:{discoveryUri.AbsoluteUri}";
        }

        /// <summary>
        /// Gets a display name for the discovery source.
        /// </summary>
        internal static string GetDisplayName(Uri discoveryUri)
        {
            if (discoveryUri == null)
            {
                return "MCP Servers";
            }

            return $"MCP Servers - {discoveryUri.Host}";
        }

        /// <summary>
        /// Validates that a server name follows naming conventions.
        /// </summary>
        internal static bool IsValidServerName(string name)
        {
            return !string.IsNullOrWhiteSpace(name)
                && _serverNameRegex.IsMatch(name)
                && !name.Contains("--");
        }

        /// <summary>
        /// Discovers MCP servers from a marketplace entry's well-known locations.
        /// </summary>
        public static async Task<McpServerDiscoveryResult> DiscoverAsync(
            Uri baseUri,
            string displayName = null,
            bool forceRefresh = false,
            CancellationToken cancellationToken = default)
        {
            if (baseUri == null)
            {
                throw new ArgumentNullException(nameof(baseUri));
            }

            if (!IsTrustedHttpUri(baseUri))
            {
                throw new InvalidOperationException("MCP server discovery sources must use HTTPS unless they are hosted on localhost.");
            }

            var result = new McpServerDiscoveryResult
            {
                Id = GetSourceId(baseUri),
                DiscoveryUri = baseUri,
                Origin = baseUri.GetLeftPart(UriPartial.Authority),
                DisplayName = displayName ?? GetDisplayName(baseUri)
            };

            try
            {
                // First, verify the host is accessible with a HEAD request
                var hostAccessible = await VerifyHostAccessibleAsync(baseUri, cancellationToken);
                if (!hostAccessible)
                {
                    result.Warnings.Add($"Host {baseUri.Authority} is not accessible. Please verify the URL is correct and the host is online.");
                    return result;
                }

                // Try to download from the specified URI first
                var (serverCardBytes, resolvedUri) = await DownloadServerCardAsync(baseUri, cancellationToken);

                if (serverCardBytes != null)
                {
                    result.DiscoveryUri = resolvedUri;
                    result.Id = GetSourceId(resolvedUri);
                    result.Origin = resolvedUri.GetLeftPart(UriPartial.Authority);
                    result.DisplayName = displayName ?? GetDisplayName(resolvedUri);

                    await ParseServerCard(serverCardBytes, baseUri, result, cancellationToken);
                }
                else
                {
                    // No valid endpoint found - add informative warning
                    result.Warnings.Add($"No MCP server definitions found at well-known locations. Tried /.well-known/mcp/server-card.json and /.well-known/mcp.json");
                }
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Failed to discover MCP servers: {ex.Message}");
                Debug.WriteLine($"McpServerDiscoveryService.DiscoverAsync failed: {ex}");
            }

            return result;
        }

        /// <summary>
        /// Parses the server-card.json file and extracts server definitions.
        /// </summary>
        private static async Task ParseServerCard(
            byte[] cardBytes,
            Uri baseUri,
            McpServerDiscoveryResult result,
            CancellationToken cancellationToken)
        {
            try
            {
                var options = new JsonSerializerOptions { PropertyNameCaseInsensitive = true };

                // First try to parse as a single server card (newer format used by schemastore.org and others)
                // The server-card.json spec defines a single server object at the root with name, description, url, etc.
                var singleServer = JsonSerializer.Deserialize<RawMcpServer>(cardBytes, options);
                List<RawMcpServer> servers = null;
                bool isSingleServerCard = false;

                if (singleServer != null && !string.IsNullOrWhiteSpace(singleServer.Name) && !string.IsNullOrWhiteSpace(singleServer.Url))
                {
                    servers = new List<RawMcpServer> { singleServer };
                    isSingleServerCard = true;
                }
                else
                {
                    // Fall back to the legacy format with a "servers" array
                    var card = JsonSerializer.Deserialize<RawMcpServerCard>(cardBytes, options);
                    if (card?.Servers != null && card.Servers.Count > 0)
                    {
                        servers = card.Servers;
                    }
                }

                if (servers == null || servers.Count == 0)
                {
                    result.Warnings.Add("Server card contains no servers.");
                    return;
                }

                foreach (var rawServer in servers)
                {
                    cancellationToken.ThrowIfCancellationRequested();

                    if (string.IsNullOrWhiteSpace(rawServer.Name))
                    {
                        result.Warnings.Add("Skipped server with missing name.");
                        continue;
                    }

                    // The strict RFC name validation only applies to identifiers in the legacy
                    // "servers" array format. Single server-card files use a free-form display name.
                    if (!isSingleServerCard && !IsValidServerName(rawServer.Name))
                    {
                        result.Warnings.Add($"Invalid server name: {rawServer.Name}");
                        continue;
                    }

                    if (string.IsNullOrWhiteSpace(rawServer.Url))
                    {
                        result.Warnings.Add($"Server '{rawServer.Name}' has no URL.");
                        continue;
                    }

                    var artifactUri = ResolveArtifactUri(baseUri, rawServer.Url);
                    if (artifactUri == null)
                    {
                        result.Warnings.Add($"Server '{rawServer.Name}' has an invalid URL.");
                        continue;
                    }

                    var server = new McpServerDefinition
                    {
                        Name = rawServer.Name,
                        Description = rawServer.Description,
                        Version = rawServer.Version,
                        ArtifactUri = artifactUri,
                        Digest = rawServer.Digest
                    };

                    result.Servers.Add(server);
                }
            }
            catch (JsonException ex)
            {
                result.Warnings.Add($"Failed to parse server card JSON: {ex.Message}");
                Debug.WriteLine($"McpServerDiscoveryService.ParseServerCard JSON error: {ex}");
            }
            catch (Exception ex)
            {
                result.Warnings.Add($"Error processing server card: {ex.Message}");
                Debug.WriteLine($"McpServerDiscoveryService.ParseServerCard error: {ex}");
            }
        }

        /// <summary>
        /// Verifies that the host is accessible by making a HEAD request to the root.
        /// </summary>
        private static async Task<bool> VerifyHostAccessibleAsync(Uri baseUri, CancellationToken cancellationToken)
        {
            try
            {
                // Create a URI to the root of the host
                var rootUri = new Uri($"{baseUri.Scheme}://{baseUri.Authority}/");
                using var request = new HttpRequestMessage(HttpMethod.Head, rootUri);
                using var response = await _httpClient.SendAsync(request, cancellationToken);

                // Accept any non-404 response; 401/403 means the host is alive but we don't have access
                // 5xx errors also indicate the host is accessible but having issues
                return response.StatusCode != System.Net.HttpStatusCode.NotFound;
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"McpServerDiscoveryService.VerifyHostAccessibleAsync failed for {baseUri.Authority}: {ex.Message}");
                return false;
            }
        }

        /// <summary>
        /// Downloads the server card from the discovery URI.
        /// Tries the newer server-card.json first, then falls back to legacy mcp.json.
        /// </summary>
        private static async Task<(byte[] bytes, Uri resolvedUri)> DownloadServerCardAsync(
            Uri baseUri,
            CancellationToken cancellationToken)
        {
            var attemptedUris = new List<string>();

            // Only download from the provided URI directly if it explicitly points at a JSON endpoint
            // (e.g. a well-known path). Otherwise we'd try to JSON-parse the host's HTML homepage.
            if (baseUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                attemptedUris.Add(baseUri.AbsoluteUri);
                var (success, bytes) = await TryDownloadAsync(baseUri, cancellationToken);
                if (success)
                {
                    return (bytes, baseUri);
                }
            }

            // If the provided URI is the newer format, try legacy format
            if (baseUri.AbsolutePath.EndsWith(WellKnownMcpPath, StringComparison.OrdinalIgnoreCase))
            {
                var legacyUri = new Uri($"{baseUri.Scheme}://{baseUri.Authority}{LegacyWellKnownMcpPath}");
                attemptedUris.Add(legacyUri.AbsoluteUri);
                var (legacySuccess, legacyBytes) = await TryDownloadAsync(legacyUri, cancellationToken);
                if (legacySuccess)
                {
                    return (legacyBytes, legacyUri);
                }
            }

            // If the provided URI is the legacy format, try newer format
            if (baseUri.AbsolutePath.EndsWith(LegacyWellKnownMcpPath, StringComparison.OrdinalIgnoreCase))
            {
                var newUri = new Uri($"{baseUri.Scheme}://{baseUri.Authority}{WellKnownMcpPath}");
                attemptedUris.Add(newUri.AbsoluteUri);
                var (newSuccess, newBytes) = await TryDownloadAsync(newUri, cancellationToken);
                if (newSuccess)
                {
                    return (newBytes, newUri);
                }
            }

            // If no explicit well-known path was provided, try both
            if (!baseUri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                var newUri = new Uri($"{baseUri.Scheme}://{baseUri.Authority}{WellKnownMcpPath}");
                if (!attemptedUris.Contains(newUri.AbsoluteUri))
                {
                    attemptedUris.Add(newUri.AbsoluteUri);
                    var (newSuccess, newBytes) = await TryDownloadAsync(newUri, cancellationToken);
                    if (newSuccess)
                    {
                        return (newBytes, newUri);
                    }
                }

                var legacyUri = new Uri($"{baseUri.Scheme}://{baseUri.Authority}{LegacyWellKnownMcpPath}");
                if (!attemptedUris.Contains(legacyUri.AbsoluteUri))
                {
                    attemptedUris.Add(legacyUri.AbsoluteUri);
                    var (legacySuccess, legacyBytes) = await TryDownloadAsync(legacyUri, cancellationToken);
                    if (legacySuccess)
                    {
                        return (legacyBytes, legacyUri);
                    }
                }
            }

            // Log which URLs we tried for debugging
            Debug.WriteLine($"McpServerDiscoveryService.DownloadServerCardAsync: No MCP server definitions found. Attempted: {string.Join(", ", attemptedUris)}");

            return (null, baseUri);
        }

        /// <summary>
        /// Attempts to download content from a URI with detailed error logging.
        /// </summary>
        private static async Task<(bool success, byte[] bytes)> TryDownloadAsync(Uri uri, CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync(uri, cancellationToken);
                if (response.IsSuccessStatusCode)
                {
                    var bytes = await response.Content.ReadAsByteArrayAsync();
                    return (true, bytes);
                }
                else
                {
                    Debug.WriteLine($"McpServerDiscoveryService.TryDownloadAsync: HTTP {(int)response.StatusCode} for {uri}");
                }
            }
            catch (Exception ex)
            {
                Debug.WriteLine($"McpServerDiscoveryService.TryDownloadAsync error for {uri}: {ex.Message}");
            }

            return (false, null);
        }

        /// <summary>
        /// Resolves a relative URL against the base discovery URI.
        /// </summary>
        internal static Uri ResolveArtifactUri(Uri baseUri, string url)
        {
            if (baseUri == null || string.IsNullOrWhiteSpace(url))
            {
                return null;
            }

            return Uri.TryCreate(baseUri, url, out var resolvedUri) && IsHttpUri(resolvedUri)
                ? resolvedUri
                : null;
        }

        /// <summary>
        /// Checks if a URI is an HTTP or HTTPS URI.
        /// </summary>
        private static bool IsHttpUri(Uri uri)
        {
            return uri != null &&
                (string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                 string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase));
        }

        /// <summary>
        /// Checks if a URI is a well-known MCP endpoint.
        /// </summary>
        private static bool IsWellKnownMcpUri(Uri uri)
        {
            return uri.AbsolutePath.IndexOf("/.well-known/mcp", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        /// <summary>
        /// Normalizes a discovery URI (ensures consistent format).
        /// </summary>
        private static Uri NormalizeDiscoveryUri(Uri uri)
        {
            if (uri.AbsolutePath.EndsWith(".json", StringComparison.OrdinalIgnoreCase))
            {
                return uri;
            }

            var basePath = uri.AbsolutePath.TrimEnd('/');
            return new Uri($"{uri.Scheme}://{uri.Authority}{basePath}{WellKnownMcpPath}");
        }

        /// <summary>
        /// Checks if the URI is HTTPS or localhost.
        /// </summary>
        private static bool IsTrustedHttpUri(Uri uri)
        {
            return string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "localhost", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "127.0.0.1", StringComparison.OrdinalIgnoreCase)
                || string.Equals(uri.Host, "::1", StringComparison.OrdinalIgnoreCase);
        }
    }
}
