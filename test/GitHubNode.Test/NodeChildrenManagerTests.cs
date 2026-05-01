using System.IO;
using GitHubNode.SolutionExplorer;

namespace GitHubNode.Test;

[TestClass]
public class NodeChildrenManagerTests
{
    [TestMethod]
    public void BuildChildPath_CombinesNewParentWithExistingFileName()
    {
        string existingChildPath = Path.Combine("C:\\repo", ".github", "skills", "old-skill", "SKILL.md");
        string newParentPath = Path.Combine("C:\\repo", ".github", "skills", "new-skill");

        string newChildPath = NodeChildrenManager.BuildChildPath(newParentPath, existingChildPath);

        Assert.AreEqual(Path.Combine("C:\\repo", ".github", "skills", "new-skill", "SKILL.md"), newChildPath);
    }

    [TestMethod]
    public void BuildChildPath_ReturnsChildNameOnly_WhenNewParentMissing()
    {
        string existingChildPath = Path.Combine("C:\\repo", ".github", "skills", "old-skill", "SKILL.md");

        string newChildPath = NodeChildrenManager.BuildChildPath(null, existingChildPath);

        Assert.AreEqual("SKILL.md", newChildPath);
    }

    [TestMethod]
    public void BuildChildPath_ReturnsExistingPath_WhenChildPathMissing()
    {
        string newChildPath = NodeChildrenManager.BuildChildPath("C:\\repo", null);

        Assert.IsNull(newChildPath);
    }

    [TestMethod]
    public void BuildChildPath_PreservesSubdirectoryName_ForFolderChild()
    {
        string existingChildPath = Path.Combine("C:\\repo", ".github", "skills", "old-skill", "examples");
        string newParentPath = Path.Combine("C:\\repo", ".github", "skills", "new-skill");

        string newChildPath = NodeChildrenManager.BuildChildPath(newParentPath, existingChildPath);

        Assert.AreEqual(Path.Combine("C:\\repo", ".github", "skills", "new-skill", "examples"), newChildPath);
    }
}
