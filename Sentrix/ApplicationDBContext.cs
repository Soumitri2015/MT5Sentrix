using Microsoft.EntityFrameworkCore;
using Sentrix.EntityModel;
using System;
using System.Collections.Generic;
using System.Data;
using System.Linq;
using System.Text;
using System.Threading.Tasks;

namespace Sentrix
{
    public class ApplicationDBContext : DbContext
    {

        public ApplicationDBContext(DbContextOptions options) : base(options)
        {
        }

        public DbSet<Users> Users { get; set; }
        public DbSet<TradingConfigs> TradingConfigs { get; set; }
        public DbSet<TradingSessionDefinitions> TradingSessionDefinitions { get; set; }
        public DbSet<TradeEvents> TradeEvents { get; set; }
        public DbSet<Positions> Positions { get; set; }
        public DbSet<Roles> Roles { get; set; }
        public DbSet<UserRoles> UserRoles { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<UserRoles>()
                .HasKey(ur => new { ur.UserId, ur.RoleId });
            base.OnModelCreating(modelBuilder);

        }
    }
}
