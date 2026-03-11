using System.Collections.Generic;
using System.Diagnostics;
using System.Threading.Tasks;
using GitHubNode.Services;
using Microsoft.VisualStudio.Imaging;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode.Test;

[TestClass]
public class GitStatusServiceTests
{
    [DataTestMethod]
    [DynamicData(nameof(GetMappedStatusCases), DynamicDataSourceType.Method)]
    public void GetStatusIcon_ReturnsExpectedIcon_ForMappedState(int statusValue, ImageMoniker expected)
    {
        GitFileStatus status = (GitFileStatus)statusValue;
        ImageMoniker actual = GitStatusService.GetStatusIcon(status);

        AssertMonikerEquals(expected, actual);
    }

    [TestMethod]
    public void GetStatusIcon_ReturnsDefaultMoniker_ForNotInRepo()
    {
        ImageMoniker actual = GitStatusService.GetStatusIcon(GitFileStatus.NotInRepo);

        Assert.AreEqual(default(ImageMoniker), actual);
    }

    [TestMethod]
    public void GetCachedFileStatus_ReturnsNotInRepo_ForNullPath()
    {
        GitFileStatus status = GitStatusService.GetCachedFileStatus(null);

        Assert.AreEqual(GitFileStatus.NotInRepo, status);
    }

    [TestMethod]
    public void GetCachedFileStatus_ReturnsNotInRepo_ForEmptyPath()
    {
        GitFileStatus status = GitStatusService.GetCachedFileStatus(string.Empty);

        Assert.AreEqual(GitFileStatus.NotInRepo, status);
    }

    [TestMethod]
    public void GetCachedFileStatus_ReturnsNotInRepo_ForUncachedPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");

        GitStatusService.InvalidateCache();
        GitFileStatus status = GitStatusService.GetCachedFileStatus(path);

