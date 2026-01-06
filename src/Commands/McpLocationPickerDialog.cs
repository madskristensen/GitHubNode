using System.Collections.Generic;
using System.Windows;
using System.Windows.Controls;
using System.Windows.Interop;
using GitHubNode.Services;
using Microsoft.VisualStudio.PlatformUI;

namespace GitHubNode.Commands
{
    /// <summary>
    /// A dialog for picking an MCP configuration file location.
    /// Shows all 5 possible locations with descriptions.
    /// </summary>
    internal sealed class McpLocationPickerDialog : DialogWindow
    {
        private readonly ListBox _locationListBox;
        private readonly List<McpConfigLocation> _locations;

        /// <summary>
        /// Gets the selected location, or null if cancelled.
        /// </summary>
        public McpConfigLocation SelectedLocation { get; private set; }

        public McpLocationPickerDialog(string solutionDirectory)
        {
            _locations = McpConfigService.GetAllLocations(solutionDirectory);

            Title = "Add MCP Configuration";
            Width = 500;
            Height = 350;
            MinWidth = 400;
            MinHeight = 300;
            ResizeMode = ResizeMode.CanResizeWithGrip;
            HasMaximizeButton = false;
            HasMinimizeButton = false;
            HasHelpButton = false;
            ShowInTaskbar = false;

            // Set the owner to VS main window for proper centering
            ThreadHelper.ThrowIfNotOnUIThread();
            if (Package.GetGlobalService(typeof(Microsoft.VisualStudio.Shell.Interop.SDTE)) is EnvDTE.DTE dte)
            {
                var hwnd = (System.IntPtr)dte.MainWindow.HWnd;
                if (hwnd != System.IntPtr.Zero)
                {
                    Owner = HwndSource.FromHwnd(hwnd)?.RootVisual as Window;
                }
            }
            WindowStartupLocation = WindowStartupLocation.CenterOwner;

            // Apply VS theme colors
            SetResourceReference(BackgroundProperty, EnvironmentColors.ToolWindowBackgroundBrushKey);

            var grid = new Grid();
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Prompt
            grid.RowDefinitions.Add(new RowDefinition { Height = new GridLength(1, GridUnitType.Star) }); // List
            grid.RowDefinitions.Add(new RowDefinition { Height = GridLength.Auto }); // Buttons
            grid.Margin = new Thickness(12);

            // Prompt label
            var label = new TextBlock
            {
                Text = "Select where to create the MCP configuration file:",
                Margin = new Thickness(0, 0, 0, 8),
                TextWrapping = TextWrapping.Wrap
            };
            label.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
            Grid.SetRow(label, 0);
            grid.Children.Add(label);

            // Location list
            _locationListBox = new ListBox
            {
                Margin = new Thickness(0, 0, 0, 12),
                BorderThickness = new Thickness(1)
            };
            _locationListBox.SetResourceReference(ListBox.BackgroundProperty, EnvironmentColors.ComboBoxBackgroundBrushKey);
            _locationListBox.SetResourceReference(ListBox.ForegroundProperty, EnvironmentColors.ComboBoxTextBrushKey);
            _locationListBox.SetResourceReference(ListBox.BorderBrushProperty, EnvironmentColors.ComboBoxBorderBrushKey);

            foreach (var location in _locations)
            {
                var itemPanel = new StackPanel { Margin = new Thickness(4) };

                var headerPanel = new StackPanel { Orientation = Orientation.Horizontal };

                var nameText = new TextBlock
                {
                    Text = location.DisplayName,
                    FontWeight = FontWeights.SemiBold
                };
                nameText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.ToolWindowTextBrushKey);
                headerPanel.Children.Add(nameText);

                if (location.Exists)
                {
                    var existsText = new TextBlock
                    {
                        Text = " (exists)",
                        FontStyle = FontStyles.Italic,
                        Margin = new Thickness(4, 0, 0, 0)
                    };
                    existsText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                    headerPanel.Children.Add(existsText);
                }

                if (location.IsSourceControlled)
                {
                    var scText = new TextBlock
                    {
                        Text = " - Source Controlled",
                        Margin = new Thickness(4, 0, 0, 0)
                    };
                    scText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGreenTextBrushKey);
                    headerPanel.Children.Add(scText);
                }

                itemPanel.Children.Add(headerPanel);

                var descText = new TextBlock
                {
                    Text = location.Description,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                descText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                itemPanel.Children.Add(descText);

                var pathText = new TextBlock
                {
                    Text = location.FilePath,
                    FontSize = 10,
                    TextWrapping = TextWrapping.Wrap,
                    Margin = new Thickness(0, 2, 0, 0)
                };
                pathText.SetResourceReference(TextBlock.ForegroundProperty, EnvironmentColors.SystemGrayTextBrushKey);
                itemPanel.Children.Add(pathText);

                var listBoxItem = new ListBoxItem { Content = itemPanel, Tag = location };
                _locationListBox.Items.Add(listBoxItem);
            }

            // Select first item
            if (_locationListBox.Items.Count > 0)
            {
                _locationListBox.SelectedIndex = 0;
            }

            // Handle double-click
            _locationListBox.MouseDoubleClick += (s, e) =>
            {
                if (_locationListBox.SelectedItem != null)
                {
                    OnOkClicked();
                }
            };

            Grid.SetRow(_locationListBox, 1);
            grid.Children.Add(_locationListBox);

            // Button panel
            var buttonPanel = new StackPanel
            {
                Orientation = Orientation.Horizontal,
                HorizontalAlignment = HorizontalAlignment.Right
            };

            var okButton = CreateThemedButton("OK", isDefault: true);
            okButton.Margin = new Thickness(0, 0, 8, 0);
            okButton.Click += (s, e) => OnOkClicked();
            buttonPanel.Children.Add(okButton);

            var cancelButton = CreateThemedButton("Cancel", isCancel: true);
            cancelButton.Click += (s, e) =>
            {
                DialogResult = false;
                Close();
            };
            buttonPanel.Children.Add(cancelButton);

            Grid.SetRow(buttonPanel, 2);
            grid.Children.Add(buttonPanel);

            Content = grid;
        }

        private void OnOkClicked()
        {
            if (_locationListBox.SelectedItem is ListBoxItem selectedItem &&
                selectedItem.Tag is McpConfigLocation location)
            {
                SelectedLocation = location;
                DialogResult = true;
                Close();
            }
        }

        private static Button CreateThemedButton(string content, bool isDefault = false, bool isCancel = false)
        {
            var button = new Button
            {
                Content = content,
                MinWidth = 75,
                Height = 23,
                Padding = new Thickness(8, 0, 8, 0),
                IsDefault = isDefault,
                IsCancel = isCancel
            };
            button.SetResourceReference(Button.StyleProperty, VsResourceKeys.ButtonStyleKey);
            return button;
        }
    }
}
