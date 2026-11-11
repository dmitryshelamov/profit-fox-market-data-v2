namespace ProfitFox.MarketData.Infrastructure.Exchanges;

/// <summary>Where the end of the pause of the REST requests lives, so it outlives the process that recorded it.</summary>
internal interface IRestPauseStore
{
    Task<DateTimeOffset?> GetPausedUntilAsync(CancellationToken cancellationToken);

    /// <summary>Records the end of the pause; a later end than the stored one replaces it, an earlier one does not.</summary>
    Task PauseAsync(DateTimeOffset until, CancellationToken cancellationToken);
}