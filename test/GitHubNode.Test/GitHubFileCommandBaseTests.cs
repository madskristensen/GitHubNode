using GitHubNode.Commands;
using GitHubNode.Services;

namespace GitHubNode.Test;

[TestClass]
public class GitHubFileCommandBaseTests
{
    [TestMethod]
    public void GetExistingTemplateFileNames_ReturnsMatchingFilesFromSubfolder()
    {
        var targetFolder = CreateTempDirectory();
        var promptsFolder = Path.Combine(targetFolder, "prompts");
        Directory.CreateDirectory(promptsFolder);
        File.WriteAllText(Path.Combine(promptsFolder, "code-review.prompt.md"), "content");
        File.WriteAllText(Path.Combine(promptsFolder, "notes.md"), "content");

        try
        {
            var command = new TestPromptCommand();
            var fileNames = command.GetExistingTemplateFileNamesForTest(targetFolder);

            CollectionAssert.AreEqual(
                new[] { "code-review.prompt.md" },
                fileNames.OrderBy(fileName => fileName).ToArray());
        }
        finally
        {
            Directory.Delete(targetFolder, recursive: true);
        }
    }

    [TestMethod]
    public void GetExistingTemplateFileNames_ReturnsEmptyWhenSubfolderDoesNotExist()
    {
        var targetFolder = CreateTempDirectory();

        try
        {
            var command = new TestPromptCommand();
            var fileNames = command.GetExistingTemplateFileNamesForTest(targetFolder);

            Assert.AreEqual(0, fileNames.Count);
        }
        finally
        {
            Directory.Delete(targetFolder, recursive: true);
        }
    }

    private static string CreateTempDirectory()
    {
        var directory = Path.Combine(Path.GetTempPath(), "GitHubNode.Tests", Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        return directory;
    }

    private sealed class TestPromptCommand : GitHubFileCommandBase<TestPromptCommand>
    {
        protected override TemplateType? TemplateType => GitHubNode.Services.TemplateType.Prompt;

        protected override string RequiredExtension => ".prompt.md";

        protected override string SubfolderName => "prompts";

        public IReadOnlyCollection<string> GetExistingTemplateFileNamesForTest(string targetFolder)
            => GetExistingTemplateFileNames(targetFolder);

        protected override string GetFilePath(string targetFolder, string userInput)
            => BuildFilePath(targetFolder, userInput);

        protected override string GetFileContent(string userInput)
            => string.Empty;
    }
}
