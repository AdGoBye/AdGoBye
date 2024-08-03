using System;
using System.Linq;
using System.Threading.Tasks;
using AdGoBye.Database;
using JetBrains.Annotations;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Testing;
using Microsoft.Extensions.Options;
using NUnit.Framework;

namespace AdGoBye.Tests;

[TestFixture]
[TestSubject(typeof(Blocklist))]
public class BlocklistTest
{
    private const string TestBlocklist = "https://raw.githubusercontent.com/AdGoBye/AdGoBye-Blocklists/778634a984591dce09e14c69c1a2d9cba959e05b/AGBBase.toml";

    private class TestAdGoByeContextFactory : IDbContextFactory<AdGoByeContext>, IDisposable, IAsyncDisposable
    {
        private SqliteConnection _connection;

        public async ValueTask DisposeAsync()
        {
            if (_connection != null) await _connection.DisposeAsync();
        }

        public AdGoByeContext CreateDbContext()
        {
            _connection = new SqliteConnection("Data Source=InMemorySample;Mode=Memory;Cache=Shared");
            _connection.Open();

            var options = new DbContextOptionsBuilder<AdGoByeContext>();
            options.EnableSensitiveDataLogging();
            options.UseSqlite(_connection);

            return new AdGoByeContext(options.Options);
        }

        public void Dispose()
        {
            _connection?.Dispose();
        }
    }

    [Test]
    public void TestBlocklistUrlHappyPath()
    {
        using var dbFac = new TestAdGoByeContextFactory();
        using var db = dbFac.CreateDbContext();
        db.Database.Migrate();
        var options = Options.Create(new Settings.BlocklistOptions
        {
            BlocklistUrls = [TestBlocklist]
        });
        var blocklistClass = new Blocklist(new FakeLogger<Blocklist>(), options, dbFac);

        blocklistClass.UpdateNetworkBlocklists();

        Assert.That(db.NetworkBlocklists.Count(), !Is.EqualTo(0));
    }

    [Test]
    public void TestBlocklistDanglingUrlRemoval()
    {
        using var dbFac = new TestAdGoByeContextFactory();
        using var db = dbFac.CreateDbContext();
        db.Database.Migrate();
        db.NetworkBlocklists.Add(new Blocklist.NetworkBlocklist
        {
            Url = TestBlocklist,
            Contents = string.Empty,
            ETag = string.Empty
        });
        db.SaveChanges();
        var options = Options.Create(new Settings.BlocklistOptions());
        var blocklistClass = new Blocklist(new FakeLogger<Blocklist>(), options, dbFac);

        blocklistClass.UpdateNetworkBlocklists();

        using var db2 = dbFac.CreateDbContext();
        Assert.That(db2.NetworkBlocklists.Count(), Is.EqualTo(0));
    }

    [Test]
    public void TestBlocklistUrlContentReplacement()
    {
        using var dbFac = new TestAdGoByeContextFactory();
        using var db = dbFac.CreateDbContext();
        db.Database.Migrate();
        db.NetworkBlocklists.Add(new Blocklist.NetworkBlocklist
        {
            Url = TestBlocklist,
            Contents = string.Empty,
            ETag = string.Empty
        });
        db.SaveChanges();
        var options = Options.Create(new Settings.BlocklistOptions
        {
            BlocklistUrls = [TestBlocklist]
        });
        var blocklistClass = new Blocklist(new FakeLogger<Blocklist>(), options, dbFac);

        blocklistClass.UpdateNetworkBlocklists();

        using var db2 = dbFac.CreateDbContext();
        Assert.That(db2.NetworkBlocklists.First().Contents, !Is.EqualTo(string.Empty));
    }

    [Test]
    public void TestBadBlocklistUrl()
    {
        using var dbFac = new TestAdGoByeContextFactory();
        using var db = dbFac.CreateDbContext();
        db.Database.Migrate();
        var options = Options.Create(new Settings.BlocklistOptions
        {
            BlocklistUrls = ["https://httpbin.org/status/418"]
        });
        var blocklistClass = new Blocklist(new FakeLogger<Blocklist>(), options, dbFac);

        blocklistClass.UpdateNetworkBlocklists();

        using var db2 = dbFac.CreateDbContext();
        Assert.That(db2.NetworkBlocklists.Count(), Is.EqualTo(0));
    }
}