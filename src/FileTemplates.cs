namespace GitHubNode
{
    /// <summary>
    /// Contains template content for common .github files and folders.
    /// </summary>
    internal static class FileTemplates
    {
        /// <summary>
        /// Template for copilot-instructions.md file.
        /// </summary>
        public const string CopilotInstructions = @"# Copilot Instructions

<!-- 
This file provides custom instructions to GitHub Copilot for this repository.
These instructions are automatically included in Copilot chat conversations.
Learn more: https://code.visualstudio.com/docs/copilot/customization/custom-instructions
-->

## Project Overview

<!-- Describe your project, its purpose, and key technologies -->

## Coding Standards

<!-- Define your coding conventions, naming patterns, and style preferences -->
- Follow the existing code style in this repository
- Use meaningful variable and function names
- Add comments for complex logic

## Architecture Guidelines

<!-- Describe the project structure and architectural patterns -->

## Testing Requirements

<!-- Specify testing practices and requirements -->
- Write unit tests for new functionality
- Ensure existing tests pass before submitting changes

## Additional Context

<!-- Add any other information that would help Copilot understand your project -->
";

        /// <summary>
        /// Template for a custom agent file (.agent.md).
        /// </summary>
        public const string CustomAgent = @"---
name: {0}
description: A custom Copilot agent
---

# {0}

<!-- 
This is a custom Copilot agent definition.
Learn more: https://code.visualstudio.com/docs/copilot/customization/custom-agents
-->

## Role

Define the role and expertise of this agent.

## Capabilities

- Capability 1
- Capability 2

## Instructions

Provide specific instructions for how this agent should behave.

## Tools

<!-- Optionally specify which tools this agent can use -->
";

        /// <summary>
        /// Template for a prompt file (.prompt.md).
        /// </summary>
        public const string PromptFile = @"---
mode: agent
description: A reusable prompt
---

# {0}

<!-- 
This is a reusable prompt file for common development tasks.
Learn more: https://code.visualstudio.com/docs/copilot/customization/prompt-files
-->

## Context

<!-- Use #file, #folder, or #codebase to include relevant context -->

## Task

Describe what this prompt should accomplish.

## Guidelines

- Guideline 1
- Guideline 2

## Output

Describe the expected output format.
";

        /// <summary>
        /// Template for an agent skill (skill.md).
        /// </summary>
        public const string AgentSkill = @"---
name: {0}
description: An agent skill
---

# {0}

<!-- 
This is an agent skill definition.
Agent Skills teach Copilot specialized capabilities through instructions and resources.
Learn more: https://code.visualstudio.com/docs/copilot/customization/agent-skills
-->

## Purpose

Describe what this skill enables the agent to do.

## Instructions

Provide detailed instructions for performing this skill.

## Examples

<!-- Include examples of inputs and expected outputs -->

### Example 1

Input: ...
Output: ...
";

        /// <summary>
        /// Template for a GitHub Actions workflow.
        /// </summary>
        public const string GitHubActionsWorkflow = @"name: {0}

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4
    
    - name: Setup
      run: echo ""Add your setup steps here""
    
    - name: Build
      run: echo ""Add your build steps here""
    
    - name: Test
      run: echo ""Add your test steps here""
";

        /// <summary>
        /// Template for a .NET build workflow.
        /// </summary>
        public const string DotNetBuildWorkflow = @"name: .NET Build

on:
  push:
    branches: [ main ]
  pull_request:
    branches: [ main ]

jobs:
  build:
    runs-on: ubuntu-latest

    steps:
    - uses: actions/checkout@v4
    
    - name: Setup .NET
      uses: actions/setup-dotnet@v4
      with:
        dotnet-version: '8.0.x'
    
    - name: Restore dependencies
      run: dotnet restore
    
    - name: Build
      run: dotnet build --no-restore
    
    - name: Test
      run: dotnet test --no-build --verbosity normal
";

        /// <summary>
        /// Template for dependabot.yml.
        /// </summary>
        public const string Dependabot = @"# Dependabot configuration
# Learn more: https://docs.github.com/en/code-security/dependabot/dependabot-version-updates/configuration-options-for-the-dependabot.yml-file

version: 2
updates:
  # Enable version updates for NuGet packages
  - package-ecosystem: ""nuget""
    directory: ""/""
    schedule:
      interval: ""weekly""
    open-pull-requests-limit: 10

  # Enable version updates for GitHub Actions
  - package-ecosystem: ""github-actions""
    directory: ""/""
    schedule:
      interval: ""weekly""
";

        /// <summary>
        /// Template for CODEOWNERS file.
        /// </summary>
        public const string CodeOwners = @"# CODEOWNERS file
# Learn more: https://docs.github.com/en/repositories/managing-your-repositorys-settings-and-features/customizing-your-repository/about-code-owners

# These owners will be the default owners for everything in the repo
* @your-username

# You can also specify owners for specific paths
# /src/frontend/ @frontend-team
# /src/backend/ @backend-team
# /docs/ @docs-team
";

        /// <summary>
        /// Template for issue template.
        /// </summary>
        public const string IssueTemplate = @"---
name: {0}
about: Describe this template's purpose here
title: ''
labels: ''
assignees: ''
---

## Description

A clear and concise description of the issue.

## Steps to Reproduce

1. Go to '...'
2. Click on '...'
3. See error

## Expected Behavior

What you expected to happen.

## Actual Behavior

What actually happened.

## Environment

- OS: [e.g. Windows 11]
- Version: [e.g. 1.0.0]

## Additional Context

Add any other context about the problem here.
";

        /// <summary>
        /// Template for pull request template.
        /// </summary>
        public const string PullRequestTemplate = @"## Description

Please include a summary of the changes and which issue is fixed.

Fixes # (issue)

## Type of Change

- [ ] Bug fix (non-breaking change which fixes an issue)
- [ ] New feature (non-breaking change which adds functionality)
- [ ] Breaking change (fix or feature that would cause existing functionality to not work as expected)
- [ ] Documentation update

## Checklist

- [ ] My code follows the style guidelines of this project
- [ ] I have performed a self-review of my code
- [ ] I have commented my code, particularly in hard-to-understand areas
- [ ] I have made corresponding changes to the documentation
- [ ] My changes generate no new warnings
- [ ] I have added tests that prove my fix is effective or that my feature works
- [ ] New and existing unit tests pass locally with my changes
";
    }
}
