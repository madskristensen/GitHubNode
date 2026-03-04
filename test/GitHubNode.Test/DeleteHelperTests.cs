using GitHubNode.Commands;

namespace GitHubNode.Test;

[TestClass]
public class DeleteHelperTests
{
    [TestMethod]
    public void CreateFolderDeleteMessage_ReturnsRecursiveMessage_WhenFolderHasContent()
    {
        string message = DeleteHelper.CreateFolderDeleteMessage("workflows", hasContent: true);

        Assert.AreEqual("Are you sure you want to delete 'workflows' and all of its contents?\n\nThis action cannot be undone.", message);
    }

    [TestMethod]
    public void CreateFolderDeleteMessage_ReturnsEmptyFolderMessage_WhenFolderIsEmpty()
    {
        string message = DeleteHelper.CreateFolderDeleteMessage("workflows", hasContent: false);

        Assert.AreEqual("Are you sure you want to delete the empty folder 'workflows'?\n\nThis action cannot be undone.", message);
    }

    [TestMethod]
    public void CreateFolderDeleteMessage_UsesFallbackName_WhenFolderNameMissing()
    {
        string message = DeleteHelper.CreateFolderDeleteMessage(" ", hasContent: false);

        Assert.AreEqual("Are you sure you want to delete the empty folder 'folder'?\n\nThis action cannot be undone.", message);
    }
}
