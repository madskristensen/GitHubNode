using System.Collections.Generic;
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
    public void GetCachedFileStatus_ReturnsNotInRepo_ForUncachedPath()
    {
        string path = Path.Combine(Path.GetTempPath(), $"{Path.GetRandomFileName()}.txt");

        GitStatusService.InvalidateCache();
        GitFileStatus status = GitStatusService.GetCachedFileStatus(path);

        Assert.AreEqual(GitFileStatus.NotInRepo, status);
    }

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

    private static void AssertMonikerEquals(
        ImageMoniker expected,
        ImageMoniker actual)
    {
        Assert.AreEqual(expected.Guid, actual.Guid);
        Assert.AreEqual(expected.Id, actual.Id);
    }
}
