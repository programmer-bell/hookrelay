using HookRelay.Data;
using Npgsql;
using Xunit;

namespace HookRelay.Tests;

public sealed class DbTests
{
    private static NpgsqlConnectionStringBuilder Normalize(string? raw) =>
        new(Db.NormalizeConnectionString(raw));

    [Fact]
    public void MissingConnectionString_Throws()
    {
        Assert.Throws<InvalidOperationException>(() => Db.NormalizeConnectionString(""));
    }

    [Fact]
    public void KeyValueLocalHost_KeepsPreferSsl()
    {
        var builder = Normalize(
            "Host=db;Port=5432;Database=hookrelay;Username=hookrelay;Password=hookrelay");

        Assert.Equal("db", builder.Host);
        Assert.Equal(SslMode.Prefer, builder.SslMode);
        Assert.Equal(30, builder.Timeout);
        Assert.Equal(30, builder.CommandTimeout);
    }

    [Fact]
    public void KeyValuePublicHost_EnforcesRequireSsl()
    {
        var builder = Normalize(
            "Host=ep-test.aws.neon.tech;Port=5432;Database=neondb;Username=user;Password=pass");

        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void UriForm_MapsPartsAndRequiresSsl()
    {
        var builder = Normalize(
            "postgres://app:secret@ep-test.aws.neon.tech/neondb?sslmode=require");

        Assert.Equal("ep-test.aws.neon.tech", builder.Host);
        Assert.Equal(5432, builder.Port);
        Assert.Equal("neondb", builder.Database);
        Assert.Equal("app", builder.Username);
        Assert.Equal("secret", builder.Password);
        Assert.Equal(SslMode.Require, builder.SslMode);
    }

    [Fact]
    public void UriFormWithoutSslMode_DefaultsToRequireForPublicHost()
    {
        var builder = Normalize("postgres://app:secret@ep-test.aws.neon.tech/neondb");

        Assert.Equal(SslMode.Require, builder.SslMode);
    }
}
