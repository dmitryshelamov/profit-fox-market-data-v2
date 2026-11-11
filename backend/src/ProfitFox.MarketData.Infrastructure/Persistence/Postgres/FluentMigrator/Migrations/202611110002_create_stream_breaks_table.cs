using FluentMigrator;

namespace ProfitFox.MarketData.Infrastructure.Persistence.Postgres.FluentMigrator.Migrations;

[Migration(202611110002)]
public sealed class CreateStreamBreaksTableMigration : ForwardOnlyMigration
{
    // Task 5.5: the breaks of the streams of the exchange, for the parity check; a break is known by its stream and the moment it began.
    public override void Up()
    {
        Execute.Sql(
            """
            CREATE TABLE stream_breaks (
                stream text NOT NULL,
                lost_at timestamp with time zone NOT NULL,
                restored_at timestamp with time zone NOT NULL,
                CONSTRAINT "PK_stream_breaks" PRIMARY KEY (stream, lost_at)
            );
            """);
    }
}
