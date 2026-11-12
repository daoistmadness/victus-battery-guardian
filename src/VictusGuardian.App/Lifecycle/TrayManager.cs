using System;
using System.IO;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using Avalonia.Threading;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.App.ViewModels;
using VictusGuardian.Core.Configuration;
using VictusGuardian.Core.Models;

namespace VictusGuardian.App.Lifecycle;

public interface ITrayManager : IDisposable
{
    bool IsTrayAvailable { get; }
    bool IsWindowHidden { get; }
    void Initialize(Window mainWindow, MainWindowViewModel viewModel);
    void UpdateStatus(BatteryReading reading);
    void HideToTray();
    void RestoreFromTray();
    void SetTrayVisible(bool visible);
}

public class TrayManager : ITrayManager
{
    private readonly ISettingsService _settingsService;
    private TrayIcon? _trayIcon;
    private Window? _mainWindow;
    private MainWindowViewModel? _viewModel;
    private NativeMenuItem? _statusMenuItem;
    private bool _isExplicitExit;
    private bool _isWindowHidden;
    private string _lastTooltip = string.Empty;

    public bool IsTrayAvailable => _trayIcon != null;
    public bool IsWindowHidden => _isWindowHidden;

    public TrayManager(ISettingsService settingsService)
    {
        _settingsService = settingsService;
    }

    public void Initialize(Window mainWindow, MainWindowViewModel viewModel)
    {
        _mainWindow = mainWindow;
        _viewModel = viewModel;

        try
        {
            _trayIcon = new TrayIcon
            {
                ToolTipText = "Victus Battery Guardian - Initializing...",
                IsVisible = _settingsService.CurrentSettings.ShowTrayIcon
            };

            // Load window icon from embedded asset or fallback stream
            try
            {
                var uri = new Uri("avares://VictusGuardian.App/Assets/app-icon.ico");
                if (AssetLoader.Exists(uri))
                {
                    using var stream = AssetLoader.Open(uri);
                    _trayIcon.Icon = new WindowIcon(stream);
                    _mainWindow.Icon = new WindowIcon(stream);
                }
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Failed to load tray icon asset: {ex.Message}");
            }

            // Build Native Context Menu
            var menu = new NativeMenu();

            var openItem = new NativeMenuItem("Open Dashboard");
            openItem.Click += (_, _) => RestoreFromTray();
            menu.Add(openItem);

            _statusMenuItem = new NativeMenuItem("Battery: Detecting...");
            _statusMenuItem.IsEnabled = false;
            menu.Add(_statusMenuItem);

            var refreshItem = new NativeMenuItem("Refresh Now");
            refreshItem.Click += (_, _) =>
            {
                if (_viewModel != null)
                {
                    _ = _viewModel.Dashboard.RefreshTelemetryAsync();
                }
            };
            menu.Add(refreshItem);

            var settingsItem = new NativeMenuItem("Settings");
            settingsItem.Click += (_, _) =>
            {
                RestoreFromTray();
                if (_viewModel != null)
                {
                    _ = _viewModel.NavigateToAsync("Settings");
                }
            };
            menu.Add(settingsItem);

            menu.Add(new NativeMenuItemSeparator());

            var exitItem = new NativeMenuItem("Exit Victus Guardian");
            exitItem.Click += (_, _) => ExitApplication();
            menu.Add(exitItem);

            _trayIcon.Menu = menu;

            // Left-click restores the window
            _trayIcon.Clicked += (_, _) => RestoreFromTray();

            // Register tray icon with application
            var app = Application.Current;
            if (app != null)
            {
                var icons = TrayIcon.GetIcons(app);
                if (icons == null)
                {
                    icons = new TrayIcons();
                    TrayIcon.SetIcons(app, icons);
                }
                icons.Add(_trayIcon);
            }

            AppLogger.LogInfo("TrayIcon initialized successfully.");
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to initialize system tray icon: {ex.Message}");
            _trayIcon = null;
        }

        // Intercept window closing and state changing
        _mainWindow.Closing += OnWindowClosing;
        _mainWindow.PropertyChanged += OnWindowPropertyChanged;
    }

