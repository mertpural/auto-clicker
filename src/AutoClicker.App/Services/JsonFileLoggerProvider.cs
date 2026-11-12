using System.Diagnostics;
using System.Text.Json;
using Microsoft.Extensions.Logging;

namespace AutoClicker.App.Services;

public sealed class JsonFileLoggerProvider(string configurationDirectory) : ILoggerProvider
{
    private readonly object _gate = new();
    private readonly string _directory = Path.Combine(configurationDirectory, "logs");
    public ILogger CreateLogger(string categoryName) => new FileLogger(this, categoryName);
    public void Dispose() { /* No open file handles are retained. */ }

    private void Write<TState>(string category, LogLevel level, EventId eventId, TState state, Exception? exception, string message)
    {
        var entry = new
        {
            timestamp = DateTimeOffset.UtcNow, level = level.ToString(), category,
            eventId = eventId.Id, message, exception = exception?.ToString(),
            properties = state is IEnumerable<KeyValuePair<string, object?>> fields
                ? fields.ToDictionary(field => field.Key, field => field.Value) : null
        };
        lock (_gate)
        {
            try
            {
                Directory.CreateDirectory(_directory);
                var path = Path.Combine(_directory, "application.jsonl");
                if (File.Exists(path) && new FileInfo(path).Length > 5 * 1024 * 1024)
                    File.Move(path, Path.Combine(_directory, "application.previous.jsonl"), overwrite: true);
                File.AppendAllText(path, JsonSerializer.Serialize(entry) + Environment.NewLine);
            }
            catch (Exception error) when (error is IOException or UnauthorizedAccessException)
            {
                Trace.WriteLine($"Log file unavailable: {error.Message}. {level} {category}: {message} {exception}");
            }
        }
    }

    private sealed class FileLogger(JsonFileLoggerProvider provider, string category) : ILogger
    {
        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;
        public bool IsEnabled(LogLevel level) => level >= LogLevel.Information && level != LogLevel.None;
        public void Log<TState>(LogLevel level, EventId eventId, TState state, Exception? exception, Func<TState, Exception?, string> formatter)
        {
            if (IsEnabled(level)) provider.Write(category, level, eventId, state, exception, formatter(state, exception));
        }
    }
}
