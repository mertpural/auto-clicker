using AutoClicker.Core.Models;

namespace AutoClicker.Core.Persistence;

public interface IApplicationStore
{
    string ConfigurationDirectory { get; }
    Task<ApplicationData> LoadAsync(CancellationToken cancellationToken = default);
    Task SaveAsync(ApplicationData data, CancellationToken cancellationToken = default);
    Task SaveLifetimeStatisticsAsync(LifetimeStatistics statistics, CancellationToken cancellationToken = default);
}

public sealed class PersistenceException(string message, Exception innerException) : Exception(message, innerException);
