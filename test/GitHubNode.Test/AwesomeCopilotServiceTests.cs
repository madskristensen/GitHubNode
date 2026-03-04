using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
using System.IO;
using System.Collections;
using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class AwesomeCopilotServiceTests
{
    [TestMethod]
    public void GetFriendlyHttpErrorMessage_ReturnsRateLimitMessage_WhenRateLimitHeadersArePresent()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Forbidden);
        response.Headers.Add("X-RateLimit-Remaining", "0");

        string message = InvokeGetFriendlyHttpErrorMessage(response.StatusCode, response.Headers);

        Assert.AreEqual("GitHub API rate limit reached. Please wait and try again.", message);
    }

    [TestMethod]
    public void GetFriendlyHttpErrorMessage_ReturnsUnauthorizedMessage_ForUnauthorizedStatus()
    {
        using HttpResponseMessage response = new(HttpStatusCode.Unauthorized);

        string message = InvokeGetFriendlyHttpErrorMessage(response.StatusCode, response.Headers);

        Assert.AreEqual("GitHub API authentication failed. Please check your GitHub credentials and try again.", message);
    }

    [TestMethod]
    public void GetFriendlyHttpErrorMessage_ReturnsUnavailableMessage_ForServerErrorStatus()
    {
        using HttpResponseMessage response = new(HttpStatusCode.ServiceUnavailable);

        string message = InvokeGetFriendlyHttpErrorMessage(response.StatusCode, response.Headers);

        Assert.AreEqual("GitHub API is temporarily unavailable. Please try again later.", message);
    }

    [TestMethod]
    public void GetFriendlyHttpErrorMessage_ReturnsNull_ForNonMappedStatus()
    {
        using HttpResponseMessage response = new(HttpStatusCode.BadRequest);

        string message = InvokeGetFriendlyHttpErrorMessage(response.StatusCode, response.Headers);

        Assert.IsNull(message);
    }

    [TestMethod]
    public void IsRateLimitResponse_ReturnsTrue_WhenRetryAfterHeaderIsPresent()
    {
        using HttpResponseMessage response = new((HttpStatusCode)429);
        response.Headers.Add("Retry-After", "60");

        bool isRateLimited = InvokeIsRateLimitResponse(response.StatusCode, response.Headers);

        Assert.IsTrue(isRateLimited);
    }

    [TestMethod]
    public void GetLastFetchIssue_ThrowsArgumentNullException_WhenProviderIsNull()
    {
        try
        {
            _ = AwesomeCopilotService.GetLastFetchIssue(TemplateType.Agent, null);
            Assert.Fail();
        }
        catch (ArgumentNullException)
        {
        }
    }

    [TestMethod]
    public void ParseGitHubTreeJson_HandlesLargeValidPayload()
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("ParseGitHubTreeJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string largePadding = new('x', 2_200_000);
        string json = "{\"tree\":[{\"path\":\"skills/demo/skill.md\",\"type\":\"blob\"}],\"padding\":\"" + largePadding + "\"}";

        object result = method.Invoke(null, [json]);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<IList>(result);
        Assert.AreEqual(1, ((IList)result).Count);
    }

    [TestMethod]
    public void ParseGitHubContentsJson_ParsesEntriesWithNestedLinksObject()
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("ParseGitHubContentsJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string json = "[{\"type\":\"file\",\"name\":\"skill.md\",\"_links\":{\"self\":\"x\",\"git\":\"y\"}}]";

        object result = method.Invoke(null, [json]);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<IList>(result);
        Assert.AreEqual(1, ((IList)result).Count);
    }

    [TestMethod]
    public void ParseGitHubContentsJson_ParsesWhenTypeAppearsBeforeName()
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("ParseGitHubContentsJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string json = "[{\"type\":\"file\",\"name\":\"template.prompt.md\"}]";

        object result = method.Invoke(null, [json]);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<IList>(result);
        Assert.AreEqual(1, ((IList)result).Count);
    }

    [TestMethod]
    public void ParseGitHubTreeJson_ParsesWhenTypeAppearsBeforePath()
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("ParseGitHubTreeJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string json = "{\"tree\":[{\"type\":\"blob\",\"path\":\"skills/demo/skill.md\"}]}";

        object result = method.Invoke(null, [json]);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<IList>(result);
        Assert.AreEqual(1, ((IList)result).Count);
    }

    [TestMethod]
    public void ParseGitHubContentsJson_ParsesValidArrayResponse()
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("ParseGitHubContentsJson", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        string json = "[{\"name\":\"template.prompt.md\",\"type\":\"file\"}]";

        object result = method.Invoke(null, [json]);

        Assert.IsNotNull(result);
        Assert.IsInstanceOfType<IList>(result);
        Assert.AreEqual(1, ((IList)result).Count);
    }

    [TestMethod]
    public void SaveToCache_PersistsEmptyMarker_AndLoadFromCacheReturnsEmptyList()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), $"githubnode-{Guid.NewGuid():N}.cache");

        try
        {
            InvokeSaveToCache(cacheFile, new List<TemplateInfo>());

            List<TemplateInfo> templates = InvokeLoadFromCache(cacheFile, expiredOk: false);

            Assert.IsNotNull(templates);
            Assert.AreEqual(0, templates.Count);
        }
        finally
        {
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
    }

    [TestMethod]
    public void GetTemplateDisplayName_ReturnsFileName_ForDotNetSkillsAgents()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Agent, "dotnet-msbuild/agents/build-perf.agent.md", "build-perf.agent.md");

        Assert.AreEqual("build-perf.agent.md", displayName);
    }

    [TestMethod]
    public void GetTemplateDisplayName_ReturnsRelativePath_ForAwesomeCopilotAgents()
    {
        TemplateProvider provider = AwesomeCopilotTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Agent, "dotnet/build-perf.agent.md", "build-perf.agent.md");

        Assert.AreEqual("dotnet/build-perf.agent.md", displayName);
    }

    private static string InvokeGetFriendlyHttpErrorMessage(HttpStatusCode statusCode, HttpResponseHeaders headers)
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("GetFriendlyHttpErrorMessage", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (string)method.Invoke(null, [statusCode, headers]);
    }

    private static bool InvokeIsRateLimitResponse(HttpStatusCode statusCode, HttpResponseHeaders headers)
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("IsRateLimitResponse", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (bool)method.Invoke(null, [statusCode, headers]);
    }

    private static void InvokeSaveToCache(string cacheFile, List<TemplateInfo> templates)
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("SaveToCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        method.Invoke(null, [cacheFile, templates]);
    }

    private static List<TemplateInfo> InvokeLoadFromCache(string cacheFile, bool expiredOk)
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("LoadFromCache", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (List<TemplateInfo>)method.Invoke(null, [cacheFile, expiredOk]);
    }

    private static string InvokeGetTemplateDisplayName(TemplateProvider provider, TemplateType templateType, string relativePath, string fileName)
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("GetTemplateDisplayName", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (string)method.Invoke(null, [provider, templateType, relativePath, fileName]);
    }
}