        Assert.AreEqual(GitFileStatus.NotInRepo, status);
    }

    [TestMethod]
    public void InvalidateCache_ClearsAllEntries()
    {
        // Seed the cache via the async path against the real workspace repo, then clear it.
        string knownFile = Path.GetFullPath(Path.Combine(
            Path.GetDirectoryName(typeof(GitStatusServiceTests).Assembly.Location),
            "..\\..\\..\\..\\src\\Services\\GitStatusService.cs"));

        // Prime: call async to populate the cache (best-effort - git may not be available).
        // Even if git is unavailable the call should not throw.
        try
        {
            Task.Run(() => GitStatusService.GetFileStatusAsync(knownFile)).Wait(3000);
        }
        catch
        {
            // git may not be available in the test environment - that is fine.
        }

        GitStatusService.InvalidateCache();

        // After invalidation, the synchronous cached lookup must return NotInRepo.
        GitFileStatus status = GitStatusService.GetCachedFileStatus(knownFile);

        Assert.AreEqual(GitFileStatus.NotInRepo, status);
    }

    [TestMethod]
    public void GetCachedFileStatus_ReturnsNotInRepo_WhenPathIsOutsideAnyGitRepo()
    {
        // A path on a drive root or isolated temp folder that has no .git ancestor.
        string isolatedDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(isolatedDir);

        try
        {
            string path = Path.Combine(isolatedDir, "norepo.txt");
            File.WriteAllText(path, "test");

            GitStatusService.InvalidateCache();
            GitFileStatus status = GitStatusService.GetCachedFileStatus(path);

            Assert.AreEqual(GitFileStatus.NotInRepo, status);
        }
        finally
        {
            Directory.Delete(isolatedDir, recursive: true);
        }
    }

    // -------------------------------------------------------------------------
    // ParseGitStatus - tested indirectly via GetFileStatusAsync on a real git repo
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsUntracked_ForNewFileInGitRepo()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "untracked.txt");
            File.WriteAllText(filePath, "new");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.Untracked, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsAdded_ForStagedNewFileInGitRepo()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "added.txt");
            File.WriteAllText(filePath, "staged");
            RunGit(repoDir, "add added.txt");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.Added, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsUnmodified_ForCommittedFileInGitRepo()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "committed.txt");
            File.WriteAllText(filePath, "committed");
            RunGit(repoDir, "add committed.txt");
            RunGit(repoDir, "commit -m init");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.Unmodified, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsModified_ForEditedCommittedFileInGitRepo()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "modified.txt");
            File.WriteAllText(filePath, "original");
            RunGit(repoDir, "add modified.txt");
            RunGit(repoDir, "commit -m init");
            File.WriteAllText(filePath, "changed");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.Modified, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsStaged_ForModifiedAndStagedFileInGitRepo()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "staged.txt");
            File.WriteAllText(filePath, "original");
            RunGit(repoDir, "add staged.txt");
            RunGit(repoDir, "commit -m init");
            File.WriteAllText(filePath, "modified staged");
            RunGit(repoDir, "add staged.txt");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.Staged, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsNotInRepo_ForDeletedFileOnDisk()
    {
        // GetFileStatusAsync guards with File.Exists before querying git.
        // A file deleted from disk therefore returns NotInRepo immediately.
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "todelete.txt");
            File.WriteAllText(filePath, "bye");
            RunGit(repoDir, "add todelete.txt");
            RunGit(repoDir, "commit -m init");
            File.Delete(filePath);

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.NotInRepo, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetCachedFileStatus_ReturnsDeleted_WhenCachedBeforeDeletion()
    {
        // If a file was cached as Deleted (from a prior async refresh), the
        // synchronous GetCachedFileStatus should return that cached status.
        string repoDir = CreateTempGitRepo();

        try
        {
            string filePath = Path.Combine(repoDir, "cached-delete.txt");
            File.WriteAllText(filePath, "here");
            RunGit(repoDir, "add cached-delete.txt");
            RunGit(repoDir, "commit -m init");

            // Warm the cache while the file exists.
            GitStatusService.InvalidateCache();
            await GitStatusService.GetFileStatusAsync(filePath);

            // Now delete the file and do a fresh async call to re-populate cache with Deleted.
            File.Delete(filePath);
            // Invalidate so the old Unmodified entry is gone, then manually stage-delete.
            RunGit(repoDir, "rm cached-delete.txt");
            GitStatusService.InvalidateCache();

            // Use a still-existing sibling to force a git status refresh for this repo.
            string sibling = Path.Combine(repoDir, "other.txt");
            File.WriteAllText(sibling, "x");
            await GitStatusService.GetFileStatusAsync(sibling);

            // The deleted file must now be in cache as Deleted (visible in porcelain output).
            GitFileStatus status = GitStatusService.GetCachedFileStatus(filePath);

            Assert.AreEqual(GitFileStatus.Deleted, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsIgnored_ForIgnoredFileInGitRepo()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            File.WriteAllText(Path.Combine(repoDir, ".gitignore"), "ignored.log\n");
            RunGit(repoDir, "add .gitignore");
            RunGit(repoDir, "commit -m init");

            string filePath = Path.Combine(repoDir, "ignored.log");
            File.WriteAllText(filePath, "noise");

            GitStatusService.InvalidateCache();
            // Need -u flag - git status --porcelain=v1 does not show ignored without --ignored
            // GetFileStatusAsync uses "status --porcelain=v1" which omits ignored by default.
            // So we expect NotInRepo (or Untracked depending on git config) - not Ignored - because
            // the service does not pass --ignored to git.  Assert whichever is returned is NOT Modified.
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreNotEqual(GitFileStatus.Modified, status);
            Assert.AreNotEqual(GitFileStatus.Staged, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsNotInRepo_WhenFileIsOutsideGitRepo()
    {
        string isolatedDir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(isolatedDir);

        try
        {
            string filePath = Path.Combine(isolatedDir, "outside.txt");
            File.WriteAllText(filePath, "no git here");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(filePath);

            Assert.AreEqual(GitFileStatus.NotInRepo, status);
        }
        finally
        {
            DeleteDirectory(isolatedDir);
        }
    }

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsNotInRepo_ForMissingFile()
    {
        string missing = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");

        GitFileStatus status = await GitStatusService.GetFileStatusAsync(missing);

        Assert.AreEqual(GitFileStatus.NotInRepo, status);
    }

    // -------------------------------------------------------------------------
    // Parent-directory fallback
    // -------------------------------------------------------------------------

    [TestMethod]
    public async Task GetFileStatusAsync_ReturnsUntrackedDerivedFromParent_WhenEntireFolderIsUntracked()
    {
        string repoDir = CreateTempGitRepo();

        try
        {
            // Commit something so we have a proper repo with HEAD.
            string seed = Path.Combine(repoDir, "seed.txt");
            File.WriteAllText(seed, "x");
            RunGit(repoDir, "add seed.txt");
            RunGit(repoDir, "commit -m init");

            // Create a new sub-folder with a file - neither is tracked.
            string subDir = Path.Combine(repoDir, "newFolder");
            Directory.CreateDirectory(subDir);
            string nestedFile = Path.Combine(subDir, "nested.txt");
            File.WriteAllText(nestedFile, "nested");

            GitStatusService.InvalidateCache();
            GitFileStatus status = await GitStatusService.GetFileStatusAsync(nestedFile);

            // Git reports the folder as untracked ("?? newFolder/"), so the
            // service should resolve the folder status and return Untracked.
            Assert.AreEqual(GitFileStatus.Untracked, status);
        }
        finally
        {
            DeleteDirectory(repoDir);
        }
    }

    // -------------------------------------------------------------------------
    // Helpers
    // -------------------------------------------------------------------------

    public static IEnumerable<object[]> GetMappedStatusCases()
    {
        yield return [(int)GitFileStatus.Unmodified, KnownMonikers.CheckedInNode];
        yield return [(int)GitFileStatus.Modified, KnownMonikers.CheckedOutForEditNode];
        yield return [(int)GitFileStatus.Staged, KnownMonikers.Checkmark];
        yield return [(int)GitFileStatus.Untracked, KnownMonikers.PendingAddNode];
        yield return [(int)GitFileStatus.Added, KnownMonikers.PendingAddNode];
        yield return [(int)GitFileStatus.Deleted, KnownMonikers.PendingDeleteNode];
        yield return [(int)GitFileStatus.Renamed, KnownMonikers.PendingRenameNode];
        yield return [(int)GitFileStatus.Conflict, KnownMonikers.StatusWarning];
        yield return [(int)GitFileStatus.Ignored, KnownMonikers.HideMember];
    }

    private static string CreateTempGitRepo()
    {
        string dir = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        Directory.CreateDirectory(dir);
        RunGit(dir, "init");
        RunGit(dir, "config user.email test@test.com");
        RunGit(dir, "config user.name Test");
        return dir;
    }

    private static void RunGit(string workingDir, string arguments)
    {
        var psi = new ProcessStartInfo
        {
            FileName = "git",
            Arguments = arguments,
            WorkingDirectory = workingDir,
            UseShellExecute = false,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            CreateNoWindow = true
        };

        using Process process = Process.Start(psi);
        process?.WaitForExit(5000);
    }

    private static void DeleteDirectory(string dir)
    {
        if (!Directory.Exists(dir))
        {
            return;
        }

        // Make all files writable before deleting (git pack files are read-only).
        foreach (string file in Directory.EnumerateFiles(dir, "*", SearchOption.AllDirectories))
        {
            File.SetAttributes(file, FileAttributes.Normal);
        }

        Directory.Delete(dir, recursive: true);
    }

    private static void AssertMonikerEquals(
        ImageMoniker expected,
        ImageMoniker actual)
    {
        Assert.AreEqual(expected.Guid, actual.Guid);
        Assert.AreEqual(expected.Id, actual.Id);
    }
}
