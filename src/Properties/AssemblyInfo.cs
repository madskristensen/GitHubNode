using System.Runtime.CompilerServices;

[assembly: ProvideCodeBase(AssemblyName = "GitHubNode")]
[assembly: InternalsVisibleTo("GitHubNode.Test")]

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}