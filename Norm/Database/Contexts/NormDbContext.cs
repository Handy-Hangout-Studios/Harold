using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using Norm.Configuration;
using Norm.Database.Entities;
using Npgsql;

namespace Norm.Database.Contexts
{
    public class NormDbContext : DbContext
    {
        public NormDbContext(DbContextOptions options) : base(options)
        {
        }

        protected override void OnModelCreating(ModelBuilder builder)
        {
            builder.ApplyConfigurationsFromAssembly(typeof(NormDbContext).Assembly);
            base.OnModelCreating(builder);
        }

        public DbContext Context => this;

        private readonly string dbConnectionString;
        public ILoggerFactory loggerFactory;        

        // Novel Tables
        public DbSet<NovelInfo> AllNovelInfo { get; private set; }
        public DbSet<GuildNovelRegistration> GuildNovelRegistrations { get; private set; }

        // Guild Tables
        public DbSet<GuildEvent> GuildEvents { get; private set; }
        public DbSet<GuildPrefix> GuildPrefixes { get; private set; }
        public DbSet<GuildBackgroundJob> GuildBackgroundJobs { get; private set; }
        public DbSet<GuildLogChannel> GuildLogChannels { get; private set; }
        public DbSet<GuildModerationAuditRecord> GuildModerationAuditRecords { get; private set; }
        public DbSet<GuildWelcomeMessageSettings> GuildWelcomeMessages { get; private set; }
        public DbSet<UserTimeZone> UserTimeZones { get; private set; }
    }
}
