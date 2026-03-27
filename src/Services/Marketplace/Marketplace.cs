using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;

namespace GitHubNode.Services.Marketplace
{
    /// <summary>
    /// Represents owner information in a MarketplaceInfo.json file.
    /// </summary>
    internal sealed record MarketplaceOwner
    {
        /// <summary>
        /// Gets the owner name.
        /// </summary>
        public string Name { get; init; }

        /// <summary>
        /// Gets the owner email.
        /// </summary>
        public string Email { get; init; }
    }

    /// <summary>
    /// Represents metadata in a MarketplaceInfo.json file.
    /// </summary>
    internal sealed record MarketplaceMetadata
    {
        /// <summary>
        /// Gets the MarketplaceInfo description.
        /// </summary>
        public string Description { get; init; }

        /// <summary>
        /// Gets the MarketplaceInfo version.
        /// </summary>
        public string Version { get; init; }
    }

    /// <summary>
    /// Represents a MarketplaceInfo - a repository containing plugins for Copilot.
    /// Marketplaces are defined by a MarketplaceInfo.json file at .github/plugin/MarketplaceInfo.json.
    /// </summary>
    internal sealed class MarketplaceInfo
    {
        /// <summary>
        /// Gets or sets the unique identifier for this MarketplaceInfo (owner/repo format).
        /// </summary>
        public string Id { get; set; }

        /// <summary>
        /// Gets or sets the GitHub owner (user or organization).
        /// </summary>
        public string Owner { get; set; }

        /// <summary>
        /// Gets or sets the repository name.
        /// </summary>
        public string RepoName { get; set; }

        /// <summary>
        /// Gets or sets the branch to use.
        /// </summary>
        public string Branch { get; set; } = "main";

        /// <summary>
        /// Gets or sets the repository URL used to browse and clone this marketplace.
        /// </summary>
        public string RepositoryUrl { get; set; }

        /// <summary>
        /// Gets or sets the display name from MarketplaceInfo.json.
        /// </summary>
        public string DisplayName { get; set; }

        /// <summary>
        /// Gets or sets the description from MarketplaceInfo.json metadata.
        /// </summary>
        public string Description { get; set; }

        /// <summary>
        /// Gets or sets the owner information from MarketplaceInfo.json.
        /// </summary>
        public MarketplaceOwner OwnerInfo { get; set; }

        /// <summary>
        /// Gets or sets the metadata from MarketplaceInfo.json.
        /// </summary>
        public MarketplaceMetadata Metadata { get; set; }

        /// <summary>
        /// Gets or sets whether this is a built-in MarketplaceInfo.
        /// Built-in marketplaces cannot be removed by the user.
        /// </summary>
        public bool IsBuiltIn { get; set; }

        /// <summary>
        /// Gets or sets the local path where the MarketplaceInfo is cloned.
        /// </summary>
        public string LocalPath { get; set; }

        /// <summary>
        /// Gets or sets whether the MarketplaceInfo has been cloned successfully.
        /// </summary>
        public bool IsCloned { get; set; }

        /// <summary>
        /// Gets or sets the last time the MarketplaceInfo was updated (git pull).
        /// </summary>
        public System.DateTime? LastUpdated { get; set; }

        /// <summary>
        /// Gets or sets any error message from the last operation.
        /// </summary>
        public string ErrorMessage { get; set; }

        /// <summary>
        /// Gets or sets the path to the locally cached icon file.
        /// </summary>
        public string IconPath { get; set; }

        /// <summary>
        /// Gets or sets the plugins defined in this MarketplaceInfo.
        /// </summary>
        public List<MarketplacePlugin> Plugins { get; set; } = new List<MarketplacePlugin>();

        /// <summary>
        /// Gets the GitHub URL for this MarketplaceInfo.
        /// </summary>
        public string GitHubUrl => MarketplaceRepositoryUrl.GetRepositoryUrl(Owner, RepoName, RepositoryUrl);

        /// <summary>
        /// Gets the clone URL for this MarketplaceInfo.
        /// </summary>
        public string CloneUrl => MarketplaceRepositoryUrl.GetCloneUrl(Owner, RepoName, RepositoryUrl);

