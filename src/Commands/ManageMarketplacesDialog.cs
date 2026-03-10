using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Runtime.InteropServices;
using System.Threading;
using System.Threading.Tasks;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Interop;
using System.Windows.Media;
using GitHubNode.Services.Marketplace;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// Dialog for managing marketplace registrations.
    /// </summary>
    internal sealed class ManageMarketplacesDialog : DialogWindow
    {
        private const int _dwmwaUseImmersiveDarkMode = 20;
        private const int _dwmwaCaptionColor = 35;
        private const int _dwmwaTextColor = 36;

        private readonly ListBox _marketplaceList;
        private readonly TextBox _urlInputTextBox;
        private readonly Button _addButton;
        private readonly Button _removeButton;
        private readonly Button _refreshButton;
        private readonly TextBlock _statusText;
        private readonly ProgressBar _progressBar;

        private ObservableCollection<MarketplaceListItem> _marketplaces;
        private CancellationTokenSource _loadCancellationTokenSource;

        public ManageMarketplacesDialog()
        {
            Title = "Manage Agent Marketplaces";
            Width = 500;
            Height = 400;
            MinWidth = 400;
            MinHeight = 300;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;
            HasMinimizeButton = false;
            HasHelpButton = false;
            ShowInTaskbar = false;

            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE)) is EnvDTE.DTE dte)
            {
                var hwnd = (IntPtr)dte.MainWindow.HWnd;
                if (hwnd != IntPtr.Zero)
                {
                    Owner = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
                }
            }

            WindowStartupLocation = WindowStartupLocation.CenterOwner;
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            _marketplaces = new ObservableCollection<MarketplaceListItem>();

            var grid = new Grid { Margin = new Thickness(12) };
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 0: Header
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // Row 1: Marketplace list
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 2: Input label
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 3: Input panel
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 4: Action buttons
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 5: Progress bar
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Row 6: Status text

            // Header
            var headerText = new TextBlock
            {
                Text = "Registered marketplaces provide templates for agents, skills, instructions, and prompts.",
                TextWrapping = TextWrapping.Wrap,
                Margin = new Thickness(0, 0, 0, 12)
            };
            headerText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(headerText, 0);
            grid.Children.Add(headerText);

            // Marketplace list
            _marketplaceList = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 8),
                ItemsSource = _marketplaces,
                DisplayMemberPath = "DisplayText"
            };
            _marketplaceList.SetResourceReference(ListBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _marketplaceList.SetResourceReference(ListBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _marketplaceList.SetResourceReference(ListBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            _marketplaceList.SelectionChanged += OnMarketplaceSelectionChanged;
            AutomationProperties.SetName(_marketplaceList, "Marketplace list");
            Grid.SetRow(_marketplaceList, 1);
            grid.Children.Add(_marketplaceList);

            // URL input row
            var inputLabel = new TextBlock
            {
                Text = "Add marketplace (owner/repo or GitHub URL):",
                Margin = new Thickness(0, 0, 0, 4)
            };
            inputLabel.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(inputLabel, 2);
            grid.Children.Add(inputLabel);

            var inputPanel = new Grid
            {
                Margin = new Thickness(0, 0, 0, 8)
            };
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = new GridLength(1, GridUnitType.Star) });
            inputPanel.ColumnDefinitions.Add(new ColumnDefinition { Width = GridLength.Auto });

            _urlInputTextBox = new TextBox
            {
                Padding = new Thickness(4, 2, 4, 2),
                VerticalContentAlignment = VerticalAlignment.Center
            };
            _urlInputTextBox.SetResourceReference(TextBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _urlInputTextBox.SetResourceReference(TextBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _urlInputTextBox.SetResourceReference(TextBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);
            AutomationProperties.SetName(_urlInputTextBox, "Marketplace repository URL");
            AutomationProperties.SetHelpText(_urlInputTextBox, "Enter owner/repo or a GitHub URL");
            Grid.SetColumn(_urlInputTextBox, 0);
            inputPanel.Children.Add(_urlInputTextBox);

            _addButton = CreateThemedButton("Add");
            _addButton.Click += OnAddButtonClick;
            _addButton.Margin = new Thickness(8, 0, 0, 0);
            AutomationProperties.SetName(_addButton, "Add marketplace");
            Grid.SetColumn(_addButton, 1);
            inputPanel.Children.Add(_addButton);

            Grid.SetRow(inputPanel, 3);
            grid.Children.Add(inputPanel);

            // Action buttons row
            var actionPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Left,
                Margin = new Thickness(0, 0, 0, 8)
            };

            _removeButton = CreateThemedButton("Remove");
            _removeButton.Click += OnRemoveButtonClick;
            _removeButton.IsEnabled = false;
            _removeButton.Margin = new Thickness(0, 0, 8, 0);
            AutomationProperties.SetName(_removeButton, "Remove marketplace");
            actionPanel.Children.Add(_removeButton);

            _refreshButton = CreateThemedButton("Refresh All");
            _refreshButton.Click += OnRefreshButtonClick;
            AutomationProperties.SetName(_refreshButton, "Refresh all marketplaces");
            actionPanel.Children.Add(_refreshButton);

            Grid.SetRow(actionPanel, 4);
            grid.Children.Add(actionPanel);

            // Progress bar
            _progressBar = new ProgressBar
            {
                Height = 4,
                IsIndeterminate = true,
                Visibility = Visibility.Collapsed,
                Margin = new Thickness(0, 0, 0, 8)
            };
            Grid.SetRow(_progressBar, 5);
            grid.Children.Add(_progressBar);

            // Status text
            _statusText = new TextBlock
            {
                FontSize = 11,
                Margin = new Thickness(0, 0, 0, 12)
            };
            _statusText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(_statusText, 6);
            grid.Children.Add(_statusText);

            Content = grid;

            ContentRendered += OnDialogContentRendered;
            Closing += OnDialogClosing;
            SourceInitialized += OnSourceInitialized;
        }

        [DllImport("dwmapi.dll")]
        private static extern int DwmSetWindowAttribute(IntPtr hwnd, int attribute, ref int attributeValue, int attributeSize);

        private void OnSourceInitialized(object sender, EventArgs e)
        {
            try
            {
                ApplyTitleBarTheme();
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
            }
        }

        private void ApplyTitleBarTheme()
        {
            var handle = new WindowInteropHelper(this).Handle;
            if (handle == IntPtr.Zero)
            {
                return;
            }

            if (TryGetResourceColor(EnvironmentColors.ToolWindowBackgroundBrushKey, out var captionColor))
            {
                int captionColorRef = ToColorRef(captionColor);
                _ = DwmSetWindowAttribute(handle, _dwmwaCaptionColor, ref captionColorRef, sizeof(int));
            }

            if (TryGetResourceColor(EnvironmentColors.ToolWindowTextBrushKey, out var textColor))
            {
                int textColorRef = ToColorRef(textColor);
                _ = DwmSetWindowAttribute(handle, _dwmwaTextColor, ref textColorRef, sizeof(int));
            }

            var darkMode = 1;
            _ = DwmSetWindowAttribute(handle, _dwmwaUseImmersiveDarkMode, ref darkMode, sizeof(int));
        }

        private bool TryGetResourceColor(object key, out Color color)
        {
            if (TryFindResource(key) is SolidColorBrush brush)
            {
                color = brush.Color;
                return true;
            }

            color = default;
            return false;
        }

        private static int ToColorRef(Color color)
            => color.R | (color.G << 8) | (color.B << 16);

        private async void OnDialogContentRendered(object sender, EventArgs e)
        {
            await LoadMarketplacesAsync();
        }

        private void OnDialogClosing(object sender, CancelEventArgs e)
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource?.Dispose();
            _loadCancellationTokenSource = null;
        }

        private async Task LoadMarketplacesAsync(bool forceRefresh = false)
        {
            _loadCancellationTokenSource?.Cancel();
            _loadCancellationTokenSource = new CancellationTokenSource();
            var cancellationToken = _loadCancellationTokenSource.Token;

            try
            {
                SetLoading(true, "Loading marketplaces...");
                _marketplaces.Clear();

                var marketplaces = await MarketplaceService.GetAllMarketplacesAsync(forceRefresh, cancellationToken);
                cancellationToken.ThrowIfCancellationRequested();

                foreach (var marketplace in marketplaces)
                {
                    _marketplaces.Add(new MarketplaceListItem(marketplace));
                }

                var clonedCount = 0;
                var totalCount = marketplaces.Count;
                foreach (var m in marketplaces)
                {
                    if (m.IsCloned)
                    {
                        clonedCount++;
                    }
                }

                SetStatus($"{totalCount} marketplaces registered, {clonedCount} cloned");
            }
            catch (OperationCanceledException)
            {
                // Ignore
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                SetStatus($"Error loading marketplaces: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private void OnMarketplaceSelectionChanged(object sender, System.Windows.Controls.SelectionChangedEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            _removeButton.IsEnabled = selected != null && !selected.IsBuiltIn;
        }

        private async void OnAddButtonClick(object sender, RoutedEventArgs e)
        {
            var input = _urlInputTextBox.Text?.Trim();
            if (string.IsNullOrWhiteSpace(input))
            {
                await VS.MessageBox.ShowWarningAsync("Invalid Input", "Please enter a repository in owner/repo format or a GitHub URL.");
                _urlInputTextBox.Focus();
                return;
            }

            SetLoading(true, $"Adding {input}...");

            try
            {
                var (success, error, marketplace) = await MarketplaceService.AddMarketplaceAsync(
                    input,
                    _loadCancellationTokenSource?.Token ?? CancellationToken.None);

                if (success && marketplace != null)
                {
                    _urlInputTextBox.Clear();
                    await LoadMarketplacesAsync();
                    SetStatus($"Added {marketplace.DisplayName}");
                }
                else
                {
                    SetStatus(error ?? "Failed to add marketplace");
                    await VS.MessageBox.ShowWarningAsync("Failed to Add Marketplace", error ?? "Unknown error");
                }
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                SetStatus($"Error: {ex.Message}");
            }
            finally
            {
                SetLoading(false);
            }
        }

        private async void OnRemoveButtonClick(object sender, RoutedEventArgs e)
        {
            var selected = _marketplaceList.SelectedItem as MarketplaceListItem;
            if (selected == null || selected.IsBuiltIn)
            {
                return;
            }

            var confirm = await VS.MessageBox.ShowConfirmAsync(
                "Remove Marketplace",
                $"Remove '{selected.DisplayName}' from your registered marketplaces?\n\nThis will also delete the local clone.");

            if (!confirm)
            {
                return;
            }

            try
            {
                MarketplaceService.RemoveMarketplace(selected.Owner, selected.RepoName, deleteClone: true);
                await LoadMarketplacesAsync();
                SetStatus($"Removed {selected.DisplayName}");
            }
            catch (Exception ex)
            {
                _ = ex.LogAsync();
                SetStatus($"Error: {ex.Message}");
            }
        }

        private async void OnRefreshButtonClick(object sender, RoutedEventArgs e)
        {
            await LoadMarketplacesAsync(forceRefresh: true);
        }

        private void SetLoading(bool isLoading, string message = null)
        {
            _progressBar.Visibility = isLoading ? Visibility.Visible : Visibility.Collapsed;
            _addButton.IsEnabled = !isLoading;
            _refreshButton.IsEnabled = !isLoading;

            if (!string.IsNullOrEmpty(message))
            {
                SetStatus(message);
            }
        }

        private void SetStatus(string message)
        {
            _statusText.Text = message;
        }

        private static Button CreateThemedButton(string content)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 75,
                Height = 23,
                Padding = new Thickness(8, 0, 8, 0)
            };
            button.SetResourceReference(Button.StyleProperty, VsResourceKeys.ButtonStyleKey);
            return button;
        }

        private sealed class MarketplaceListItem
        {
            public string Id { get; }
            public string DisplayName { get; }
            public string Owner { get; }
            public string RepoName { get; }
            public bool IsBuiltIn { get; }
            public bool IsCloned { get; }
            public string ErrorMessage { get; }

            public string DisplayText
                {
                    get
                    {
                        var suffix = IsBuiltIn ? " (built-in)" : "";
                        var status = IsCloned ? "" : " [not cloned]";
                        if (!string.IsNullOrEmpty(ErrorMessage))
                        {
                            status = $" [error: {ErrorMessage}]";
                        }
                        return $"{Id}{suffix}{status}";
                    }
                }

            public MarketplaceListItem(MarketplaceInfo marketplace)
            {
                Id = marketplace.Id;
                DisplayName = marketplace.DisplayName ?? marketplace.Id;
                Owner = marketplace.Owner;
                RepoName = marketplace.RepoName;
                IsBuiltIn = marketplace.IsBuiltIn;
                IsCloned = marketplace.IsCloned;
                ErrorMessage = marketplace.ErrorMessage;
            }
        }
    }
}
