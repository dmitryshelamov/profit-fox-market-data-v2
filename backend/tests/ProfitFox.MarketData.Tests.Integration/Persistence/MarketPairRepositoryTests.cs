using System.Globalization;

using Npgsql;

using ProfitFox.MarketData.Domain.Entities;
using ProfitFox.MarketData.Domain.Entities.Enums;
using ProfitFox.MarketData.Domain.ValueObjects;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.Dapper.Repositories;
using ProfitFox.MarketData.Tests.Shared.Generators.Domain.Entity;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Integration.Persistence;

/// <summary>Requirement «Пары сервиса»: every pair the service ever collected stays, listed or not.</summary>
public class MarketPairRepositoryTests
{
    private static readonly Instrument Btc = new Instrument(MarketCategory.Linear, "BTCUSDT");
    private static readonly DateTimeOffset Launch = DateTimeOffset.Parse("2026-10-01T08:00:00Z", CultureInfo.InvariantCulture);

    private MarketPairRepository _pairs = null!;

    [SetUp]
    public void SetUp()
    {
        _pairs = new MarketPairRepository(NpgsqlDataSource.Create(IntegrationEnvironment.ConnectionString));
    }

    [Test]
    public async Task Saved_pairs_are_read_back()
    {
        MarketPair[] pairs = [MarketPairGenerator.Generate(listedAt: Launch), MarketPairGenerator.Generate(listed: false)];

        await _pairs.SaveAsync(pairs, CancellationToken.None);

        (await _pairs.GetAllAsync(CancellationToken.None)).ShouldBe(pairs, ignoreOrder: true);
    }

    // Scenario «Пара снята с торгов».
    [Test]
    public async Task Saved_pair_replaces_the_stored_one()
    {
        await _pairs.SaveAsync([new MarketPair(Btc, Listed: true, Launch)], CancellationToken.None);

        await _pairs.SaveAsync([new MarketPair(Btc, Listed: false, Launch)], CancellationToken.None);

        (await _pairs.GetAllAsync(CancellationToken.None)).ShouldBe([new MarketPair(Btc, Listed: false, Launch)]);
    }

    [Test]
    public async Task Pair_is_found_by_its_instrument()
    {
        await _pairs.SaveAsync([new MarketPair(Btc, Listed: true, Launch)], CancellationToken.None);

        (await _pairs.FindAsync(Btc, CancellationToken.None)).ShouldBe(new MarketPair(Btc, Listed: true, Launch));
        (await _pairs.FindAsync(new Instrument(MarketCategory.Spot, "BTCUSDT"), CancellationToken.None)).ShouldBeNull();
    }
}