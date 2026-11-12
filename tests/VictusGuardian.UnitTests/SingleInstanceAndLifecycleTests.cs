using System;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using VictusGuardian.App.Diagnostics;
using VictusGuardian.App.Lifecycle;
using Xunit;

namespace VictusGuardian.UnitTests;

public class SingleInstanceAndLifecycleTests
{
    [Fact]
    public void SingleInstanceGuard_FirstInstance_AcquiresGuardSuccessfully()
    {
        var testId = $"T_{Guid.NewGuid():N}"[..10];
        using var guard = new SingleInstanceGuard(testId);

        var acquired = guard.TryAcquire();

        Assert.True(acquired);
        Assert.True(guard.HasAcquired);
    }

    [Fact]
    public void SingleInstanceGuard_SimultaneousDoubleLaunches_SecondInstanceFailsToAcquire()
    {
        var testId = $"T_{Guid.NewGuid():N}"[..10];
        var firstAcquiredEvent = new ManualResetEventSlim(false);
        var releaseFirstEvent = new ManualResetEventSlim(false);
        bool firstAcquired = false;
        bool secondAcquired = true;

        // In .NET, Mutex ownership is thread-affine. Hold the first instance on an active owner thread.
        var thread = new Thread(() =>
        {
            using var firstInstance = new SingleInstanceGuard(testId);
            firstAcquired = firstInstance.TryAcquire();
            firstAcquiredEvent.Set();
            releaseFirstEvent.Wait();
        }) { IsBackground = true };

        thread.Start();
        firstAcquiredEvent.Wait();
        Assert.True(firstAcquired);

        using (var secondInstance = new SingleInstanceGuard(testId))
        {
            secondAcquired = secondInstance.TryAcquire(TimeSpan.FromMilliseconds(50));
        }

        releaseFirstEvent.Set();
        thread.Join();

        Assert.False(secondAcquired);
    }

    [Fact]
    public async Task SingleInstanceGuard_ExistingWindowActivation_SignalsAndReceivesCommand()
    {
        var testId = $"T_{Guid.NewGuid():N}"[..10];
        var firstAcquiredEvent = new ManualResetEventSlim(false);
        var releaseFirstEvent = new ManualResetEventSlim(false);
        var activationReceived = new TaskCompletionSource<bool>();
        bool firstAcquired = false;

        var thread = new Thread(() =>
        {
            using var firstInstance = new SingleInstanceGuard(testId);
            firstAcquired = firstInstance.TryAcquire();
            firstInstance.StartActivationServer(() =>
            {
                activationReceived.TrySetResult(true);
            });
            firstAcquiredEvent.Set();
            releaseFirstEvent.Wait();
        }) { IsBackground = true };

        thread.Start();
        firstAcquiredEvent.Wait();
        Assert.True(firstAcquired);

        // Allow named pipe server a moment to start listening
        await Task.Delay(100);

        using (var secondInstance = new SingleInstanceGuard(testId))
        {
            var secondAcquired = secondInstance.TryAcquire(TimeSpan.FromMilliseconds(50));
            Assert.False(secondAcquired);

            var signaled = secondInstance.SignalExistingInstance(TimeSpan.FromSeconds(2));
            Assert.True(signaled);
        }

        var completed = await Task.WhenAny(activationReceived.Task, Task.Delay(3000));
        Assert.Same(activationReceived.Task, completed);
        Assert.True(await activationReceived.Task);

        releaseFirstEvent.Set();
        thread.Join();
    }

    [Fact]
    public void SingleInstanceGuard_AfterDispose_NextInstanceCanAcquire()
    {
        var testId = $"T_{Guid.NewGuid():N}"[..10];
        using (var firstInstance = new SingleInstanceGuard(testId))
        {
            Assert.True(firstInstance.TryAcquire());
        } // Disposed here, releasing mutex

        using var secondInstance = new SingleInstanceGuard(testId);
        var acquired = secondInstance.TryAcquire(TimeSpan.FromMilliseconds(100));

        Assert.True(acquired);
        Assert.True(secondInstance.HasAcquired);
    }

    [Fact]
    public void SingleInstanceGuard_SignalWhenNoServer_DoesNotThrowAndReturnsFalse()
    {
        var testId = $"T_{Guid.NewGuid():N}"[..10];
        using var guard = new SingleInstanceGuard(testId);

        // No server is running
        var signaled = guard.SignalExistingInstance(TimeSpan.FromMilliseconds(100));

        Assert.False(signaled);
    }
}
