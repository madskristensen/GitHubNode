# Copilot Instructions
- This is a Visual Studio extension project
- It uses the latest version of C# that is supported on .NET Framework 4.8
- source.extension.cs and VSCommandTable.cs are generated files, but you can't generate them, so just edit them directly
 
# Team Best Practices
- A lot of developers with no experience in Visual Studio extensions will be reading the code.
- The code must be readable and maintainable, especially for new team members.
- Simplicity is key.

# Git Commit Message Guidelines

Writing clear and consistent commit messages is crucial for maintaining a readable and understandable project history. Follow these guidelines when creating commit messages:

- **Treat .md files as plain text files:** When generating commit messages, recognize that .md files are not binary files and their diffs offer meaningful explanations of changes.
- **Use the imperative mood:** Write commit messages as if you're giving a command. For example, use "Fix bug" instead of "Fixed bug" or "Fixes bug."
- **Limit the subject line to 50 characters:** This ensures that the message is easy to read in Git logs and on platforms like GitHub.
- **Capitalize the subject line:** Start the subject line with a capital letter.
- **Do not end the subject line with a period:** This helps keep the message concise.
- **Use a blank line between the subject and the body:** This separates the subject from the body of the commit message.
- **Wrap the body at 72 characters:** This improves readability in various Git tools.
- **Explain the "why" and "what" of the change:** The body of the commit message should provide context for the change. Explain why the change was necessary and what the change accomplishes.
- **Reference issue tracker IDs:** If the commit relates to a specific issue in the issue tracker (e.g., Jira, Azure DevOps), include the issue ID in the commit message (e.g., `Fixes #123`).
- **Example:**

