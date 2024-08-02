using Microsoft.EntityFrameworkCore;

namespace AdGoBye.Database;

public sealed class AdGoByeContext(DbContextOptions<AdGoByeContext> options) : DbContext(options)
{
    public DbSet<Content> Content { get; set; }
    public DbSet<Content.ContentVersionMeta> ContentVersionMetas { get; set; }
    public DbSet<Blocklist.NetworkBlocklist> NetworkBlocklists { get; set; }
}