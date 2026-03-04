using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Reflection;
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
}
