using DoingLife.WebApi.Entities;
using Microsoft.EntityFrameworkCore;

namespace DoingLife.WebApi.DAL
{
    public static class CatalogDraftConfiguration
    {
        // Call from AppDbContext.OnModelCreating, after StudyCareerConfiguration.Apply.
        public static void Apply(ModelBuilder b)
        {
            b.Entity<CatalogDraft>(e =>
            {
                e.Property(x => x.DliNumber).HasMaxLength(20).IsRequired();
                e.Property(x => x.SchoolName).HasMaxLength(200).IsRequired();
                e.Property(x => x.Status).HasConversion<string>().HasMaxLength(20);
                e.Property(x => x.Payload).HasColumnType("jsonb").IsRequired();
                e.Property(x => x.PublishSummary).HasMaxLength(1000);
                e.Property(x => x.Version).IsConcurrencyToken();

                // One open draft per school: only drafts still in progress count.
                // Published and discarded drafts stay as history and don't block a new one.
                e.HasIndex(x => x.DliNumber).IsUnique()
                    .HasFilter("status IN ('Open', 'ReadyForReview')");
                e.HasIndex(x => x.Status);

                // Drafts are history: removing a user must never remove their drafts.
                e.HasOne<User>().WithMany().HasForeignKey(x => x.CreatedByUserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<User>().WithMany().HasForeignKey(x => x.UpdatedByUserId).OnDelete(DeleteBehavior.Restrict);
                e.HasOne<User>().WithMany().HasForeignKey(x => x.ClosedByUserId).OnDelete(DeleteBehavior.Restrict);

                e.ToTable(t =>
                {
                    t.HasCheckConstraint("ck_catalog_drafts_status",
                        "status IN ('Open', 'ReadyForReview', 'Published', 'Discarded')");
                    t.HasCheckConstraint("ck_catalog_drafts_payload", "jsonb_typeof(payload) = 'object'");
                });
            });
        }
    }
}
