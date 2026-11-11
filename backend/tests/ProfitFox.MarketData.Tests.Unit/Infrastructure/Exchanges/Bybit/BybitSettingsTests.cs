using Bybit.Net;

using ProfitFox.MarketData.Infrastructure.Exchanges.Bybit;

using Shouldly;

namespace ProfitFox.MarketData.Tests.Unit.Infrastructure.Exchanges.Bybit;

/// <summary>Where the service reaches Bybit: the live Bybit unless a test gives the address of a fake one.</summary>
public class BybitSettingsTests
{
    [Test]
    public void Without_addresses_the_service_reaches_the_live_bybit()
    {
        new BybitSettings().Environment().ShouldBeSameAs(BybitEnvironment.Live);
    }

    [Test]
    public void Socket_address_moves_only_the_streams()
    {
        BybitEnvironment environment = new BybitSettings(SocketAddress: "ws://127.0.0.1:5297").Environment();

        environment.SocketBaseAddress.ShouldBe("ws://127.0.0.1:5297");
        environment.RestBaseAddress.ShouldBe(BybitEnvironment.Live.RestBaseAddress);
    }

    [Test]
    public void Rest_address_moves_only_the_rest_calls()
    {
        BybitEnvironment environment = new BybitSettings(RestAddress: "http://127.0.0.1:5296").Environment();

        environment.RestBaseAddress.ShouldBe("http://127.0.0.1:5296");
        environment.SocketBaseAddress.ShouldBe(BybitEnvironment.Live.SocketBaseAddress);
    }
}
