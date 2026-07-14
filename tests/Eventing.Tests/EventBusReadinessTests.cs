using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using RealtimePix.Eventing;
using Xunit;

namespace Eventing.Tests;

public sealed class EventBusReadinessTests : IDisposable
{
    private readonly string _root = Path.Combine(Path.GetTempPath(), $"realtime-pix-readiness-{Guid.NewGuid():N}");

    [Fact]
    public async Task File_transport_is_ready_when_its_directory_is_writable()
    {
        var probe = CreateProbe(Path.Combine(_root, "event-bus"));

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.True(result.IsReady);
        Assert.Equal("File", result.Provider);
    }

    [Fact]
    public async Task File_transport_is_not_ready_when_its_directory_path_is_a_file()
    {
        Directory.CreateDirectory(_root);
        var filePath = Path.Combine(_root, "not-a-directory");
        await File.WriteAllTextAsync(filePath, "occupied");
        var probe = CreateProbe(filePath);

        var result = await probe.CheckAsync(CancellationToken.None);

        Assert.False(result.IsReady);
        Assert.Equal("file-transport-unavailable", result.Reason);
    }

    public void Dispose()
    {
        if (Directory.Exists(_root))
        {
            Directory.Delete(_root, recursive: true);
        }

        GC.SuppressFinalize(this);
    }

    private static FileEventBusReadinessProbe CreateProbe(string directory) =>
        new(
            Options.Create(new FileEventBusOptions { Directory = directory }),
            NullLogger<FileEventBusReadinessProbe>.Instance);
}
