using System.Globalization;

using Npgsql;

using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Persistence;

/// <summary>Requirement «Паузы по ответам о лимите»: the end of the pause of the REST requests outlives a restart.</summary>
public class CollectorStateRepositoryTests
{
    private static readonly DateTimeOffset Noon = DateTimeOffset.Parse("2026-11-10T12:00:00Z", CultureInfo.InvariantCulture);

    private CollectorStateRepository _state = null!;

    [SetUp]
    public void SetUp()
    {
        _state = new CollectorStateRepository(NpgsqlDataSource.Create(IntegrationEnvironment.ConnectionString));
    }

    [Test]
    public async Task There_is_no_pause_at_first()
    {
        (await _state.GetPausedUntilAsync(CancellationToken.None)).ShouldBeNull();
    }

    [Test]
    public async Task Recorded_pause_is_read_back()
    {
        await _state.PauseAsync(Noon, CancellationToken.None);

        (await _state.GetPausedUntilAsync(CancellationToken.None)).ShouldBe(Noon);
    }

    [Test]
    public async Task Earlier_pause_does_not_shorten_the_recorded_one()
    {
        await _state.PauseAsync(Noon, CancellationToken.None);

        await _state.PauseAsync(Noon.AddMinutes(-5), CancellationToken.None);

        (await _state.GetPausedUntilAsync(CancellationToken.None)).ShouldBe(Noon);
    }
}