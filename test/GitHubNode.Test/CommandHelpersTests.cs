using System.IO;
using GitHubNode.Commands;

namespace GitHubNode.Test;

[TestClass]
public class CommandHelpersTests
{
    [TestMethod]
    public void GetGitHubFolderPath_ReturnsNull_WhenPathIsNull()
    {
        string result = CommandHelpers.GetGitHubFolderPath(null);

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetGitHubFolderPath_ReturnsPath_WhenTargetIsGitHubFolder()
    {
        var path = Path.Combine("C:\\repo", ".github");

        string result = CommandHelpers.GetGitHubFolderPath(path);

        Assert.AreEqual(path, result);
    }

    [TestMethod]
    public void GetGitHubFolderPath_ReturnsGitHubPath_WhenPathContainsGitHubFolder()
    {
        var path = Path.Combine("C:\\repo", ".github", "workflows");
        var expected = Path.Combine("C:\\repo", ".github");

        string result = CommandHelpers.GetGitHubFolderPath(path);

        Assert.AreEqual(expected, result);
    }

    [TestMethod]
    public void GetGitHubFolderPath_ReturnsChildGitHubPath_WhenGitHubFolderExists()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());
        var gitHubPath = Path.Combine(root, ".github");

        try
        {
            Directory.CreateDirectory(gitHubPath);

            string result = CommandHelpers.GetGitHubFolderPath(root);

            Assert.AreEqual(gitHubPath, result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void GetGitHubFolderPath_ReturnsNull_WhenPathDoesNotContainGitHubFolder()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            Directory.CreateDirectory(root);

            string result = CommandHelpers.GetGitHubFolderPath(root);

            Assert.IsNull(result);
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }

    [TestMethod]
    public void SanitizeFileName_ReplacesInvalidCharactersAndSpaces()
    {
        string result = CommandHelpers.SanitizeFileName("My: Prompt Name");

        Assert.AreEqual("my--prompt-name", result);
    }

    [TestMethod]
    public void SanitizeFileName_LowercasesValue()
    {
        string result = CommandHelpers.SanitizeFileName("HELLO");

        Assert.AreEqual("hello", result);
    }

    [TestMethod]
    public void EnsureFolder_CreatesFolderAndReturnsPath()
    {
        var root = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

        try
        {
            string result = CommandHelpers.EnsureFolder(root, "prompts");

            Assert.IsTrue(Directory.Exists(result));
        }
        finally
        {
            if (Directory.Exists(root))
            {
                Directory.Delete(root, recursive: true);
            }
        }
    }
}
