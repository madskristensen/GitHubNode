using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Windows;
using Microsoft.Internal.VisualStudio.PlatformUI;

namespace GitHubNode.SolutionExplorer
{
    /// <summary>
    /// Base class for all MCP nodes in Solution Explorer.
    /// </summary>
    internal abstract class McpNodeBase : ITreeDisplayItem, INotifyPropertyChanged, ISupportDisposalNotification, IInteractionPatternProvider
    {
        private bool _isDisposed;

        protected McpNodeBase(object sourceItem)
        {
            SourceItem = sourceItem;
        }

        /// <summary>
        /// The parent source item this node is attached to.
        /// </summary>
        public object SourceItem { get; }

        /// <summary>
        /// Gets the types of patterns this node supports.
        /// </summary>
        protected abstract HashSet<Type> SupportedPatterns { get; }

        // ITreeDisplayItem
        public abstract string Text { get; }
        public virtual string ToolTipText => Text;
        public virtual object ToolTipContent => null;
        public virtual string StateToolTipText => null;
        public virtual FontStyle FontStyle => FontStyles.Normal;
        public virtual FontWeight FontWeight => FontWeights.Normal;
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
