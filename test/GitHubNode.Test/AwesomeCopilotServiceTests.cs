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
    public void ExtractDisplayNameFromFrontMatter_ReturnsName_WhenNameExists()
    {
        const string markdown = "---\nname: Build Perf Agent\ntitle: Ignored Title\n---\n# Heading";

        string displayName = InvokeExtractDisplayNameFromFrontMatter(markdown);

        Assert.AreEqual("Build Perf Agent", displayName);
    }

    [TestMethod]
    public void ExtractDisplayNameFromFrontMatter_ReturnsTitle_WhenNameMissing()
    {
        const string markdown = "---\ntitle: \"Agent Title\"\n---\n# Heading";

        string displayName = InvokeExtractDisplayNameFromFrontMatter(markdown);

        Assert.AreEqual("Agent Title", displayName);
    }

    [TestMethod]
    public void ExtractDisplayNameFromFrontMatter_ReturnsNull_WhenFrontMatterMissing()
    {
        const string markdown = "# Heading\ncontent";

        string displayName = InvokeExtractDisplayNameFromFrontMatter(markdown);

        Assert.IsNull(displayName);
    }

    [TestMethod]
    public void ExtractDisplayNameFromFrontMatter_ReturnsName_WhenFrontMatterHasBomAndLeadingBlankLines()
    {
        const string markdown = "\uFEFF\n\n---\nname: '4.1 Beast Mode v3.1'\n---\ncontent";

        string displayName = InvokeExtractDisplayNameFromFrontMatter(markdown);

        Assert.AreEqual("4.1 Beast Mode v3.1", displayName);
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
    public void GetTemplateDisplayName_ReturnsRelativePath_ForDotNetSkillsAgents()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Agent, "dotnet-msbuild/agents/build-perf.agent.md", "build-perf.agent.md");

        Assert.AreEqual("dotnet-msbuild/agents/build-perf.agent.md", displayName);
    }

    [TestMethod]
    public void GetTemplateDisplayName_ReturnsRelativePath_ForAwesomeCopilotAgents()
    {
        TemplateProvider provider = AwesomeCopilotTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Agent, "dotnet/build-perf.agent.md", "build-perf.agent.md");

        Assert.AreEqual("dotnet/build-perf.agent.md", displayName);
    }

    [TestMethod]
    public void GetTemplateDisplayName_ReturnsRelativePath_ForDotNetSkillsSkills()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Skill, "dotnet-msbuild/SKILL.md", "SKILL.md");

        Assert.AreEqual("dotnet-msbuild/SKILL.md", displayName);
    }

    [TestMethod]
    public void GetTemplateDisplayName_ReturnsRelativePath_ForNestedDotNetSkillsSkills()
    {
        TemplateProvider provider = DotNetSkillsTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Skill, "dotnet-msbuild/skills/binlog-failure-analysis/SKILL.md", "SKILL.md");

        Assert.AreEqual("dotnet-msbuild/skills/binlog-failure-analysis/SKILL.md", displayName);
    }

    [TestMethod]
    public void GetTemplateDisplayName_ReturnsRelativePath_ForAnthropicSkillsSkills()
    {
        TemplateProvider provider = AnthropicSkillsTemplateProvider.Create();

        string displayName = InvokeGetTemplateDisplayName(provider, TemplateType.Skill, "algorithmic-art/SKILL.md", "SKILL.md");

        Assert.AreEqual("algorithmic-art/SKILL.md", displayName);
    }

    [TestMethod]
    public void LoadFromCache_PreservesDisplayName_FromCacheEntry()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), $"githubnode-{Guid.NewGuid():N}.cache");

        try
        {
            File.WriteAllLines(cacheFile,
            [
                "algorithmic-art\talgorithmic-art\thttps://example.invalid/algorithmic-art/SKILL.md\t2\tanthropic-skills\talgorithmic-art/SKILL.md"
            ]);

            List<TemplateInfo> templates = InvokeLoadFromCache(cacheFile, expiredOk: false);

            Assert.IsNotNull(templates);
            Assert.AreEqual(1, templates.Count);
            Assert.AreEqual("algorithmic-art/SKILL.md", templates[0].DisplayName);
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
    public void LoadFromCache_PreservesDotNetAgentDisplayName_FromCacheEntry()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), $"githubnode-{Guid.NewGuid():N}.cache");

        try
        {
            File.WriteAllLines(cacheFile,
            [
                "build-perf\tbuild-perf.agent.md\thttps://example.invalid/build-perf.agent.md\t0\tdotnet-skills-plugins\tbuild-perf.agent.md"
            ]);

            List<TemplateInfo> templates = InvokeLoadFromCache(cacheFile, expiredOk: false);

            Assert.IsNotNull(templates);
            Assert.AreEqual(1, templates.Count);
            Assert.AreEqual("build-perf.agent.md", templates[0].DisplayName);
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
    public void LoadFromCache_PreservesDotNetSkillDisplayName_FromCacheEntry()
    {
        string cacheFile = Path.Combine(Path.GetTempPath(), $"githubnode-{Guid.NewGuid():N}.cache");

        try
        {
            File.WriteAllLines(cacheFile,
            [
                "binlog-failure-analysis\tbinlog-failure-analysis\thttps://example.invalid/dotnet-msbuild/skills/binlog-failure-analysis/SKILL.md\t2\tdotnet-skills-plugins\tdotnet-msbuild/skills/binlog-failure-analysis/SKILL.md"
            ]);

            List<TemplateInfo> templates = InvokeLoadFromCache(cacheFile, expiredOk: false);

            Assert.IsNotNull(templates);
            Assert.AreEqual(1, templates.Count);
            Assert.AreEqual("dotnet-msbuild/skills/binlog-failure-analysis/SKILL.md", templates[0].DisplayName);
        }
        finally
        {
            if (File.Exists(cacheFile))
            {
                File.Delete(cacheFile);
            }
        }
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

    private static string InvokeExtractDisplayNameFromFrontMatter(string markdown)
    {
        MethodInfo method = typeof(AwesomeCopilotService).GetMethod("ExtractDisplayNameFromFrontMatter", BindingFlags.NonPublic | BindingFlags.Static);
        Assert.IsNotNull(method);

        return (string)method.Invoke(null, [markdown]);
    }

}
