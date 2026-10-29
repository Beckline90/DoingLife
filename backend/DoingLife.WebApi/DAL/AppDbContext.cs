#nullable disable
using System;
using System.Collections.Generic;
using DoingLife.WebApi.Entities;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace DoingLife.WebApi.DAL;

public partial class AppDbContext : IdentityDbContext<User, IdentityRole<int>, int>
{
    public AppDbContext(DbContextOptions<AppDbContext> options)
        : base(options)
    {
    }

    public virtual DbSet<UserLoginHistory> UserLoginHistories { get; set; }

    public virtual DbSet<UserPreference> UserPreferences { get; set; }

    public virtual DbSet<UserProfile> UserProfiles { get; set; }

    public virtual DbSet<UserSession> UserSessions { get; set; }

    public virtual DbSet<UserSocialLogin> UserSocialLogins { get; set; }

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<User>(entity =>
        {
            entity.ToTable("users");

            entity.HasKey(e => e.Id).HasName("users_pkey");

            entity.HasIndex(e => e.ScheduledPurgeAt, "idx_users_deletion_status")
                .HasFilter("(is_deleted = true)");

            entity.Property(e => e.Id).HasColumnName("user_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DeletedAt).HasColumnName("deleted_at");
            entity.Property(e => e.FirstName)
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnName("first_name");
            entity.Property(e => e.LastName)
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnName("last_name");
            entity.Property(e => e.IsActive)
                .HasDefaultValue(true)
                .HasColumnName("is_active");
            entity.Property(e => e.IsDeleted)
                .HasDefaultValue(false)
                .HasColumnName("is_deleted");
            entity.Property(e => e.ScheduledPurgeAt).HasColumnName("scheduled_purge_at");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");

            // keep your original length limits
            entity.Property(e => e.Email).HasMaxLength(255);
            entity.Property(e => e.PhoneNumber).HasMaxLength(20);
        });

        
        modelBuilder.Entity<IdentityRole<int>>().ToTable("identity_roles");
        modelBuilder.Entity<IdentityUserRole<int>>().ToTable("identity_user_roles");
        modelBuilder.Entity<IdentityUserClaim<int>>().ToTable("identity_user_claims");
        modelBuilder.Entity<IdentityUserLogin<int>>().ToTable("identity_user_logins");
        modelBuilder.Entity<IdentityRoleClaim<int>>().ToTable("identity_role_claims");
        modelBuilder.Entity<IdentityUserToken<int>>().ToTable("identity_user_tokens");

        modelBuilder.Entity<UserLoginHistory>(entity =>
        {
            entity.HasKey(e => e.UserLoginHistoryId).HasName("user_login_history_pkey");

            entity.ToTable("user_login_history");

            entity.HasIndex(e => e.AttemptedAt, "idx_user_login_history_attempted_at");

            entity.HasIndex(e => e.UserId, "idx_user_login_history_user_id");

            entity.Property(e => e.UserLoginHistoryId).HasColumnName("user_login_history_id");
            entity.Property(e => e.AttemptedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("attempted_at");
            entity.Property(e => e.FailureReason)
                .HasMaxLength(150)
                .HasColumnName("failure_reason");
            entity.Property(e => e.IpAddress)
                .HasMaxLength(45)
                .HasColumnName("ip_address");
            entity.Property(e => e.IsSuccessful).HasColumnName("is_successful");
            entity.Property(e => e.LoginProvider)
                .HasMaxLength(50)
                .HasColumnName("login_provider");
            entity.Property(e => e.UserAgent).HasColumnName("user_agent");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserLoginHistories)
                .HasForeignKey(d => d.UserId)
                .OnDelete(DeleteBehavior.SetNull)
                .HasConstraintName("user_login_history_user_id_fkey");
        });

        modelBuilder.Entity<UserPreference>(entity =>
        {
            entity.HasKey(e => e.UserPreferenceId).HasName("user_preference_pkey");

            entity.ToTable("user_preference");

            entity.HasIndex(e => e.UserId, "user_preference_user_id_key").IsUnique();

            entity.Property(e => e.UserPreferenceId).HasColumnName("user_preference_id");
            entity.Property(e => e.MemoryEnabled)
                .HasDefaultValue(false)
                .HasColumnName("memory_enabled");
            entity.Property(e => e.NotificationsEnabled)
                .HasDefaultValue(true)
                .HasColumnName("notifications_enabled");
            entity.Property(e => e.PreferredLanguage)
                .HasMaxLength(20)
                .HasColumnName("preferred_language");
            entity.Property(e => e.PreferredVoice)
                .HasMaxLength(100)
                .HasColumnName("preferred_voice");
            entity.Property(e => e.SpeechRate)
                .HasPrecision(3, 2)
                .HasDefaultValueSql("1.00")
                .HasColumnName("speech_rate");
            entity.Property(e => e.TimeZone)
                .IsRequired()
                .HasMaxLength(100)
                .HasDefaultValueSql("'UTC'::character varying")
                .HasColumnName("time_zone");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.VoiceEnabled)
                .HasDefaultValue(false)
                .HasColumnName("voice_enabled");

            entity.HasOne(d => d.User).WithOne(p => p.UserPreference)
                .HasForeignKey<UserPreference>(d => d.UserId)
                .HasConstraintName("user_preference_user_id_fkey");
        });

