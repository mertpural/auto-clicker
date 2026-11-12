using AutoClicker.App.Services;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace AutoClicker.Tests;

public sealed class SingleInstanceTests
{
    [Fact]
    public async Task Explicit_smoke_instances_use_isolated_identity_and_still_share_their_own_lock()
    {
        var directory = Path.Combine(Path.GetTempPath(), "autoclicker-isolation-" + Guid.NewGuid().ToString("N"));
        try
        {
            await using var first = new SingleInstanceService(Path.Combine(directory, "first"), NullLogger<SingleInstanceService>.Instance, true);
            await using var other = new SingleInstanceService(Path.Combine(directory, "other"), NullLogger<SingleInstanceService>.Instance, true);
            await using var duplicate = new SingleInstanceService(Path.Combine(directory, "first"), NullLogger<SingleInstanceService>.Instance, true);
            Assert.True(first.TryAcquire()); Assert.True(other.TryAcquire()); Assert.False(duplicate.TryAcquire());
        }
        finally { if (Directory.Exists(directory)) Directory.Delete(directory, true); }
    }

    [Fact]
    public async Task Second_owner_forwards_activation_to_the_first_instance()
    {
        var directory = Path.Combine(Path.GetTempPath(), "autoclicker-test-" + Guid.NewGuid().ToString("N"));
        Directory.CreateDirectory(directory);
        try
        {
            await using var first = new SingleInstanceService(directory, NullLogger<SingleInstanceService>.Instance);
            await using var second = new SingleInstanceService(directory, NullLogger<SingleInstanceService>.Instance);
            Assert.True(first.TryAcquire());
            Assert.False(second.TryAcquire());
            var activated = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
            var listener = first.RunActivationListenerAsync(() => { activated.TrySetResult(); return Task.CompletedTask; });

            Assert.True(await second.RequestActivationAsync().WaitAsync(TimeSpan.FromSeconds(3)));
            await activated.Task.WaitAsync(TimeSpan.FromSeconds(3));
            await first.DisposeAsync();
            await listener.WaitAsync(TimeSpan.FromSeconds(3));
        }
        finally { Directory.Delete(directory, recursive: true); }
    }
}
