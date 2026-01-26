using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Base class for all GitHub nodes in Solution Explorer.
    /// </summary>
    internal abstract class GitHubNodeBase : ITreeDisplayItem, INotifyPropertyChanged, ISupportDisposalNotification, IInteractionPatternProvider
    {
        private bool _isDisposed;
        private IAttachedCollectionSource _containedByCollection;

        protected GitHubNodeBase(object parentItem)
        {
            ParentItem = parentItem;
        }

        /// <summary>
        /// Gets this item as the source for the attached collection (IAttachedCollectionSource).
        /// For IAttachedCollectionSource semantics, SourceItem should be the item itself.
        /// </summary>
        public object SourceItem => this;

        /// <summary>
        /// Gets the parent item for ContainedBy relationship support.
        /// This is used during search to trace items back to their parents.
        /// </summary>
        public object ParentItem { get; }

        /// <summary>
        /// Gets or sets the ContainedBy collection for this item.
        /// This is set during search to enable Solution Explorer to trace items back to their parents.
        /// </summary>
        public IAttachedCollectionSource ContainedByCollection
        {
            get => _containedByCollection;
            set => _containedByCollection = value;
        }

        /// <summary>
        /// Gets the types of patterns this node supports.
        /// </summary>
        protected abstract HashSet<Type> SupportedPatterns { get; }

        // ITreeDisplayItem
        public abstract string Text { get; }
        public virtual string ToolTipText => Text;
        public virtual object ToolTipContent => null;
        public virtual string StateToolTipText => null;
        public virtual System.Windows.FontStyle FontStyle => System.Windows.FontStyles.Normal;
        public virtual System.Windows.FontWeight FontWeight => System.Windows.FontWeights.Normal;
        public virtual bool IsCut => false;

        // INotifyPropertyChanged
        public event PropertyChangedEventHandler PropertyChanged;

        protected void RaisePropertyChanged(string propertyName)
        {
            PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(propertyName));
        }

        // ISupportDisposalNotification
        public bool IsDisposed => _isDisposed;
        public event EventHandler IsDisposedChanged;

        public void Dispose()
        {
            if (!_isDisposed)
            {
                _isDisposed = true;
                OnDisposing();
                IsDisposedChanged?.Invoke(this, EventArgs.Empty);
            }
        }

        protected virtual void OnDisposing() { }

        // IInteractionPatternProvider
        public TPattern GetPattern<TPattern>() where TPattern : class
        {
            if (SupportedPatterns.Contains(typeof(TPattern)) && this is TPattern pattern)
            {
                return pattern;
            }
            return null;
        }
    }
}
