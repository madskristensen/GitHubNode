using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Defines relationships for the attached collection source provider.
    /// </summary>
    internal static class Relationships
    {
        public static IAttachedRelationship Contains { get; } = new ContainsAttachedRelationship();

        private sealed class ContainsAttachedRelationship : IAttachedRelationship
        {
            public string Name => KnownRelationships.Contains;
            public string DisplayName => KnownRelationships.Contains;
        }
    }
}