        modelBuilder.Entity<UserProfile>(entity =>
        {
            entity.HasKey(e => e.UserProfileId).HasName("user_profile_pkey");

            entity.ToTable("user_profile");

            entity.HasIndex(e => e.UserId, "user_profile_user_id_key").IsUnique();

            entity.HasIndex(e => e.Username, "user_profile_username_key").IsUnique();

            entity.Property(e => e.UserProfileId).HasColumnName("user_profile_id");
            entity.Property(e => e.Bio).HasColumnName("bio");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.DateOfBirth).HasColumnName("date_of_birth");
            entity.Property(e => e.PostalCode)
                .HasMaxLength(20)
                .HasColumnName("postal_code");
            entity.Property(e => e.Sex)
                .HasMaxLength(30)
                .HasColumnName("sex");
            entity.Property(e => e.UpdatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("updated_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");
            entity.Property(e => e.Username)
                .HasMaxLength(50)
                .HasColumnName("username");

            entity.HasOne(d => d.User).WithOne(p => p.UserProfile)
                .HasForeignKey<UserProfile>(d => d.UserId)
                .HasConstraintName("user_profile_user_id_fkey");
        });

        modelBuilder.Entity<UserSession>(entity =>
        {
            entity.HasKey(e => e.UserSessionId).HasName("user_session_pkey");

            entity.ToTable("user_session");

            entity.HasIndex(e => e.UserId, "idx_user_session_user_id");

            entity.Property(e => e.UserSessionId).HasColumnName("user_session_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.ExpiresAt).HasColumnName("expires_at");
            entity.Property(e => e.RevokedAt).HasColumnName("revoked_at");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserSessions)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_session_user_id_fkey");
        });

        modelBuilder.Entity<UserSocialLogin>(entity =>
        {
            entity.HasKey(e => e.UserSocialLoginId).HasName("user_social_login_pkey");

            entity.ToTable("user_social_login");

            entity.HasIndex(e => e.UserId, "idx_user_social_login_user_id");

            entity.HasIndex(e => new { e.Provider, e.ProviderUserId }, "uq_social_provider_account").IsUnique();

            entity.HasIndex(e => new { e.UserId, e.Provider }, "uq_user_social_provider").IsUnique();

            entity.Property(e => e.UserSocialLoginId).HasColumnName("user_social_login_id");
            entity.Property(e => e.CreatedAt)
                .HasDefaultValueSql("now()")
                .HasColumnName("created_at");
            entity.Property(e => e.Provider)
                .IsRequired()
                .HasMaxLength(50)
                .HasColumnName("provider");
            entity.Property(e => e.ProviderUserId)
                .IsRequired()
                .HasMaxLength(255)
                .HasColumnName("provider_user_id");
            entity.Property(e => e.UserId).HasColumnName("user_id");

            entity.HasOne(d => d.User).WithMany(p => p.UserSocialLogins)
                .HasForeignKey(d => d.UserId)
                .HasConstraintName("user_social_login_user_id_fkey");
        });

        OnModelCreatingPartial(modelBuilder);
    }

    partial void OnModelCreatingPartial(ModelBuilder modelBuilder);
}