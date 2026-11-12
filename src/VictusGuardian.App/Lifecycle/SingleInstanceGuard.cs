using System;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.App.Diagnostics;

namespace VictusGuardian.App.Lifecycle;

public sealed class SingleInstanceGuard : IDisposable
{
    private readonly string _mutexName;
    private readonly string _pipeName;
    private Mutex? _mutex;
    private bool _hasAcquired;
    private CancellationTokenSource? _pipeCts;
    private Task? _pipeServerTask;
    private bool _isDisposed;

    public const string ActivationCommand = "ACTIVATE";

    public SingleInstanceGuard(string? applicationIdentifier = null)
    {
        var baseIdentifier = string.IsNullOrWhiteSpace(applicationIdentifier)
            ? "VictusGuardian"
            : applicationIdentifier;

        var userName = SanitizeIdentifier(Environment.UserName);

        _mutexName = OperatingSystem.IsWindows()
            ? $"Local\\{baseIdentifier}_{userName}_SingleInstance"
            : $"{baseIdentifier}_{userName}_SingleInstance";

        // Windows named pipes have no 108-character UNIX domain socket path restriction.
        // On Linux/Unix, .NET places socket files in temp dirs, so keep pipe name compact.
        var shortUser = userName.Length > 8 ? userName[..8] : userName;
        var shortApp = baseIdentifier.Length > 12 ? baseIdentifier[..12] : baseIdentifier;

        _pipeName = OperatingSystem.IsWindows()
            ? $"{baseIdentifier}_{userName}_Activation"
            : $"{shortApp}_{shortUser}_act";
    }

    public string MutexName => _mutexName;
    public string PipeName => _pipeName;
    public bool HasAcquired => _hasAcquired;

    public bool TryAcquire(TimeSpan timeout = default)
    {
        if (_isDisposed) throw new ObjectDisposedException(nameof(SingleInstanceGuard));

        try
        {
            _mutex = new Mutex(true, _mutexName, out bool createdNew);
            if (createdNew)
            {
                _hasAcquired = true;
                return true;
            }

            if (timeout > TimeSpan.Zero)
            {
                _hasAcquired = _mutex.WaitOne(timeout, false);
                return _hasAcquired;
            }

            _hasAcquired = false;
            return false;
        }
        catch (AbandonedMutexException)
        {
            // Previous instance crashed or was killed without releasing the mutex.
            // OS transfers ownership to this instance.
            AppLogger.LogWarning("Acquired single-instance mutex after abandoned ownership from crashed instance.");
            _hasAcquired = true;
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Failed to acquire single instance mutex '{_mutexName}': {ex.Message}");
            // If mutex cannot be created due to permission limits, fail open to avoid preventing application launch
            _hasAcquired = true;
            return true;
        }
    }

    public bool SignalExistingInstance(TimeSpan timeout = default)
    {
        if (timeout == default) timeout = TimeSpan.FromSeconds(2);

        try
        {
            using var client = new NamedPipeClientStream(".", _pipeName, PipeDirection.Out);
            client.Connect((int)timeout.TotalMilliseconds);
            using var writer = new StreamWriter(client, Encoding.UTF8) { AutoFlush = true };
            writer.WriteLine(ActivationCommand);
            return true;
        }
        catch (Exception ex)
        {
            AppLogger.LogWarning($"Could not signal existing instance on pipe '{_pipeName}': {ex.Message}");
            return false;
        }
    }

    public void StartActivationServer(Action onActivate)
    {
        if (!_hasAcquired || _isDisposed) return;

        _pipeCts = new CancellationTokenSource();
        var token = _pipeCts.Token;

        _pipeServerTask = Task.Run(async () =>
        {
            while (!token.IsCancellationRequested)
            {
                try
                {
                    await using var server = new NamedPipeServerStream(
                        _pipeName,
                        PipeDirection.In,
                        NamedPipeServerStream.MaxAllowedServerInstances,
                        PipeTransmissionMode.Byte,
                        PipeOptions.Asynchronous);

                    await server.WaitForConnectionAsync(token);

                    using var reader = new StreamReader(server, Encoding.UTF8);
                    var line = await reader.ReadLineAsync(token);

                    // Strictly validate command: only allow exact ActivationCommand
                    if (string.Equals(line?.Trim(), ActivationCommand, StringComparison.Ordinal))
                    {
                        onActivate();
                    }
                    else
                    {
                        AppLogger.LogWarning($"Rejected unauthorized IPC command: '{line}'");
                    }
                }
                catch (OperationCanceledException)
                {
                    break;
                }
                catch (Exception ex)
                {
                    if (token.IsCancellationRequested) break;
                    AppLogger.LogWarning($"Named pipe server loop encountered transient error: {ex.Message}");
                    try
                    {
                        await Task.Delay(250, token);
                    }
                    catch (OperationCanceledException)
                    {
                        break;
                    }
                }
            }
        }, token);
    }

    public void Dispose()
    {
        if (_isDisposed) return;
        _isDisposed = true;

        try
        {
            _pipeCts?.Cancel();
            _pipeCts?.Dispose();
            _pipeCts = null;
        }
        catch
        {
            // Non-fatal
        }

        if (_hasAcquired && _mutex != null)
        {
            try
            {
                _mutex.ReleaseMutex();
            }
            catch (Exception ex)
            {
                AppLogger.LogWarning($"Error releasing mutex during disposal: {ex.Message}");
            }
        }

        _mutex?.Dispose();
        _mutex = null;
        _hasAcquired = false;
        GC.SuppressFinalize(this);
    }

    private static string SanitizeIdentifier(string input)
    {
        if (string.IsNullOrWhiteSpace(input)) return "DefUser";
        var sb = new StringBuilder();
        foreach (var c in input)
        {
            if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                sb.Append(c);
        }
        return sb.Length > 0 ? sb.ToString() : "DefUser";
    }
}
