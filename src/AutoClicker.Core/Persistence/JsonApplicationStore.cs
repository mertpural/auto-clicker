using System.Text.Json;
using AutoClicker.Core.Models;
using AutoClicker.Core.Validation;

namespace AutoClicker.Core.Persistence;

/// <summary>Serialized writes and atomic replacement keep a failed save from truncating existing data.</summary>
public sealed class JsonApplicationStore(string configurationDirectory) : IApplicationStore, IDisposable
{
    public const int MaximumFileBytes = 4 * 1024 * 1024;
    private readonly SemaphoreSlim _gate = new(1, 1);
    private readonly JsonSerializerOptions _options = ProfileJson.CreateOptions();
    public string ConfigurationDirectory { get; } = Path.GetFullPath(configurationDirectory);
    public string FilePath => Path.Combine(ConfigurationDirectory, "settings.json");

    public async Task<ApplicationData> LoadAsync(CancellationToken cancellationToken = default)
    {
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            Directory.CreateDirectory(ConfigurationDirectory);
            if (!File.Exists(FilePath))
            {
                var defaults = ApplicationData.CreateDefault();
                await WriteAsync(defaults, cancellationToken).ConfigureAwait(false);
                return defaults;
            }
            if (LegacyConfigurationMigration.TryMigrate(FilePath, out var migrated) && migrated is not null)
            {
                ApplicationDataValidator.Validate(migrated);
                await WriteAsync(migrated, cancellationToken).ConfigureAwait(false);
                return migrated;
            }
            ApplicationData data;
            await using (var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
                             bufferSize: 4096, useAsync: true))
            {
                if (stream.Length > MaximumFileBytes) throw new DataValidationException("Configuration file is too large.");
                data = await JsonSerializer.DeserializeAsync<ApplicationData>(stream, _options, cancellationToken).ConfigureAwait(false)
                    ?? throw new DataValidationException("Configuration file is empty.");
            }
            data = SupportedCpsMigration.Apply(data, out var cpsChanged);
            ApplicationDataValidator.Validate(data);
            if (cpsChanged) await WriteAsync(data, cancellationToken).ConfigureAwait(false);
            return data;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DataValidationException)
        {
            throw new PersistenceException("Unable to load configuration. The existing file has been preserved. Open the configuration folder to inspect settings.json, then retry.", exception);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveAsync(ApplicationData data, CancellationToken cancellationToken = default)
    {
        ApplicationDataValidator.Validate(data);
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(data with { LifetimeStatistics = current.LifetimeStatistics }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException)
        {
            throw new PersistenceException("Unable to save configuration. Check that the configuration folder is writable and has enough free space.", exception);
        }
        finally { _gate.Release(); }
    }

    public async Task SaveLifetimeStatisticsAsync(LifetimeStatistics statistics, CancellationToken cancellationToken = default)
    {
        if (statistics.TotalPhysicalClicks < 0 || statistics.TotalActiveTicks < 0)
            throw new DataValidationException("Lifetime statistics are invalid.");
        await _gate.WaitAsync(cancellationToken).ConfigureAwait(false);
        try
        {
            var current = await ReadOrDefaultAsync(cancellationToken).ConfigureAwait(false);
            await WriteAsync(current with { LifetimeStatistics = statistics }, cancellationToken).ConfigureAwait(false);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException or JsonException or DataValidationException)
        {
            throw new PersistenceException("Unable to save usage statistics. Check that the configuration folder is writable and has enough free space.", exception);
        }
        finally { _gate.Release(); }
    }

    private async Task<ApplicationData> ReadOrDefaultAsync(CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        if (!File.Exists(FilePath)) return ApplicationData.CreateDefault();
        await using var stream = new FileStream(FilePath, FileMode.Open, FileAccess.Read, FileShare.Read,
            bufferSize: 4096, useAsync: true);
        if (stream.Length > MaximumFileBytes) throw new DataValidationException("Configuration file is too large.");
        var data = await JsonSerializer.DeserializeAsync<ApplicationData>(stream, _options, cancellationToken).ConfigureAwait(false)
            ?? throw new DataValidationException("Configuration file is empty.");
        ApplicationDataValidator.Validate(data);
        return data;
    }

    private async Task WriteAsync(ApplicationData data, CancellationToken cancellationToken)
    {
        Directory.CreateDirectory(ConfigurationDirectory);
        var bytes = JsonSerializer.SerializeToUtf8Bytes(data, _options);
        if (bytes.Length > MaximumFileBytes) throw new DataValidationException("Configuration exceeds the file size limit.");
        var temporaryPath = Path.Combine(ConfigurationDirectory, $"settings.{Guid.NewGuid():N}.tmp");
        try
        {
            await using (var stream = new FileStream(temporaryPath, FileMode.CreateNew, FileAccess.Write, FileShare.None,
                             bufferSize: 4096, options: FileOptions.Asynchronous | FileOptions.WriteThrough))
            {
                await stream.WriteAsync(bytes, cancellationToken).ConfigureAwait(false);
                await stream.FlushAsync(cancellationToken).ConfigureAwait(false);
            }
            cancellationToken.ThrowIfCancellationRequested();
            File.Move(temporaryPath, FilePath, overwrite: true);
        }
        finally
        {
            // A cleanup error is surfaced, never silently discarded.
            if (File.Exists(temporaryPath)) File.Delete(temporaryPath);
        }
    }

    public void Dispose() => _gate.Dispose();
}
