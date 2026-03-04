using System.Reflection;
using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class GitHubUrlServiceTests
{
    [TestMethod]
    public void ConvertToGitHubWebUrl_ReturnsHttpsUrl_ForSshRemote()
    {
        string result = InvokeConvertToGitHubWebUrl("git@github.com:owner/repo.git");

        Assert.AreEqual("https://github.com/owner/repo", result);
    }

    [TestMethod]
    public void ConvertToGitHubWebUrl_ReturnsHttpsUrl_ForHttpsRemote()
    {
        string result = InvokeConvertToGitHubWebUrl("https://github.com/owner/repo.git");

        Assert.AreEqual("https://github.com/owner/repo", result);
    }

    [TestMethod]
    public void ConvertToGitHubWebUrl_ReturnsNull_ForUnsupportedRemote()
    {
        string result = InvokeConvertToGitHubWebUrl("https://example.com/owner/repo.git");

        Assert.IsNull(result);
    }

    [TestMethod]
    public void GetRelativePath_ReturnsChildPath_WhenPathIsWithinRepository()
    {
        string result = InvokeGetRelativePath(@"C:\repo", @"C:\repo\.github\workflows\ci.yml");

        Assert.AreEqual(@".github\workflows\ci.yml", result);
    }

    [TestMethod]
    public void GetRelativePath_ReturnsNull_WhenPathIsOutsideRepository()
    {
        string result = InvokeGetRelativePath(@"C:\repo", @"D:\other\file.txt");

        Assert.IsNull(result);
    }

    private static string InvokeConvertToGitHubWebUrl(string remoteUrl)
    {
        MethodInfo method = typeof(GitHubUrlService).GetMethod("ConvertToGitHubWebUrl", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (string)method.Invoke(null, [remoteUrl]);
    }

    private static string InvokeGetRelativePath(string basePath, string fullPath)
    {
        MethodInfo method = typeof(GitHubUrlService).GetMethod("GetRelativePath", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (string)method.Invoke(null, [basePath, fullPath]);
    }
}