        /// <summary>
        /// Gets all assets of a specific type across all plugins.
        /// </summary>
        public IEnumerable<PluginAsset> GetAllAssets(AssetType type)
        {
            foreach (var plugin in Plugins)
            {
                foreach (var asset in plugin.GetAssets(type))
                {
                    yield return asset;
                }
            }
        }

        /// <summary>
        /// Returns true if any plugin contains assets of the specified type.
        /// </summary>
        public bool HasAssetType(AssetType type)
        {
            foreach (var plugin in Plugins)
            {
                if (plugin.HasAssetType(type))
                {
                    return true;
                }
            }

            return false;
        }
    }

    internal static class MarketplaceRepositoryUrl
    {
        private const string DefaultGitHubHost = "github.com";
        private static readonly Regex _scpStyleUrlRegex = new Regex(@"^(?<user>[^@]+)@(?<host>[^:]+):(?<path>.+)$", RegexOptions.Compiled);

        public static string GetRepositoryUrl(string owner, string repo, string repositoryUrl = null)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return $"https://{DefaultGitHubHost}/{owner}/{repo}";
            }

            if (!TryGetRepositoryLocation(repositoryUrl, out var authority, out var path, out var scheme))
            {
                return null;
            }

            path = TrimRepositoryPath(path);
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            if (IsHttpScheme(scheme))
            {
                return $"{scheme}://{authority}/{path}";
            }

