using ProfitFox.MarketData.Infrastructure;
using ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Extensions;

namespace ProfitFox.MarketData.Presentation;

/// <summary>The host of the market data service; it migrates its own database before it starts (decision 2).</summary>
public class Program
{
    public static void Main(string[] args)
    {
        WebApplicationBuilder builder = WebApplication.CreateBuilder(args);

        InfrastructureRegistrar.Configure(builder.Configuration, builder.Services);

        WebApplication app = builder.Build();

        app.Services.MigrateToLatestVersion();

        app.Run();
    }
}