    private void OnWindowClosing(object? sender, WindowClosingEventArgs e)
    {
        if (_isExplicitExit)
        {
            return;
        }

        if (_settingsService.CurrentSettings.CloseToTray && IsTrayAvailable && _settingsService.CurrentSettings.ShowTrayIcon)
        {
            e.Cancel = true;
            HideToTray();
        }
    }

    private void OnWindowPropertyChanged(object? sender, AvaloniaPropertyChangedEventArgs e)
    {
        if (e.Property == Window.WindowStateProperty)
        {
            var state = (WindowState)e.NewValue!;
            if (state == WindowState.Minimized && _settingsService.CurrentSettings.MinimizeToTray && IsTrayAvailable && _settingsService.CurrentSettings.ShowTrayIcon)
            {
                HideToTray();
            }
        }
    }

    public void HideToTray()
    {
        if (_mainWindow == null || !IsTrayAvailable || !_settingsService.CurrentSettings.ShowTrayIcon)
        {
            return;
        }

        Dispatcher.UIThread.Post(() =>
        {
            _isWindowHidden = true;
            _mainWindow.Hide();
            _viewModel?.Dashboard.SetWindowVisible(false);
            AppLogger.LogInfo("MainWindow hidden to system tray.");
        });
    }

    public void RestoreFromTray()
    {
        if (_mainWindow == null) return;

        Dispatcher.UIThread.Post(() =>
        {
            _isWindowHidden = false;
            if (!_mainWindow.IsVisible)
            {
                _mainWindow.Show();
            }
            if (_mainWindow.WindowState == WindowState.Minimized)
            {
                _mainWindow.WindowState = WindowState.Normal;
            }
            _mainWindow.Activate();
            _mainWindow.Focus();

            _viewModel?.Dashboard.SetWindowVisible(true);
            if (_viewModel != null)
            {
                _ = _viewModel.Dashboard.RefreshTelemetryAsync();
            }
            AppLogger.LogInfo("MainWindow restored from system tray.");
        });
    }

    public void UpdateStatus(BatteryReading reading)
    {
        if (_trayIcon == null) return;

        string tooltip;
        string menuStatus;

        if (!reading.BatteryPercentage.HasValue)
        {
            tooltip = "Victus Guardian — Telemetry Unavailable";
            menuStatus = "Battery: Unavailable";
        }
        else
        {
            var pct = reading.BatteryPercentage.Value;
            var stateStr = reading.ChargingState.HasValue
                ? reading.ChargingState.Value.ToString()
                : (reading.IsAcConnected ? "Holding Charge" : "Discharging");
            tooltip = $"Victus Guardian — {pct:0}% | {stateStr}";
            menuStatus = $"Battery: {pct:0}% ({stateStr})";
        }

        // Avoid continuous property mutations if state has not changed
        if (tooltip != _lastTooltip)
        {
            _lastTooltip = tooltip;
            Dispatcher.UIThread.Post(() =>
            {
                if (_trayIcon != null)
                {
                    _trayIcon.ToolTipText = tooltip;
                }
                if (_statusMenuItem != null)
                {
                    _statusMenuItem.Header = menuStatus;
                }
            });
        }
    }

    public void SetTrayVisible(bool visible)
    {
        if (_trayIcon != null)
        {
            _trayIcon.IsVisible = visible;
        }
    }

    public void ExitApplication()
    {
        _isExplicitExit = true;
        AppLogger.LogInfo("Exit initiated from system tray menu.");

        Dispatcher.UIThread.Post(() =>
        {
            Dispose();
            if (Application.Current?.ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
            {
                desktop.Shutdown(0);
            }
        });
    }

    public void Dispose()
    {
        if (_trayIcon != null)
        {
            try
            {
                _trayIcon.IsVisible = false;
                _trayIcon.Dispose();
            }
            catch
            {
            }
            _trayIcon = null;
        }

        if (_mainWindow != null)
        {
            _mainWindow.Closing -= OnWindowClosing;
            _mainWindow.PropertyChanged -= OnWindowPropertyChanged;
            _mainWindow = null;
        }
    }
}