            return $"https://{authority}/{path}";
        }

        public static string GetCloneUrl(string owner, string repo, string repositoryUrl = null)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return $"https://{DefaultGitHubHost}/{owner}/{repo}.git";
            }

            var normalizedUrl = repositoryUrl.Trim().TrimEnd('/');
            if (!TryGetRepositoryLocation(normalizedUrl, out _, out _, out var scheme) || !IsHttpScheme(scheme))
            {
                return normalizedUrl;
            }

            if (normalizedUrl.EndsWith(".git", StringComparison.OrdinalIgnoreCase) ||
                IsAzureDevOpsUrl(normalizedUrl))
            {
                return normalizedUrl;
            }

            return $"{normalizedUrl}.git";
        }

        public static string GetHost(string repositoryUrl)
        {
            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return null;
            }

            if (!TryGetRepositoryLocation(repositoryUrl, out var authority, out _, out _))
            {
                return null;
            }

            return string.Equals(authority, DefaultGitHubHost, StringComparison.OrdinalIgnoreCase)
                ? null
                : authority;
        }

        public static bool TryParseInput(string input, out string owner, out string repo, out string branch, out string repositoryUrl)
        {
            owner = null;
            repo = null;
            branch = null;
            repositoryUrl = null;

            if (string.IsNullOrWhiteSpace(input))
            {
                return false;
            }

            var trimmedInput = input.Trim();

            if (Uri.TryCreate(trimmedInput, UriKind.Absolute, out var absoluteUri) &&
                !string.Equals(absoluteUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return TryParseAbsoluteUrl(trimmedInput, absoluteUri, out owner, out repo, out branch, out repositoryUrl);
            }

            if (TryParseScpStyleUrl(trimmedInput, out owner, out repo, out repositoryUrl))
            {
                return true;
            }

            return TryParseOwnerRepoShorthand(trimmedInput, out owner, out repo, out branch);
        }

        private static bool TryParseAbsoluteUrl(string input, Uri absoluteUri, out string owner, out string repo, out string branch, out string repositoryUrl)
        {
            owner = null;
            repo = null;
            branch = null;
            repositoryUrl = null;

            var parts = absoluteUri.AbsolutePath.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            var repoIndex = parts.Length - 1;
            var ownerIndex = parts.Length - 2;

            if (IsHttpScheme(absoluteUri.Scheme) &&
                parts.Length >= 4 &&
                string.Equals(parts[parts.Length - 2], "tree", StringComparison.OrdinalIgnoreCase))
            {
                branch = parts[parts.Length - 1];
                repoIndex = parts.Length - 3;
                ownerIndex = parts.Length - 4;
            }

            if (ownerIndex < 0 || repoIndex < 0)
            {
                return false;
            }

            owner = parts[ownerIndex];
            repo = TrimGitSuffix(parts[repoIndex]);

            if (string.IsNullOrWhiteSpace(owner) || string.IsNullOrWhiteSpace(repo))
            {
                return false;
            }

            if (IsHttpScheme(absoluteUri.Scheme) && !string.IsNullOrEmpty(branch))
            {
                var baseSegments = new string[repoIndex + 1];
                Array.Copy(parts, baseSegments, repoIndex + 1);
                baseSegments[repoIndex] = repo;
                repositoryUrl = $"{absoluteUri.Scheme}://{absoluteUri.Authority}/{string.Join("/", baseSegments)}";
            }
            else
            {
                repositoryUrl = input.Trim().TrimEnd('/');
            }

            return true;
        }

        private static bool TryParseScpStyleUrl(string input, out string owner, out string repo, out string repositoryUrl)
        {
            owner = null;
            repo = null;
            repositoryUrl = null;

            var match = _scpStyleUrlRegex.Match(input);
            if (!match.Success)
            {
                return false;
            }

            var path = match.Groups["path"].Value;
            if (!TryExtractOwnerAndRepo(path, out owner, out repo))
            {
                return false;
            }

            repositoryUrl = input.Trim().TrimEnd('/');
            return true;
        }

        private static bool TryParseOwnerRepoShorthand(string input, out string owner, out string repo, out string branch)
        {
            owner = null;
            repo = null;
            branch = null;

            var parts = input.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            owner = parts[0];
            repo = parts[1];
            if (parts.Length >= 4 && string.Equals(parts[2], "tree", StringComparison.OrdinalIgnoreCase))
            {
                branch = parts[3];
            }

            return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
        }

        private static bool TryGetRepositoryLocation(string repositoryUrl, out string authority, out string path, out string scheme)
        {
            authority = null;
            path = null;
            scheme = null;

            if (string.IsNullOrWhiteSpace(repositoryUrl))
            {
                return false;
            }

            var normalizedUrl = repositoryUrl.Trim().TrimEnd('/');
            var scpMatch = _scpStyleUrlRegex.Match(normalizedUrl);
            if (scpMatch.Success)
            {
                authority = scpMatch.Groups["host"].Value;
                path = scpMatch.Groups["path"].Value;
                return true;
            }

            if (!Uri.TryCreate(normalizedUrl, UriKind.Absolute, out var repositoryUri) ||
                string.Equals(repositoryUri.Scheme, Uri.UriSchemeFile, StringComparison.OrdinalIgnoreCase))
            {
                return false;
            }

            authority = repositoryUri.Authority;
            path = repositoryUri.AbsolutePath;
            scheme = repositoryUri.Scheme;
            return !string.IsNullOrWhiteSpace(authority) && !string.IsNullOrWhiteSpace(path);
        }

        private static bool TryExtractOwnerAndRepo(string path, out string owner, out string repo)
        {
            owner = null;
            repo = null;

            if (string.IsNullOrWhiteSpace(path))
            {
                return false;
            }

            var parts = path.Split(new[] { '/' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2)
            {
                return false;
            }

            owner = parts[parts.Length - 2];
            repo = TrimGitSuffix(parts[parts.Length - 1]);
            return !string.IsNullOrWhiteSpace(owner) && !string.IsNullOrWhiteSpace(repo);
        }

        private static bool IsHttpScheme(string scheme)
        {
            return string.Equals(scheme, Uri.UriSchemeHttp, StringComparison.OrdinalIgnoreCase) ||
                   string.Equals(scheme, Uri.UriSchemeHttps, StringComparison.OrdinalIgnoreCase);
        }

        /// <summary>
        /// Determines whether the URL is an Azure DevOps Git URL.
        /// Azure DevOps clone URLs use the /_git/ path segment and do not require a .git suffix.
        /// </summary>
        private static bool IsAzureDevOpsUrl(string url)
        {
            return url.IndexOf("/_git/", StringComparison.OrdinalIgnoreCase) >= 0;
        }

        private static string TrimRepositoryPath(string path)
        {
            if (string.IsNullOrWhiteSpace(path))
            {
                return null;
            }

            return TrimGitSuffix(path.Trim().Trim('/'));
        }

        private static string TrimGitSuffix(string value)
        {
            if (string.IsNullOrWhiteSpace(value))
            {
                return value;
            }

            return value.EndsWith(".git", StringComparison.OrdinalIgnoreCase)
                ? value.Substring(0, value.Length - 4)
                : value;
        }
    }
}
