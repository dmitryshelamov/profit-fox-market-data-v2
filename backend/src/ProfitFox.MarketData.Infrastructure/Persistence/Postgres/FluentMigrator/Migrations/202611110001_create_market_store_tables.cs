using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611110001)]
public sealed class CreateMarketStoreTablesMigration : ForwardOnlyMigration
{
    // Decision 2: the series that grow with time are hypertables compressed after 7 days; the coverage of a series is its disjoint ranges;
    // the state of the collector is one row. Money, prices and volumes are numeric, exactly as the exchange gave them.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE pairs (
                category text NOT NULL,
                symbol text NOT NULL,
                listed boolean NOT NULL,
                listed_at timestamp with time zone NULL,
                CONSTRAINT "PK_pairs" PRIMARY KEY (category, symbol)
            );

            CREATE TABLE candles (
                category text NOT NULL,
                symbol text NOT NULL,
                "interval" text NOT NULL,
                open_time timestamp with time zone NOT NULL,
                open numeric NOT NULL,
                high numeric NOT NULL,
                low numeric NOT NULL,
                close numeric NOT NULL,
                volume numeric NOT NULL,
                turnover numeric NOT NULL,
                CONSTRAINT "PK_candles" PRIMARY KEY (category, symbol, "interval", open_time)
            );

            SELECT create_hypertable('candles', by_range('open_time'));
            ALTER TABLE candles SET (timescaledb.compress, timescaledb.compress_segmentby = 'category, symbol, "interval"', timescaledb.compress_orderby = 'open_time');
            SELECT add_compression_policy('candles', INTERVAL '7 days');

            CREATE TABLE open_interest (
                category text NOT NULL,
                symbol text NOT NULL,
                "time" timestamp with time zone NOT NULL,
                value numeric NOT NULL,
                CONSTRAINT "PK_open_interest" PRIMARY KEY (category, symbol, "time")
            );

            SELECT create_hypertable('open_interest', by_range('time'));
            ALTER TABLE open_interest SET (timescaledb.compress, timescaledb.compress_segmentby = 'category, symbol', timescaledb.compress_orderby = '"time"');
            SELECT add_compression_policy('open_interest', INTERVAL '7 days');

            CREATE TABLE funding_rates (
                category text NOT NULL,
                symbol text NOT NULL,
                "time" timestamp with time zone NOT NULL,
                rate numeric NOT NULL,
                CONSTRAINT "PK_funding_rates" PRIMARY KEY (category, symbol, "time")
            );

            CREATE TABLE liquidations (
                category text NOT NULL,
                symbol text NOT NULL,
                "time" timestamp with time zone NOT NULL,
                side text NOT NULL,
                price numeric NOT NULL,
                quantity numeric NOT NULL,
                CONSTRAINT "PK_liquidations" PRIMARY KEY (category, symbol, "time", side, price, quantity)
            );

            SELECT create_hypertable('liquidations', by_range('time'));
            ALTER TABLE liquidations SET (timescaledb.compress, timescaledb.compress_segmentby = 'category, symbol', timescaledb.compress_orderby = '"time"');
            SELECT add_compression_policy('liquidations', INTERVAL '7 days');

            CREATE TABLE trade_flow_minutes (
                category text NOT NULL,
                symbol text NOT NULL,
                open_time timestamp with time zone NOT NULL,
                buy_volume numeric NOT NULL,
                sell_volume numeric NOT NULL,
                buy_turnover numeric NOT NULL,
                sell_turnover numeric NOT NULL,
                buy_trades integer NOT NULL,
                sell_trades integer NOT NULL,
                CONSTRAINT "PK_trade_flow_minutes" PRIMARY KEY (category, symbol, open_time)
            );

            SELECT create_hypertable('trade_flow_minutes', by_range('open_time'));
            ALTER TABLE trade_flow_minutes SET (timescaledb.compress, timescaledb.compress_segmentby = 'category, symbol', timescaledb.compress_orderby = 'open_time');
            SELECT add_compression_policy('trade_flow_minutes', INTERVAL '7 days');

            CREATE TABLE coverage (
                category text NOT NULL,
                symbol text NOT NULL,
                kind text NOT NULL,
                "interval" text NOT NULL,
                "from" timestamp with time zone NOT NULL,
                "to" timestamp with time zone NOT NULL,
                CONSTRAINT "PK_coverage" PRIMARY KEY (category, symbol, kind, "interval", "from")
            );

            CREATE TABLE collector_state (
                id integer NOT NULL,
                rest_paused_until timestamp with time zone NULL,
                CONSTRAINT "PK_collector_state" PRIMARY KEY (id),
                CONSTRAINT "CK_collector_state_one_row" CHECK (id = 1)
            );

            INSERT INTO collector_state (id) VALUES (1);
            """);
    }
}
