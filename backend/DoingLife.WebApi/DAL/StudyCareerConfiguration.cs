using DoingLife.WebApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoingLife.WebApi.DAL
{
    public static class StudyCareerConfiguration
    {
        // Call from AppDbContext.OnModelCreating, next to StudyCatalogConfiguration.Apply.
        public static void Apply(ModelBuilder b)
        {
            b.Entity<Career>(e =>
            {
                e.HasIndex(x => new { x.NocCode, x.NocVersion }).IsUnique();
                e.Property(x => x.NocCode).HasMaxLength(5).IsRequired();
                e.Property(x => x.NocVersion).HasMaxLength(30).IsRequired();
                e.Property(x => x.Title).HasMaxLength(250).IsRequired();
                e.ToTable(t =>
                {
                    t.HasCheckConstraint("ck_careers_teer", "teer_category BETWEEN 0 AND 5");
                    t.HasCheckConstraint("ck_careers_noc_code", "noc_code ~ '^[0-9]{5}$'");
                });
            });

            b.Entity<ProgramCareer>(e =>
            {
                e.HasOne(x => x.Program).WithMany()
                    .HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Career).WithMany()
                    .HasForeignKey(x => x.CareerId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => new { x.ProgramId, x.ExampleTitle }).IsUnique();
                e.Property(x => x.ExampleTitle).HasMaxLength(150).IsRequired();
                e.Property(x => x.Basis).HasConversion<string>().HasMaxLength(30);
            });
        }
    }
}
