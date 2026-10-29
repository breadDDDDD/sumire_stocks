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
    public void Uri_password_may_contain_question_mark_and_slash()
    {
        var csb = new NpgsqlConnectionStringBuilder(Db.ConnectionString("postgresql://postgres.ref:a?b/c@d@aws-0.pooler.example:5432/postgres?sslmode=require"));
        Assert.Equal("a?b/c@d", csb.Password);
        Assert.Equal("aws-0.pooler.example", csb.Host);
        Assert.Equal(5432, csb.Port);
        Assert.Equal(SslMode.Require, csb.SslMode);
    }

    [Fact]
    public void Uri_with_ipv6_host_and_default_port()
    {
        var csb = new NpgsqlConnectionStringBuilder(Db.ConnectionString("postgresql://u:p@[2001:db8::1]/postgres"));
        Assert.Equal("2001:db8::1", csb.Host);
        Assert.Equal(5432, csb.Port);
    }

    [Theory]
    [InlineData("\"postgresql://u:p@host.example:6543/postgres\"\n")]
    [InlineData("'postgresql://u:p@host.example:6543/postgres'")]
    [InlineData("  postgresql://u:p@host.example:6543/postgres\r\n")]
    public void Surrounding_quotes_and_whitespace_are_ignored(string raw)
    {
        var csb = new NpgsqlConnectionStringBuilder(Db.ConnectionString(raw));
        Assert.Equal("host.example", csb.Host);
        Assert.Equal("p", csb.Password);
        Assert.Equal(6543, csb.Port);
    }

    [Theory]
    [InlineData("postgresql://u:p@host.example:notaport/postgres")]
    [InlineData("postgresql://u:p@/postgres")]
    public void Malformed_uri_is_reported_as_invalid(string raw)
    {
        Assert.Equal("", Db.ConnectionString(raw));
        Assert.NotNull(Db.ConfigError);
    }

    [Theory]
    [InlineData("Failed to connect to [2406:da18:243:7400::1]:5432", "2406:da18")]
    [InlineData("Failed to connect to 10.20.30.40:6543", "10.20.30.40")]
    [InlineData("No such host is known: db.abcdefref.supabase.co", "abcdefref")]
    public void Short_error_hides_hosts(string inner, string secret)
    {
        var message = Db.ShortError(new InvalidOperationException("wrap", new NpgsqlException(inner)));
        Assert.DoesNotContain(secret, message);
        Assert.StartsWith("NpgsqlException:", message);
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
