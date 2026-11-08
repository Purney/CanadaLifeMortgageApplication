using Microsoft.EntityFrameworkCore;
using MortgageApplication.Api.Models;
using ApplicationEntity = MortgageApplication.Api.Models.MortgageApplication;

namespace MortgageApplication.Api.Data;

public sealed class MortgageDbContext(DbContextOptions<MortgageDbContext> options) : DbContext(options)
{
    public DbSet<ApplicationEntity> Applications => Set<ApplicationEntity>();

    public DbSet<AffordabilityAssessment> Assessments => Set<AffordabilityAssessment>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        var applications = modelBuilder.Entity<ApplicationEntity>();
        applications.HasKey(x => x.Id);
        var assessments = modelBuilder.Entity<AffordabilityAssessment>();
        assessments.HasKey(x => x.Sequence);
        assessments.Property(x => x.Sequence).ValueGeneratedOnAdd();
        assessments.HasIndex(x => x.Id).IsUnique();
        assessments.HasIndex(x => new { x.ApplicationId, x.CreatedAtUtcTicks, x.Sequence });
        assessments.HasOne<ApplicationEntity>().WithMany().HasForeignKey(x => x.ApplicationId).OnDelete(DeleteBehavior.Restrict);
        assessments.Property(x => x.SnapshotJson).IsRequired();
    }
}
