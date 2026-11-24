using ChurchManager.Api.Models;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ChurchManager.Api.Data;

public static class DatabaseSeeder
{
    public static async Task SeedAsync(
        AppDbContext db,
        IPasswordHasher<AppUser> userHasher,
        IPasswordHasher<Church> churchHasher,
        ILogger logger,
        CancellationToken cancellationToken = default)
    {
        if (await db.Churches.AnyAsync(cancellationToken))
            return;

        logger.LogInformation("Banco de dados vazio detectado. Inicializando dados padrão com isolamento...");

        await using var transaction = await db.Database.BeginTransactionAsync(cancellationToken);
        try
        {
            var church = new Church
            {
                Name = "Igreja Central ChurchManager",
                Denomination = "Comunidade Cristã",
                Email = "institucional@churchmanager.com",
                Phone = "(11) 98765-4321",
                Address = "Av. Principal, 1000 - Centro",
                LogoUrl = "",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            church.PasswordHash = churchHasher.HashPassword(church, "Igreja@2026Secure!");
            db.Churches.Add(church);
            await db.SaveChangesAsync(cancellationToken);

            var adminRole = new ChurchRole
            {
                ChurchId = church.Id,
                Name = "Administrador",
                Description = "Acesso completo à gestão da igreja.",
                PermissionsJson = "[\"Membros\",\"Eventos\",\"Financeiro\",\"Relatórios\",\"Configurações\",\"Perfil\",\"Escalas\"]",
                Users = 1,
                CreatedAt = DateTime.UtcNow
            };
            var leaderRole = new ChurchRole
            {
                ChurchId = church.Id,
                Name = "Líder",
                Description = "Liderança de ministérios e escalas.",
                PermissionsJson = "[\"Membros\",\"Eventos\",\"Escalas\",\"Perfil\"]",
                Users = 0,
                CreatedAt = DateTime.UtcNow
            };
            var memberRole = new ChurchRole
            {
                ChurchId = church.Id,
                Name = "Membro",
                Description = "Membro da congregação.",
                PermissionsJson = "[\"Eventos\",\"Escalas\",\"Perfil\"]",
                Users = 1,
                CreatedAt = DateTime.UtcNow
            };
            db.Roles.AddRange(adminRole, leaderRole, memberRole);
            await db.SaveChangesAsync(cancellationToken);

            var adminUser = new AppUser
            {
                ChurchId = church.Id,
                CargoId = adminRole.Id,
                Name = "Jonathan Otiniel",
                Email = "jonathan@churchmanager.com",
                Phone = "(11) 98765-4321",
                Role = "Administrador",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            adminUser.PasswordHash = userHasher.HashPassword(adminUser, "Admin@2026Secure!");

            var memberUser = new AppUser
            {
                ChurchId = church.Id,
                CargoId = memberRole.Id,
                Name = "Felippe Silva",
                Email = "felippe@churchmanager.com",
                Phone = "(11) 91234-5678",
                Role = "Membro",
                IsActive = true,
                CreatedAt = DateTime.UtcNow
            };
            memberUser.PasswordHash = userHasher.HashPassword(memberUser, "Membro@2026Secure!");

            db.Users.AddRange(adminUser, memberUser);
            await db.SaveChangesAsync(cancellationToken);

            var adminMember = new Member
            {
                ChurchId = church.Id,
                UserId = adminUser.Id,
                Name = adminUser.Name,
                Email = adminUser.Email,
                Phone = adminUser.Phone ?? "",
                Ministry = "Louvor",
                Role = "Administrador",
                Status = "Ativo",
                CreatedAt = DateTime.UtcNow
            };
            var regularMember = new Member
            {
                ChurchId = church.Id,
                UserId = memberUser.Id,
                Name = memberUser.Name,
                Email = memberUser.Email,
                Phone = memberUser.Phone ?? "",
                Ministry = "Comunicação",
                Role = "Membro",
                Status = "Ativo",
                CreatedAt = DateTime.UtcNow
            };
            db.Members.AddRange(adminMember, regularMember);

            var ministries = new[]
            {
                new Ministry { ChurchId = church.Id, Name = "Louvor", Coordinator = "Jonathan Otiniel", Description = "Banda e equipe de adoração", Status = "Ativo" },
                new Ministry { ChurchId = church.Id, Name = "Recepção", Coordinator = "Maria Souza", Description = "Acolhimento aos visitantes e membros", Status = "Ativo" },
                new Ministry { ChurchId = church.Id, Name = "Comunicação", Coordinator = "Felippe Silva", Description = "Mídias sociais, transmissão e design", Status = "Ativo" },
                new Ministry { ChurchId = church.Id, Name = "Ministério Infantil", Coordinator = "Ana Clara", Description = "Educação cristã infantil", Status = "Ativo" }
            };
            db.Ministries.AddRange(ministries);

            var sampleEvent = new ChurchEvent
            {
                ChurchId = church.Id,
                Title = "Culto de Celebração de Domingo",
                Description = "Culto presencial com louvor e palavra de edificação.",
                StartsAt = DateTime.UtcNow.Date.AddDays(7).AddHours(19),
                Location = "Templo Principal",
                Ministry = "Todos",
                Status = "Confirmado",
                CreatedAt = DateTime.UtcNow
            };
            db.Events.Add(sampleEvent);
            await db.SaveChangesAsync(cancellationToken);

            var sampleScale = new ChurchScale
            {
                ChurchId = church.Id,
                EventId = sampleEvent.Id,
                Title = "Escala de Louvor - Culto de Celebração",
                Ministry = "Louvor",
                StartsAt = sampleEvent.StartsAt,
                AssignedMembersJson = "[\"Jonathan Otiniel\", \"Felippe Silva\"]",
                Status = "Confirmada",
                CreatedAt = DateTime.UtcNow
            };
            db.Scales.Add(sampleScale);

            var sampleDonation = new Donation
            {
                ChurchId = church.Id,
                UserId = memberUser.Id,
                DonorName = memberUser.Name,
                Amount = 150.00m,
                Category = "Dízimo",
                Status = "Confirmada",
                DonationDate = DateTime.UtcNow,
                CreatedAt = DateTime.UtcNow
            };
            db.Donations.Add(sampleDonation);
            await db.SaveChangesAsync(cancellationToken);

            var sampleTransaction = new FinanceTransaction
            {
                ChurchId = church.Id,
                Description = "Dízimo de Felippe Silva",
                Category = "Dízimo",
                Amount = 150.00m,
                TransactionType = "Income",
                TransactionDate = DateTime.UtcNow,
                Method = "PIX",
                DonationId = sampleDonation.Id,
                CreatedAt = DateTime.UtcNow
            };
            db.Transactions.Add(sampleTransaction);

            db.AuditLogs.Add(new AuditLog
            {
                ChurchId = church.Id,
                PerformedByEmail = "sistema@churchmanager",
                Action = "InitialSeed",
                Details = "Provisionamento inicial com igreja, membros e configurações padrão.",
                CreatedAt = DateTime.UtcNow
            });

            await db.SaveChangesAsync(cancellationToken);
            await transaction.CommitAsync(cancellationToken);

            logger.LogInformation("Dados padrão inicializados com sucesso.");
        }
        catch (Exception ex)
        {
            await transaction.RollbackAsync(cancellationToken);
            logger.LogError(ex, "Erro ao inicializar dados padrão.");
        }
    }
}
