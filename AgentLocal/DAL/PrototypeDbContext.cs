using AgentLocal.Models;
using Microsoft.EntityFrameworkCore;
using System.Collections.Generic;
using System.Reflection.Emit;

namespace AgentLocal.Data
{
    public class PrototypeDbContext : DbContext
    {
        public PrototypeDbContext(DbContextOptions<PrototypeDbContext> options)
            : base(options)
        {
        }
        public DbSet<Prototype> Prototypes { get; set; }

        protected override void OnModelCreating(ModelBuilder modelBuilder)
        {
            modelBuilder.Entity<Prototype>(entity =>
            {
                entity.HasKey(e => e.Id);

                entity.Property(e => e.ModelName)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.OperatingSystem)
                    .IsRequired()
                    .HasMaxLength(50);

                entity.Property(e => e.ProcessorModel)
                    .IsRequired()
                    .HasMaxLength(100);

                entity.Property(e => e.ProductDescription)
                    .HasMaxLength(1000);

                entity.Property(e => e.EmailRecipient)
                    .IsRequired()
                    .HasMaxLength(255);

                entity.Property(e => e.ImageData)
                    .HasColumnType("varbinary(max)");

                entity.Property(e => e.CreatedDate)
                    .HasDefaultValueSql("GETUTCDATE()");
            });
        }
    }
}