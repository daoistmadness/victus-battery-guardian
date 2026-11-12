using System;
using System.IO;
using System.Text;
using System.Text.RegularExpressions;
using System.Threading.Tasks;

namespace VictusGuardian.App.Diagnostics;

public enum LogLevel
{
    Debug,
    Info,
    Warning,
    Error,
    Fatal
}

public static class AppLogger
{
    private static readonly object _syncLock = new();
    private static string? _logDirectory;
    private static string? _currentLogFilePath;
    private static bool _isInitialized;
    private const long MaxFileSizeBytes = 2 * 1024 * 1024; // 2 MB
    private const int MaxBackupCount = 5;

    private static readonly Regex SerialNumberMaskRegex = new(
        @"(serial|serialnumber|sn)[:= ]+([A-Za-z0-9_-]{4,})",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

    public static void Initialize(string? customLogDirectory = null)
    {
        lock (_syncLock)
        {
            if (_isInitialized && customLogDirectory == null) return;

            try
            {
                if (!string.IsNullOrWhiteSpace(customLogDirectory))
                {
                    _logDirectory = customLogDirectory;
                }
                else
                {
                    var baseAppDir = Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData);
                    _logDirectory = Path.Combine(baseAppDir, "VictusGuardian", "logs");
                }

                if (!Directory.Exists(_logDirectory))
                {
                    Directory.CreateDirectory(_logDirectory);
                }

                _currentLogFilePath = Path.Combine(_logDirectory, "startup.log");
                RotateLogsIfNeeded();

                // Hook unhandled exception handlers only once
                if (!_isInitialized)
                {
                    AppDomain.CurrentDomain.UnhandledException += OnUnhandledException;
                    TaskScheduler.UnobservedTaskException += OnUnobservedTaskException;
                }

                _isInitialized = true;
            }
            catch (Exception ex)
            {
                // Fallback to console if file logging setup encounters permissions error
                Console.Error.WriteLine($"[AppLogger.Initialize Error] {ex.Message}");
            }
        }
    }

    public static string GetLogDirectory()
    {
        if (_logDirectory == null) Initialize();
        return _logDirectory!;
    }

    public static string GetLogFilePath()
    {
        if (_currentLogFilePath == null) Initialize();
        return _currentLogFilePath!;
    }

    public static void Log(string message, LogLevel level = LogLevel.Info)
    {
        lock (_syncLock)
        {
            if (!_isInitialized)
            {
                Initialize();
            }

            try
            {
                var sanitized = Sanitize(message);
                var timestamp = DateTimeOffset.Now.ToString("yyyy-MM-dd HH:mm:ss.fff zzz");
                var pid = Environment.ProcessId;
                var logLine = $"[{timestamp}] [{level.ToString().ToUpperInvariant(),-7}] [PID {pid,6}] {sanitized}";

                if (_currentLogFilePath != null)
                {
                    RotateLogsIfNeeded();
                    File.AppendAllText(_currentLogFilePath, logLine + Environment.NewLine, Encoding.UTF8);
                }

                // Also output to console/trace for dev/debugger
                if (level >= LogLevel.Warning)
                {
                    Console.Error.WriteLine(logLine);
                }
                else
                {
                    Console.WriteLine(logLine);
                }
            }
            catch
            {
                // Never allow logging failures to crash the application
            }
        }
    }

    public static void LogDebug(string message) => Log(message, LogLevel.Debug);
    public static void LogInfo(string message) => Log(message, LogLevel.Info);
    public static void LogWarning(string message) => Log(message, LogLevel.Warning);
    public static void LogError(string message) => Log(message, LogLevel.Error);
    public static void LogFatal(string message) => Log(message, LogLevel.Fatal);

    public static string Sanitize(string message)
    {
        if (string.IsNullOrEmpty(message)) return string.Empty;

        // Mask any serial number / hardware identifier patterns
        return SerialNumberMaskRegex.Replace(message, "$1: [REDACTED]");
    }

    private static void RotateLogsIfNeeded()
    {
        if (_currentLogFilePath == null || !File.Exists(_currentLogFilePath)) return;

        try
        {
            var fileInfo = new FileInfo(_currentLogFilePath);
            if (fileInfo.Length < MaxFileSizeBytes) return;

            // Delete oldest backup if it exists
            var oldestBackup = Path.Combine(_logDirectory!, $"startup.{MaxBackupCount}.log");
            if (File.Exists(oldestBackup))
            {
                File.Delete(oldestBackup);
            }

            // Shift older backups down
            for (int i = MaxBackupCount - 1; i >= 1; i--)
            {
                var src = Path.Combine(_logDirectory!, $"startup.{i}.log");
                var dst = Path.Combine(_logDirectory!, $"startup.{i + 1}.log");
                if (File.Exists(src))
                {
                    File.Move(src, dst, true);
                }
            }

            // Move current to .1
            var firstBackup = Path.Combine(_logDirectory!, "startup.1.log");
            File.Move(_currentLogFilePath, firstBackup, true);
        }
        catch
        {
            // Non-fatal rotation error
        }
    }

    private static void OnUnhandledException(object sender, UnhandledExceptionEventArgs e)
    {
        if (e.ExceptionObject is Exception ex)
        {
            LogFatal($"AppDomain Unhandled Exception (IsTerminating={e.IsTerminating}): {ex}");
        }
        else
        {
            LogFatal($"AppDomain Unhandled Exception (IsTerminating={e.IsTerminating}): {e.ExceptionObject}");
        }
    }

    private static void OnUnobservedTaskException(object? sender, UnobservedTaskExceptionEventArgs e)
    {
        LogError($"TaskScheduler Unobserved Task Exception: {e.Exception}");
    }
}
