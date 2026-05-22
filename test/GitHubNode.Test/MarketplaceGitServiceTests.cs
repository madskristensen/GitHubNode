using System.Diagnostics;
using System.Text;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class MarketplaceGitServiceTests
{
    [TestMethod]
    public async Task CloneLinkedRepositoryAsync_RespectsRefForBranchAndTag()
    {
        var originRepoPath = CreateTempDirectory();
        var linkedBranchPath = string.Empty;
        var linkedTagPath = string.Empty;

        try
        {
            var (initialSha, featureSha) = CreateRepositoryWithFeatureBranchAndTag(originRepoPath);
            var parentOwner = "parent-" + Guid.NewGuid().ToString("N");
            var parentRepo = "marketplace";

            var (branchResult, branchLocalPath) = await MarketplaceGitService.CloneLinkedRepositoryAsync(
                parentOwner,
                parentRepo,
                linkedOwner: "linked",
                linkedRepo: "branch-repo",
                @ref: "feature",
                linkedRepositoryUrl: originRepoPath);

            linkedBranchPath = branchLocalPath;
            Assert.IsTrue(branchResult.Success, branchResult.Error);
            var checkedOutFeatureSha = RunGit(branchLocalPath, "rev-parse HEAD").Trim();
            Assert.AreEqual(featureSha, checkedOutFeatureSha, "Expected linked clone to checkout the requested branch ref.");

            var (tagResult, tagLocalPath) = await MarketplaceGitService.CloneLinkedRepositoryAsync(
                parentOwner,
                parentRepo,
                linkedOwner: "linked",
                linkedRepo: "tag-repo",
                @ref: "v1",
                linkedRepositoryUrl: originRepoPath);

            linkedTagPath = tagLocalPath;
            Assert.IsTrue(tagResult.Success, tagResult.Error);
            var checkedOutTagSha = RunGit(tagLocalPath, "rev-parse HEAD").Trim();
            Assert.AreEqual(initialSha, checkedOutTagSha, "Expected linked clone to checkout the requested tag ref.");
        }
        finally
        {
            DeleteDirectory(linkedBranchPath);
            DeleteDirectory(linkedTagPath);
            DeleteDirectory(originRepoPath);
        }
    }

    [TestMethod]
    public async Task CloneLinkedRepositoryAsync_PrioritizesShaOverRef()
    {
        var originRepoPath = CreateTempDirectory();
        var linkedPath = string.Empty;

        try
        {
            var (initialSha, _) = CreateRepositoryWithFeatureBranchAndTag(originRepoPath);

            var (result, localPath) = await MarketplaceGitService.CloneLinkedRepositoryAsync(
                parentMarketplaceOwner: "parent-" + Guid.NewGuid().ToString("N"),
                parentMarketplaceRepo: "marketplace",
                linkedOwner: "linked",
                linkedRepo: "sha-repo",
                @ref: "feature",
                sha: initialSha,
                linkedRepositoryUrl: originRepoPath);

            linkedPath = localPath;
            Assert.IsTrue(result.Success, result.Error);
            var checkedOutSha = RunGit(localPath, "rev-parse HEAD").Trim();
            Assert.AreEqual(initialSha, checkedOutSha, "Expected linked clone to checkout the requested sha when both sha and ref are provided.");
        }
        finally
        {
            DeleteDirectory(linkedPath);
            DeleteDirectory(originRepoPath);
        }
    }

    [TestMethod]
    public async Task CloneLinkedRepositoryAsync_TreatsCommitHashRefAsSha()
    {
        var originRepoPath = CreateTempDirectory();
        var linkedPath = string.Empty;

        try
        {
            var (initialSha, _) = CreateRepositoryWithFeatureBranchAndTag(originRepoPath);

            var (result, localPath) = await MarketplaceGitService.CloneLinkedRepositoryAsync(
                parentMarketplaceOwner: "parent-" + Guid.NewGuid().ToString("N"),
                parentMarketplaceRepo: "marketplace",
                linkedOwner: "linked",
                linkedRepo: "ref-as-sha-repo",
                @ref: initialSha,
                linkedRepositoryUrl: originRepoPath);

            linkedPath = localPath;
            Assert.IsTrue(result.Success, result.Error);
            var checkedOutSha = RunGit(localPath, "rev-parse HEAD").Trim();
            Assert.AreEqual(initialSha, checkedOutSha, "Expected sha-looking ref values to be treated as commit checkouts.");
        }
        finally
        {
            DeleteDirectory(linkedPath);
            DeleteDirectory(originRepoPath);
        }
    }

    private static (string InitialSha, string FeatureSha) CreateRepositoryWithFeatureBranchAndTag(string repoPath)
    {
        Directory.CreateDirectory(repoPath);
        RunGit(repoPath, "init");
        RunGit(repoPath, "config user.email test@example.com");
        RunGit(repoPath, "config user.name Test User");
        RunGit(repoPath, "branch -M main");

        var trackedFile = Path.Combine(repoPath, "README.md");
        File.WriteAllText(trackedFile, "initial");
        RunGit(repoPath, "add README.md");
        RunGit(repoPath, "commit -m \"initial\"");

        var initialSha = RunGit(repoPath, "rev-parse HEAD").Trim();
        RunGit(repoPath, $"tag v1 {initialSha}");

        RunGit(repoPath, "checkout -b feature");
        File.WriteAllText(trackedFile, "feature");
        RunGit(repoPath, "add README.md");
        RunGit(repoPath, "commit -m \"feature\"");
        var featureSha = RunGit(repoPath, "rev-parse HEAD").Trim();

        RunGit(repoPath, "checkout main");

        return (initialSha, featureSha);
    }

    private static string RunGit(string workingDirectory, string arguments)
    {
        var startInfo = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDirectory,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true,
            StandardOutputEncoding = Encoding.UTF8,
            StandardErrorEncoding = Encoding.UTF8
        };

        using var process = Process.Start(startInfo);
        Assert.IsNotNull(process, $"Failed to start git process: git {arguments}");

        var stdOut = process.StandardOutput.ReadToEnd();
        var stdErr = process.StandardError.ReadToEnd();
        process.WaitForExit();

        if (process.ExitCode != 0)
        {
            Assert.Fail($"Git command failed ({process.ExitCode}): git {arguments}{Environment.NewLine}STDOUT:{Environment.NewLine}{stdOut}{Environment.NewLine}STDERR:{Environment.NewLine}{stdErr}");
        }

        return stdOut;
    }

    private static string CreateTempDirectory()
    {
        var path = Path.Combine(Path.GetTempPath(), "GitHubNode.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(path);
        return path;
    }

    private static void DeleteDirectory(string path)
    {
        if (string.IsNullOrWhiteSpace(path) || !Directory.Exists(path))
        {
            return;
        }

        foreach (var file in Directory.EnumerateFiles(path, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(path, recursive: true);
    }
}
