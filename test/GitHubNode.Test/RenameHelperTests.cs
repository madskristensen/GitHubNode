using System.IO;
using GitHubNode.Commands;

namespace GitHubNode.Test;

[TestClass]
public class RenameHelperTests
{
    [TestMethod]
    public void BuildRenamedPath_ReturnsSanitizedPath_WhenInputIsValid()
    {
        string existingPath = Path.Combine("C:\\repo", ".github", "prompts", "old.prompt.md");

        string renamedPath = RenameHelper.BuildRenamedPath(existingPath, "New: Prompt Name");

        Assert.AreEqual(Path.Combine("C:\\repo", ".github", "prompts", "new--prompt-name"), renamedPath);
    }

    [TestMethod]
    public void BuildRenamedPath_ReturnsNull_WhenExistingPathMissing()
    {
        string renamedPath = RenameHelper.BuildRenamedPath(null, "new-name");

        Assert.IsNull(renamedPath);
    }

    [TestMethod]
    public void BuildRenamedPath_ReturnsNull_WhenRequestedNameMissing()
    {
        string existingPath = Path.Combine("C:\\repo", ".github", "prompts", "old.prompt.md");

        string renamedPath = RenameHelper.BuildRenamedPath(existingPath, " ");

        Assert.IsNull(renamedPath);
    }

    [TestMethod]
    public void BuildRenamedPath_PreservesExtension_WhenRequestedNameIncludesExtension()
    {
        string existingPath = Path.Combine("C:\\repo", ".github", "prompts", "old.prompt.md");

        string renamedPath = RenameHelper.BuildRenamedPath(existingPath, "new-name.prompt.md");

        Assert.AreEqual(Path.Combine("C:\\repo", ".github", "prompts", "new-name.prompt.md"), renamedPath);
    }

    [TestMethod]
    public void BuildRenamedPath_ReturnsNameOnly_WhenExistingPathHasNoDirectory()
    {
        string renamedPath = RenameHelper.BuildRenamedPath("old.md", "New Name");

        Assert.AreEqual("new-name", renamedPath);
    }
}
