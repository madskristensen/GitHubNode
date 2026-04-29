using System.IO.Compression;
using GitHubNode.Services.Marketplace;

namespace GitHubNode.Test;

[TestClass]
public class WellKnownArchiveServiceTests
{
    [TestMethod]
    public void ExtractArchive_ExtractsZipWithRootSkillMarkdown()
    {
        byte[] archiveBytes = CreateZipArchive(("SKILL.md", "---\nname: test-skill\ndescription: Test\n---\n"));
        string targetDirectory = CreateTargetDirectory();

        string skillPath = WellKnownArchiveService.ExtractArchive(archiveBytes, new Uri("https://example.com/test-skill.zip"), targetDirectory);

        Assert.IsTrue(File.Exists(skillPath));
        Assert.AreEqual(Path.Combine(targetDirectory, "SKILL.md"), skillPath);
    }

    [TestMethod]
    public void ExtractArchive_RejectsZipPathTraversal()
    {
        byte[] archiveBytes = CreateZipArchive(("../SKILL.md", "unsafe"));
        string targetDirectory = CreateTargetDirectory();

        AssertThrowsInvalidOperation(() =>
            WellKnownArchiveService.ExtractArchive(archiveBytes, new Uri("https://example.com/test-skill.zip"), targetDirectory));
    }

    [TestMethod]
    public void ExtractArchive_RejectsZipMissingRootSkillMarkdown()
    {
        byte[] archiveBytes = CreateZipArchive(("nested/SKILL.md", "---\nname: test-skill\ndescription: Test\n---\n"));
        string targetDirectory = CreateTargetDirectory();

        AssertThrowsInvalidOperation(() =>
            WellKnownArchiveService.ExtractArchive(archiveBytes, new Uri("https://example.com/test-skill.zip"), targetDirectory));
    }

    private static void AssertThrowsInvalidOperation(Action action)
    {
        try
        {
            action();
        }
        catch (InvalidOperationException)
        {
            return;
        }

        Assert.Fail("Expected InvalidOperationException.");
    }

    private static byte[] CreateZipArchive(params (string Name, string Content)[] entries)
    {
        using var memoryStream = new MemoryStream();
        using (var archive = new ZipArchive(memoryStream, ZipArchiveMode.Create, leaveOpen: true))
        {
            foreach (var entryInfo in entries)
            {
                var entry = archive.CreateEntry(entryInfo.Name);
                using var writer = new StreamWriter(entry.Open());
                writer.Write(entryInfo.Content);
            }
        }

        return memoryStream.ToArray();
    }

    private static string CreateTargetDirectory()
    {
        return Path.Combine(Path.GetTempPath(), "GitHubNode.Tests", Guid.NewGuid().ToString("N"));
    }
}
