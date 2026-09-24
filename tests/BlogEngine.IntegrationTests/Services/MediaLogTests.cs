using BlogEngine.IntegrationTests.Infrastructure;
using BlogEngine.Server.Services.Media;
using BlogEngine.Shared.Contracts;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

namespace BlogEngine.IntegrationTests.Services;

/// <summary>
/// Tests the media log events (design 18, T2.13): <c>MediaUploaded</c> and <c>MediaEdited</c> are written as structured
/// events carrying the media id and size.
/// </summary>
[ClassDataSource<BlogEngineWebApplicationFactory>(Shared = SharedType.PerTestSession)]
public class MediaLogTests(BlogEngineWebApplicationFactory factory)
{
    /// <summary>An upload logs MediaUploaded; an edit logs MediaEdited with the new version's size.</summary>
    [Test]
    public async Task UploadAndEdit_WriteStructuredEvents()
    {
        var logger = new CapturingLogger<ServerMediaService>();
        await using var scope = factory.Services.CreateAsyncScope();
        var media = ActivatorUtilities.CreateInstance<ServerMediaService>(scope.ServiceProvider, logger);
        var bytes = MediaTestFiles.Png(80, 40);

        using var content = new MemoryStream(bytes);
        var uploaded = (await media.UploadAsync($"logged-{MediaTestFiles.Token()}.png", content, bytes.Length, CancellationToken.None)).Item!;
        var edited = ((MediaSaved)await media.EditAsync(uploaded.Id, new MediaEditOperations { Rotate = 90 })).Item;

        var upload = logger.Entries.Single(e => e.EventId.Name == "MediaUploaded");
        var edit = logger.Entries.Single(e => e.EventId.Name == "MediaEdited");
        await Assert.That(upload.Level).IsEqualTo(LogLevel.Information);
        await Assert.That(upload.Properties["MediaId"]).IsEqualTo(uploaded.Id);
        await Assert.That(upload.Properties["SizeBytes"]).IsEqualTo(uploaded.SizeBytes);
        await Assert.That(upload.Properties["PublicId"]).IsEqualTo(uploaded.PublicId);
        await Assert.That(edit.Properties["MediaId"]).IsEqualTo(uploaded.Id);
        await Assert.That(edit.Properties["SizeBytes"]).IsEqualTo(edited.SizeBytes);
        await Assert.That(edit.Properties["Version"]).IsEqualTo(2);
        await Assert.That((edit.Properties["Width"], edit.Properties["Height"])).IsEqualTo(((object?)40, (object?)80));
    }

    /// <summary>A rejected upload is logged as a warning with the reason.</summary>
    [Test]
    public async Task RejectedUpload_IsLogged()
    {
        var logger = new CapturingLogger<ServerMediaService>();
        await using var scope = factory.Services.CreateAsyncScope();
        var media = ActivatorUtilities.CreateInstance<ServerMediaService>(scope.ServiceProvider, logger);
        var bytes = "not an image"u8.ToArray();

        using var content = new MemoryStream(bytes);
        await media.UploadAsync("fake.jpg", content, bytes.Length, CancellationToken.None);

        var rejected = logger.Entries.Single(e => e.EventId.Name == "MediaUploadRejected");
        await Assert.That(rejected.Level).IsEqualTo(LogLevel.Warning);
        await Assert.That(rejected.Properties["FileName"]).IsEqualTo("fake.jpg");
    }

    /// <summary>Records every log entry with its structured properties.</summary>
    private sealed class CapturingLogger<T> : ILogger<T>
    {
        public List<(LogLevel Level, EventId EventId, IReadOnlyDictionary<string, object?> Properties)> Entries { get; } = [];

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            var properties = state is IEnumerable<KeyValuePair<string, object?>> pairs
                ? pairs.ToDictionary(p => p.Key, p => p.Value)
                : [];
            lock (Entries)
            {
                Entries.Add((logLevel, eventId, properties));
            }
        }
    }
}
