using Microsoft.Extensions.DependencyInjection;

using ProfitFox.MarketData.Application.Commands.Pairs.RefreshPairs;

namespace ProfitFox.MarketData.Infrastructure.Mediatr;

internal static class MediatrRegistrar
{
    internal static void Configure(IServiceCollection services)
    {
        services.AddMediatR(configuration => configuration.RegisterServicesFromAssemblyContaining<RefreshPairsCommand>());
    }
}
