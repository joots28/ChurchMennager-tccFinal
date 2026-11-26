using ChurchManager.Api.Models;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Data;

public sealed class AppDbContext(DbContextOptions<AppDbContext> options) : DbContext(options)
{
    public DbSet<Church> Churches => Set<Church>();
    public DbSet<AppUser> Users => Set<AppUser>();
    public DbSet<Member> Members => Set<Member>();
    public DbSet<Ministry> Ministries => Set<Ministry>();
    public DbSet<ChurchEvent> Events => Set<ChurchEvent>();
    public DbSet<EventAttendance> EventAttendances => Set<EventAttendance>();
    public DbSet<ChurchScale> Scales => Set<ChurchScale>();
    public DbSet<ScaleParticipant> ScaleParticipants => Set<ScaleParticipant>();
    public DbSet<FinanceTransaction> Transactions => Set<FinanceTransaction>();
    public DbSet<Donation> Donations => Set<Donation>();
    public DbSet<ChurchRole> Roles => Set<ChurchRole>();
    public DbSet<RolePermission> RolePermissions => Set<RolePermission>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        base.OnModelCreating(modelBuilder);

        modelBuilder.Entity<Church>(entity =>
        {
            entity.ToTable("Churches");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Denomination).HasMaxLength(100);
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512);
            entity.Property(x => x.Phone).HasMaxLength(40).HasDefaultValue("");
            entity.Property(x => x.Address).HasMaxLength(250).HasDefaultValue("");
            entity.Property(x => x.LogoUrl).HasMaxLength(500);
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.HasIndex(x => x.Email);
        });

        modelBuilder.Entity<AppUser>(entity =>
        {
            entity.ToTable("Users");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).IsRequired();
            entity.Property(x => x.PasswordHash).HasMaxLength(512).IsRequired();
            entity.Property(x => x.Cpf).HasMaxLength(14);
            entity.Property(x => x.Phone).HasMaxLength(40);
            entity.Property(x => x.PhotoUrl).HasMaxLength(500);
            entity.Property(x => x.Role).HasMaxLength(80).HasDefaultValue("Membro").IsRequired();
            entity.Property(x => x.IsActive).HasDefaultValue(true);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.Email).IsUnique();
            entity.HasIndex(x => x.ChurchId);
            entity.HasOne(x => x.Church).WithMany(c => c.Users).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Cargo).WithMany().HasForeignKey(x => x.CargoId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Member>(entity =>
        {
            entity.ToTable("Members");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Email).HasMaxLength(254).HasDefaultValue("");
            entity.Property(x => x.Phone).HasMaxLength(40).HasDefaultValue("");
            entity.Property(x => x.Ministry).HasMaxLength(120).HasDefaultValue("");
            entity.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("Ativo");
            entity.Property(x => x.Role).HasMaxLength(80).HasDefaultValue("Membro");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => new { x.ChurchId, x.Email });
            entity.HasOne<Church>().WithMany(c => c.Members).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Ministry>(entity =>
        {
            entity.ToTable("Ministries");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).HasDefaultValue("");
            entity.Property(x => x.Coordinator).HasMaxLength(160).HasDefaultValue("");
            entity.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("Ativo");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasOne<Church>().WithMany(c => c.Ministries).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChurchEvent>(entity =>
        {
            entity.ToTable("Events");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(100).IsRequired();
            entity.Property(x => x.Description).HasDefaultValue("");
            entity.Property(x => x.Location).HasMaxLength(255).HasDefaultValue("");
            entity.Property(x => x.Ministry).HasMaxLength(120).HasDefaultValue("Todos");
            entity.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("Planejado");
            entity.Property(x => x.IsRestricted).HasDefaultValue(false);
            entity.Property(x => x.RecurrenceType).HasMaxLength(50).HasDefaultValue("Nenhuma");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => new { x.ChurchId, x.StartsAt });
            entity.HasOne<Church>().WithMany(c => c.Events).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<EventAttendance>(entity =>
        {
            entity.ToTable("EventAttendances");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.ConfirmedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => new { x.EventId, x.UserId }).IsUnique();
            entity.HasOne(x => x.Event).WithMany(e => e.Attendances).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne<Church>().WithMany().HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<ChurchScale>(entity =>
        {
            entity.ToTable("Scales");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Title).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Ministry).HasMaxLength(120).IsRequired();
            entity.Property(x => x.AssignedMembersJson).HasColumnType("text").HasDefaultValue("[]");
            entity.Property(x => x.Status).HasMaxLength(40).HasDefaultValue("Pendente");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => new { x.ChurchId, x.StartsAt });
            entity.HasOne<Church>().WithMany(c => c.Scales).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Event).WithMany(e => e.Scales).HasForeignKey(x => x.EventId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ScaleParticipant>(entity =>
        {
            entity.ToTable("ScaleParticipants");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.MemberName).HasMaxLength(160).IsRequired();
            entity.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Confirmado");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => x.ScaleId);
            entity.HasOne(x => x.Scale).WithMany(s => s.Participants).HasForeignKey(x => x.ScaleId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
            entity.HasOne<Church>().WithMany().HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<FinanceTransaction>(entity =>
        {
            entity.ToTable("Transactions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Description).HasMaxLength(180).IsRequired();
            entity.Property(x => x.Category).HasMaxLength(100).HasDefaultValue("Outros");
            entity.Property(x => x.Amount).HasPrecision(12, 2);
            entity.Property(x => x.TransactionType).HasMaxLength(30).HasDefaultValue("Income");
            entity.Property(x => x.Method).HasMaxLength(50).HasDefaultValue("PIX");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => new { x.ChurchId, x.TransactionDate });
            entity.HasIndex(x => x.DonationId).IsUnique().HasFilter("\"DonationId\" IS NOT NULL");
            entity.HasOne<Church>().WithMany(c => c.Transactions).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.Donation).WithMany().HasForeignKey(x => x.DonationId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<Donation>(entity =>
        {
            entity.ToTable("Donations");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.DonorName).HasMaxLength(160).HasDefaultValue("");
            entity.Property(x => x.Amount).HasPrecision(10, 2);
            entity.Property(x => x.Category).HasMaxLength(50).HasDefaultValue("Dízimo");
            entity.Property(x => x.PaymentMethod).HasMaxLength(30).HasDefaultValue("PIX");
            entity.Property(x => x.ReceiptUrl).HasMaxLength(500);
            entity.Property(x => x.QrCode).HasMaxLength(500);
            entity.Property(x => x.Status).HasMaxLength(50).HasDefaultValue("Pendente");
            entity.Property(x => x.DonationDate).HasDefaultValueSql("CURRENT_TIMESTAMP");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasIndex(x => x.UserId);
            entity.HasOne<Church>().WithMany(c => c.Donations).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
            entity.HasOne(x => x.User).WithMany().HasForeignKey(x => x.UserId).OnDelete(DeleteBehavior.SetNull);
        });

        modelBuilder.Entity<ChurchRole>(entity =>
        {
            entity.ToTable("Roles");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Name).HasMaxLength(50).IsRequired();
            entity.Property(x => x.Description).HasMaxLength(500).HasDefaultValue("");
            entity.Property(x => x.PermissionsJson).HasColumnType("text").HasDefaultValue("[]");
            entity.Property(x => x.Users).HasDefaultValue(0);
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasOne<Church>().WithMany(c => c.Roles).HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<RolePermission>(entity =>
        {
            entity.ToTable("RolePermissions");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.Module).HasMaxLength(50).IsRequired();

            entity.HasIndex(x => x.CargoId);
            entity.HasOne(x => x.Cargo).WithMany(r => r.Permissions).HasForeignKey(x => x.CargoId).OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<AuditLog>(entity =>
        {
            entity.ToTable("AuditLogs");
            entity.HasKey(x => x.Id);
            entity.Property(x => x.PerformedByEmail).HasMaxLength(254).IsRequired();
            entity.Property(x => x.TargetUserEmail).HasMaxLength(254);
            entity.Property(x => x.Action).HasMaxLength(80).IsRequired();
            entity.Property(x => x.Details).HasMaxLength(1000).HasDefaultValue("");
            entity.Property(x => x.CreatedAt).HasDefaultValueSql("CURRENT_TIMESTAMP");

            entity.HasIndex(x => x.ChurchId);
            entity.HasOne<Church>().WithMany().HasForeignKey(x => x.ChurchId).OnDelete(DeleteBehavior.Cascade);
        });
    }
}
