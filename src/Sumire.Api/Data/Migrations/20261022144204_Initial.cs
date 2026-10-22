using System;
using Microsoft.EntityFrameworkCore.Migrations;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace Sumire.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class Initial : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "sumire");

            migrationBuilder.CreateTable(
                name: "alerts",
                schema: "sumire",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    code = table.Column<string>(type: "text", nullable: true),
                    type = table.Column<string>(type: "text", nullable: false),
                    @params = table.Column<string>(name: "params", type: "jsonb", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    last_triggered_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_alerts", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "daily_bars",
                schema: "sumire",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    date = table.Column<DateOnly>(type: "date", nullable: false),
                    open = table.Column<decimal>(type: "numeric", nullable: false),
                    high = table.Column<decimal>(type: "numeric", nullable: false),
                    low = table.Column<decimal>(type: "numeric", nullable: false),
                    close = table.Column<decimal>(type: "numeric", nullable: false),
                    adj_close = table.Column<decimal>(type: "numeric", nullable: false),
                    volume = table.Column<long>(type: "bigint", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_daily_bars", x => new { x.code, x.date });
                });

            migrationBuilder.CreateTable(
                name: "forecast_metrics",
                schema: "sumire",
                columns: table => new
                {
                    model = table.Column<string>(type: "text", nullable: false),
                    horizon = table.Column<int>(type: "integer", nullable: false),
                    scope = table.Column<string>(type: "text", nullable: false),
                    mape = table.Column<double>(type: "double precision", nullable: false),
                    dir_accuracy = table.Column<double>(type: "double precision", nullable: false),
                    coverage80 = table.Column<double>(type: "double precision", nullable: false),
                    n = table.Column<int>(type: "integer", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecast_metrics", x => new { x.model, x.horizon, x.scope });
                });

            migrationBuilder.CreateTable(
                name: "forecasts",
                schema: "sumire",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    horizon = table.Column<int>(type: "integer", nullable: false),
                    model = table.Column<string>(type: "text", nullable: false),
                    p10 = table.Column<decimal>(type: "numeric", nullable: false),
                    p50 = table.Column<decimal>(type: "numeric", nullable: false),
                    p90 = table.Column<decimal>(type: "numeric", nullable: false),
                    prob_up = table.Column<double>(type: "double precision", nullable: false),
                    sigma = table.Column<double>(type: "double precision", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_forecasts", x => new { x.code, x.as_of, x.horizon, x.model });
                });

            migrationBuilder.CreateTable(
                name: "fundamentals",
                schema: "sumire",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    updated_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    market_cap = table.Column<decimal>(type: "numeric", nullable: true),
                    pe = table.Column<double>(type: "double precision", nullable: true),
                    pb = table.Column<double>(type: "double precision", nullable: true),
                    roe = table.Column<double>(type: "double precision", nullable: true),
                    der = table.Column<double>(type: "double precision", nullable: true),
                    npm = table.Column<double>(type: "double precision", nullable: true),
                    eps = table.Column<decimal>(type: "numeric", nullable: true),
                    div_yield = table.Column<double>(type: "double precision", nullable: true),
                    revenue_growth = table.Column<double>(type: "double precision", nullable: true),
                    earnings_growth = table.Column<double>(type: "double precision", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_fundamentals", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "insights",
                schema: "sumire",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    runner = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: true),
                    sentiment = table.Column<double>(type: "double precision", nullable: false),
                    confidence = table.Column<string>(type: "text", nullable: false),
                    body = table.Column<string>(type: "jsonb", nullable: false),
                    sources = table.Column<string>(type: "jsonb", nullable: false),
                    warnings = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_insights", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "instruments",
                schema: "sumire",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    yahoo_symbol = table.Column<string>(type: "text", nullable: false),
                    name = table.Column<string>(type: "text", nullable: false),
                    sector = table.Column<string>(type: "text", nullable: true),
                    industry = table.Column<string>(type: "text", nullable: true),
                    board = table.Column<string>(type: "text", nullable: true),
                    is_index = table.Column<bool>(type: "boolean", nullable: false),
                    index_memberships = table.Column<string[]>(type: "text[]", nullable: false),
                    group_name = table.Column<string>(type: "text", nullable: true),
                    core = table.Column<bool>(type: "boolean", nullable: false),
                    active = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_instruments", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "research_jobs",
                schema: "sumire",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    kind = table.Column<string>(type: "text", nullable: false),
                    subject = table.Column<string>(type: "text", nullable: false),
                    priority = table.Column<int>(type: "integer", nullable: false),
                    packet = table.Column<string>(type: "jsonb", nullable: true),
                    status = table.Column<string>(type: "text", nullable: false),
                    runner = table.Column<string>(type: "text", nullable: true),
                    model = table.Column<string>(type: "text", nullable: true),
                    attempts = table.Column<int>(type: "integer", nullable: false),
                    error = table.Column<string>(type: "text", nullable: true),
                    created_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: false),
                    started_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    finished_at = table.Column<DateTimeOffset>(type: "timestamp with time zone", nullable: true),
                    insight_id = table.Column<long>(type: "bigint", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_research_jobs", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "settings",
                schema: "sumire",
                columns: table => new
                {
                    key = table.Column<string>(type: "text", nullable: false),
                    value = table.Column<string>(type: "jsonb", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_settings", x => x.key);
                });

            migrationBuilder.CreateTable(
                name: "signal_scores",
                schema: "sumire",
                columns: table => new
                {
                    code = table.Column<string>(type: "text", nullable: false),
                    as_of = table.Column<DateOnly>(type: "date", nullable: false),
                    total = table.Column<double>(type: "double precision", nullable: false),
                    trend = table.Column<double>(type: "double precision", nullable: true),
                    momentum = table.Column<double>(type: "double precision", nullable: true),
                    volume = table.Column<double>(type: "double precision", nullable: true),
                    volatility = table.Column<double>(type: "double precision", nullable: true),
                    valuation = table.Column<double>(type: "double precision", nullable: true),
                    forecast = table.Column<double>(type: "double precision", nullable: true),
                    news = table.Column<double>(type: "double precision", nullable: true),
                    reasons = table.Column<string>(type: "jsonb", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_signal_scores", x => x.code);
                });

            migrationBuilder.CreateTable(
                name: "watchlists",
                schema: "sumire",
                columns: table => new
                {
                    id = table.Column<int>(type: "integer", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    name = table.Column<string>(type: "text", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlists", x => x.id);
                });

            migrationBuilder.CreateTable(
                name: "watchlist_items",
                schema: "sumire",
                columns: table => new
                {
                    watchlist_id = table.Column<int>(type: "integer", nullable: false),
                    code = table.Column<string>(type: "text", nullable: false),
                    sort = table.Column<int>(type: "integer", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("pk_watchlist_items", x => new { x.watchlist_id, x.code });
                    table.ForeignKey(
                        name: "fk_watchlist_items_watchlists_watchlist_id",
                        column: x => x.watchlist_id,
                        principalSchema: "sumire",
                        principalTable: "watchlists",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "ix_alerts_active",
                schema: "sumire",
                table: "alerts",
                column: "active");

            migrationBuilder.CreateIndex(
                name: "ix_daily_bars_date",
                schema: "sumire",
                table: "daily_bars",
                column: "date");

            migrationBuilder.CreateIndex(
                name: "ix_forecasts_as_of",
                schema: "sumire",
                table: "forecasts",
                column: "as_of");

            migrationBuilder.CreateIndex(
                name: "ix_insights_kind_subject_created_at",
                schema: "sumire",
                table: "insights",
                columns: new[] { "kind", "subject", "created_at" });

            migrationBuilder.CreateIndex(
                name: "ix_instruments_core",
                schema: "sumire",
                table: "instruments",
                column: "core");

            migrationBuilder.CreateIndex(
                name: "ix_research_jobs_created_at",
                schema: "sumire",
                table: "research_jobs",
                column: "created_at");

            migrationBuilder.CreateIndex(
                name: "ix_research_jobs_status_priority",
                schema: "sumire",
                table: "research_jobs",
                columns: new[] { "status", "priority" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "alerts",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "daily_bars",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "forecast_metrics",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "forecasts",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "fundamentals",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "insights",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "instruments",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "research_jobs",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "settings",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "signal_scores",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "watchlist_items",
                schema: "sumire");

            migrationBuilder.DropTable(
                name: "watchlists",
                schema: "sumire");
        }
    }
}
