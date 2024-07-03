using Microsoft.EntityFrameworkCore;

namespace AdGoBye.Database;

public sealed class AdGoByeContext : DbContext
{
    protected override void OnConfiguring(DbContextOptionsBuilder options)
    {
        options.UseSqlite("Data Source=database.db");
    }

    public DbSet<Content> Content { get; set; }
    public DbSet<Content.ContentVersionMeta> ContentVersionMetas { get; set; }
    public DbSet<Blocklist.NetworkBlocklist> NetworkBlocklists { get; set; }
}