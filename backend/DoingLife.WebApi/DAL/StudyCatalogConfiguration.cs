using Microsoft.EntityFrameworkCore;
using DoingLife.WebApi.Entities;

namespace DoingLife.WebApi.DAL
{
    public static class StudyCatalogConfiguration
    {
        // Call from AppDbContext.OnModelCreating: StudyCatalogConfiguration.Apply(modelBuilder);
        public static void Apply(ModelBuilder b)
        {
            b.Entity<Institution>(e =>
            {
                e.HasIndex(x => x.DliNumber).IsUnique();
                e.Property(x => x.DliNumber).HasMaxLength(20).IsRequired();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.Province).HasMaxLength(2).IsRequired();
                e.Property(x => x.InstitutionType).HasMaxLength(30).IsRequired();
                e.Property(x => x.Website).HasMaxLength(300);
            });

            b.Entity<Campus>(e =>
            {
                e.HasOne(x => x.Institution).WithMany(i => i.Campuses)
                    .HasForeignKey(x => x.InstitutionId).OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(x => new { x.InstitutionId, x.SourceName }).IsUnique();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
                e.Property(x => x.SourceName).HasMaxLength(200).IsRequired();
                e.Property(x => x.City).HasMaxLength(100).IsRequired();
            });

            b.Entity<Department>(e =>
            {
                e.HasOne(x => x.Institution).WithMany(i => i.Departments)
                    .HasForeignKey(x => x.InstitutionId).OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(x => new { x.InstitutionId, x.Name }).IsUnique();
                e.Property(x => x.Name).HasMaxLength(200).IsRequired();
            });

            b.Entity<StudyProgram>(e =>
            {
                e.HasOne(x => x.Institution).WithMany(i => i.Programs)
                    .HasForeignKey(x => x.InstitutionId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Department).WithMany(d => d.Programs)
                    .HasForeignKey(x => x.DepartmentId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => new { x.InstitutionId, x.Name }).IsUnique();
                e.Property(x => x.Name).HasMaxLength(250).IsRequired();
                e.Property(x => x.Credential).HasMaxLength(150).IsRequired();
                e.Property(x => x.FieldOfStudy).HasMaxLength(100);
                e.Property(x => x.DurationText).HasMaxLength(60);
                e.Property(x => x.EnglishTest).HasMaxLength(60);
                e.Property(x => x.AcademicMinimum).HasMaxLength(2000);
            });

            b.Entity<ProgramIntake>(e =>
            {
                e.HasOne(x => x.Program).WithMany(p => p.Intakes)
                    .HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Campus).WithMany()
                    .HasForeignKey(x => x.CampusId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => new { x.ProgramId, x.StartDate, x.CampusId }).IsUnique();
                e.Property(x => x.Season).HasMaxLength(20);
                e.Property(x => x.DeliveryMode).HasMaxLength(60);
                e.Property(x => x.InternationalStatus).HasMaxLength(20);
                e.Property(x => x.DomesticStatus).HasMaxLength(20);
            });

            b.Entity<TuitionTerm>(e =>
            {
                e.HasOne(x => x.Program).WithMany(p => p.TuitionTerms)
                    .HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.Cascade);
                e.HasIndex(x => new { x.ProgramId, x.StudentType, x.AcademicYear, x.YearOfStudy, x.TermNumber })
                    .IsUnique();
                e.Property(x => x.StudentType).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.AcademicYear).HasMaxLength(9).IsRequired();
                e.Property(x => x.TermLabel).HasMaxLength(60);
                e.Property(x => x.TuitionRateText).HasMaxLength(60);
                foreach (var name in new[] { nameof(TuitionTerm.Tuition), nameof(TuitionTerm.RecreationAthleticsFee),
                    nameof(TuitionTerm.OneCardSupportFee), nameof(TuitionTerm.StudentTechnologyFee),
                    nameof(TuitionTerm.StudentAssociationFee), nameof(TuitionTerm.BooksSupplies),
                    nameof(TuitionTerm.TermTotalSite) })
                    e.Property(name).HasPrecision(10, 2);
                e.Ignore(x => x.TermTotalCalculated);
            });

            b.Entity<EmploymentStat>(e =>
            {
                e.HasOne(x => x.Institution).WithMany()
                    .HasForeignKey(x => x.InstitutionId).OnDelete(DeleteBehavior.Cascade);
                e.HasOne(x => x.Program).WithMany()
                    .HasForeignKey(x => x.ProgramId).OnDelete(DeleteBehavior.SetNull);
                e.HasIndex(x => new { x.InstitutionId, x.SourceProgramName, x.Period }).IsUnique();
                e.Property(x => x.SourceProgramName).HasMaxLength(200).IsRequired();
                e.Property(x => x.Period).HasMaxLength(10).IsRequired();
                e.Ignore(x => x.Rate);
                e.ToTable(t => t.HasCheckConstraint("ck_employment_counts", "employed >= 0 AND available >= employed"));
            });
        }
    }
}
