using System;
using Microsoft.VisualStudio.Imaging.Interop;

namespace GitHubNode
{
    /// <summary>
    /// Custom image monikers for the GitHubNode extension.
    /// </summary>
    internal static class CustomMonikers
    {
        private static readonly Guid ImageCatalogGuid = new("e08fd448-2a24-4e8b-883e-83e4a6ab1aa3");

        public static ImageMoniker McpIcon => new() { Guid = ImageCatalogGuid, Id = 1 };
    }
}
