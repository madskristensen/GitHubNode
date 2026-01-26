using System.Collections;
using System.Collections.ObjectModel;
using System.ComponentModel;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Collection source that provides the GitHub root node as a child of the solution node.
    /// This wrapper is needed so that the GitHubRootNode appears as a child of the solution.
    /// </summary>
    internal sealed class GitHubSolutionCollectionSource : IAttachedCollectionSource, INotifyPropertyChanged, IDisposable
    {
        private readonly ObservableCollection<object> _items;
        private bool _disposed;

        public GitHubSolutionCollectionSource(object sourceItem, GitHubRootNode rootNode)
        {
            SourceItem = sourceItem;
            _items = [];

            // Add the root node immediately
            _items.Add(rootNode);
        }

        public object SourceItem { get; }

        public bool HasItems => _items.Count > 0;

        public IEnumerable Items => _items;

        public event PropertyChangedEventHandler PropertyChanged;

        private void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        public void Dispose()
        {
            if (!_disposed)
            {
                _disposed = true;
                // Do NOT dispose the root node here - it's owned by the source provider
                _items.Clear();
            }
        }
    }
}
