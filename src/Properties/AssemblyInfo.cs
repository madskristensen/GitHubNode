using System.Reflection;
using System.Runtime.CompilerServices;
using System.Runtime.InteropServices;
using GitHubNode;

[assembly: AssemblyTitle(Vsix.Name)]
[assembly: AssemblyDescription(Vsix.Description)]
[assembly: AssemblyConfiguration("")]
[assembly: AssemblyCompany(Vsix.Author)]
[assembly: AssemblyProduct(Vsix.Name)]
[assembly: AssemblyCopyright(Vsix.Author)]
[assembly: AssemblyTrademark("")]
[assembly: AssemblyCulture("")]

[assembly: ComVisible(false)]

[assembly: AssemblyVersion(Vsix.Version)]
[assembly: AssemblyFileVersion(Vsix.Version)]

[assembly: ProvideCodeBase(AssemblyName = "GitHubNode")]
[assembly: InternalsVisibleTo("GitHubNode.Test")]

namespace System.Runtime.CompilerServices
{
    public class IsExternalInit { }
}