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

        // Cached values to avoid building a ShellSettingsManager on every Solution Explorer
        // call. The cache is invalidated by the corresponding setters.
        private static bool? _cachedMcpServersEnabled;
        private static bool? _cachedGitHubNodeEnabled;

        public static bool IsMcpServersEnabled()
        {
            if (_cachedMcpServersEnabled.HasValue)
            {
                return _cachedMcpServersEnabled.Value;
            }

            try
            {
                var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                SettingsStore store = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

                bool value;
                if (!store.CollectionExists(_collectionPath) ||
                    !store.PropertyExists(_collectionPath, _showMcpServersProperty))
                {
                    value = false;
                }
                else
                {
                    value = store.GetBoolean(_collectionPath, _showMcpServersProperty);
                }

                _cachedMcpServersEnabled = value;
                return value;
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
                _cachedMcpServersEnabled = enabled;
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
                if (_cachedGitHubNodeEnabled.HasValue)
                {
                    return _cachedGitHubNodeEnabled.Value;
                }

                try
                {
                    var settingsManager = new ShellSettingsManager(ServiceProvider.GlobalProvider);
                    SettingsStore store = settingsManager.GetReadOnlySettingsStore(SettingsScope.UserSettings);

                    bool value;
                    if (!store.CollectionExists(_collectionPath) ||
                        !store.PropertyExists(_collectionPath, _showGitHubNodeProperty))
                    {
                        value = true;
                    }
                    else
                    {
                        value = store.GetBoolean(_collectionPath, _showGitHubNodeProperty);
                    }

                    _cachedGitHubNodeEnabled = value;
                    return value;
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
                    _cachedGitHubNodeEnabled = enabled;
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
