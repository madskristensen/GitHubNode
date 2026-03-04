# Copilot Instructions

## General Guidelines
- This is a Visual Studio extension project.
- It uses the latest version of C# that is supported on .NET Framework 4.8.
- A lot of developers with no experience in Visual Studio extensions will be reading the code.
- The code must be readable and maintainable, especially for new team members.
- Simplicity is key.
- Don't use emojis in .md files and elsewhere.
- Don't use the em-dash character (-), use single hyphens (-) surrounded by space characters instead.

## Exception Handling
- When handling exceptions in this repo, log caught exceptions with `ex.LogAsync()` to aid diagnostics.

## File Management
- `source.extension.cs` and `VSCommandTable.cs` are generated files, but you can't generate them, so just edit them directly.

## Template Loading
- When template loading fails due to GitHub API rate limiting, show an explicit user-facing status message in the input dialog instead of a generic offline message.

## JSON Parsing
- Use System.Text.Json for JSON parsing instead of legacy serializers in this codebase.
