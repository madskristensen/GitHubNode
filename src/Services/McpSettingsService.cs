using Microsoft.VisualStudio.Settings;
using Microsoft.VisualStudio.Shell.Settings;
using System.Diagnostics;

namespace GitHubNode.Services
{
    internal static class McpSettingsService
    {
        private const string _collectionPath = "GitHubNode";
        private const string _showMcpServersProperty = "ShowMcpServers";
        private const string _showGitHubNodeProperty = "ShowGitHubNode";

        public static bool IsMcpServersEnabled()
        {
            try
            {
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                SettingsStore store = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

                if (!store.CollectionExists(_collectionPath) ||
                    !store.PropertyExists(_collectionPath, _showMcpServersProperty))
                {
                    return false;
                }

                return store.GetBoolean(_collectionPath, _showMcpServersProperty);
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpSettingsService.IsMcpServersEnabled failed: {ex}");
                return false;
            }
            catch (ArgumentException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpSettingsService.IsMcpServersEnabled failed: {ex}");
                return false;
            }
        }

        public static void SetMcpServersEnabled(bool enabled)
        {
            try
            {
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                WritableSettingsStore store = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                if (!store.CollectionExists(_collectionPath))
                {
                    store.CreateCollection(_collectionPath);
                }

                store.SetBoolean(_collectionPath, _showMcpServersProperty, enabled);
            }
            catch (InvalidOperationException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpSettingsService.SetMcpServersEnabled failed: {ex}");
            }
            catch (ArgumentException ex)
            {
                _ = ex.LogAsync();
                Debug.WriteLine($"McpSettingsService.SetMcpServersEnabled failed: {ex}");
            }
        }
            public static bool IsGitHubNodeEnabled()
            {
                try
                {
                    var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                    SettingsStore store = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

                    if (!store.CollectionExists(_collectionPath) ||
                        !store.PropertyExists(_collectionPath, _showGitHubNodeProperty))
                    {
                        return true;
                    }

                    return store.GetBoolean(_collectionPath, _showGitHubNodeProperty);
                }
                catch (InvalidOperationException ex)
                {
                    _ = ex.LogAsync();
                    Debug.WriteLine($"McpSettingsService.IsGitHubNodeEnabled failed: {ex}");
                    return true;
                }
                catch (ArgumentException ex)
                {
                    _ = ex.LogAsync();
                    Debug.WriteLine($"McpSettingsService.IsGitHubNodeEnabled failed: {ex}");
                    return true;
                }
            }

            public static void SetGitHubNodeEnabled(bool enabled)
            {
                try
                {
                    var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                    WritableSettingsStore store = settingsManager.GetWritableSettingsStore(SettingsScope.UserSettings);

                    if (!store.CollectionExists(_collectionPath))
                    {
                        store.CreateCollection(_collectionPath);
                    }

                    store.SetBoolean(_collectionPath, _showGitHubNodeProperty, enabled);
                }
                catch (InvalidOperationException ex)
                {
                    _ = ex.LogAsync();
                    Debug.WriteLine($"McpSettingsService.SetGitHubNodeEnabled failed: {ex}");
                }
                catch (ArgumentException ex)
                {
                    _ = ex.LogAsync();
                    Debug.WriteLine($"McpSettingsService.SetGitHubNodeEnabled failed: {ex}");
                }
            }
        }
    }
