using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sumire.Api.Data;

namespace Sumire.Api.Tests.Data;

public class DbTests
{
    [Fact]
    public void Transaction_pooler_disables_prepare_and_reset()
    {
        var csb = new NpgsqlConnectionStringBuilder(Db.ConnectionString("Host=pooler.example;Port=6543;Database=postgres;Username=u;Password=p"));
        Assert.Equal(0, csb.MaxAutoPrepare);
        Assert.True(csb.NoResetOnClose);
    }

    [Fact]
    public void Uri_form_is_converted()
    {
        var csb = new NpgsqlConnectionStringBuilder(Db.ConnectionString("postgresql://postgres.ref:p%40ss@word@aws-0.pooler.example:6543/postgres?sslmode=require"));
        Assert.Equal("aws-0.pooler.example", csb.Host);
        Assert.Equal(6543, csb.Port);
        Assert.Equal("postgres.ref", csb.Username);
        Assert.Equal("p@ss@word", csb.Password);
        Assert.Equal("postgres", csb.Database);
        Assert.Equal(SslMode.Require, csb.SslMode);
        Assert.Equal(0, csb.MaxAutoPrepare);
    }

    [Fact]
    public void Invalid_value_returns_empty_without_echoing_it()
    {
        Assert.Equal("", Db.ConnectionString("not a connection string"));
        Assert.DoesNotContain("not a connection", Db.ConfigError);
    }

    [Fact]
    public void Session_pooler_is_left_alone()
    {
        var csb = new NpgsqlConnectionStringBuilder(Db.ConnectionString("Host=pooler.example;Port=5432;Database=postgres;Username=u;Password=p"));
        Assert.False(csb.NoResetOnClose);
    }

    [Fact]
    public void Short_error_hides_quoted_values()
    {
        var message = Db.ShortError(new InvalidOperationException("wrap", new Exception("password authentication failed for user \"postgres.secretref\"")));
        Assert.DoesNotContain("secretref", message);
    }

    [Fact]
    public void Model_uses_sumire_schema_and_snake_case()
    {
        using var db = new SumireDbContextFactory().CreateDbContext([]);
        var bars = db.Model.FindEntityType(typeof(DailyBar))!;
        Assert.Equal("sumire", bars.GetSchema());
        Assert.Equal("daily_bars", bars.GetTableName());
        Assert.Equal("index_memberships", db.Model.FindEntityType(typeof(Instrument))!.FindProperty(nameof(Instrument.IndexMemberships))!.GetColumnName());
    }
}
