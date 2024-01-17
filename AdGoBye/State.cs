using Microsoft.EntityFrameworkCore;

namespace AdGoBye;

public abstract class State
{
    public sealed class IndexContext : DbContext
    {
        protected override void OnConfiguring(DbContextOptionsBuilder options)
        {
            options.UseSqlite("Data Source=database.db");
        }

#pragma warning disable CS8618 //
        public DbSet<Content> Content { get; set; }
        public DbSet<Content.ContentVersionMeta> ContentVersionMetas { get; set; }
        public DbSet<Blocklist.NetworkBlocklist> NetworkBlocklists { get; set; }
#pragma warning restore CS8618
    }
}