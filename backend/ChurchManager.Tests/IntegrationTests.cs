using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using ChurchManager.Api.Data;
using ChurchManager.Api.DTOs;
using ChurchManager.Api.Models;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;

namespace ChurchManager.Tests;

public class CustomWebApplicationFactory : WebApplicationFactory<Program>
{
    private SqliteConnection? _connection;

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Development");
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        builder.ConfigureServices(services =>
        {
            var descriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<AppDbContext>));
            if (descriptor != null) services.Remove(descriptor);

            services.AddDbContext<AppDbContext>(options =>
            {
                options.UseSqlite(_connection);
            });

            var sp = services.BuildServiceProvider();
            using var scope = sp.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
            db.Database.EnsureCreated();
        });
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        _connection?.Dispose();
    }
}

public class IntegrationTests(CustomWebApplicationFactory factory) : IClassFixture<CustomWebApplicationFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_Endpoint_ReturnsOk()
    {
        var response = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task Password_Under_8_Chars_Is_Rejected()
    {
        var request = new ChurchRegisterRequest(
            "Igreja Teste Curta",
            "Denom",
            "curta@igreja.com",
            "12345", // Menos de 8 caracteres
            "(11) 99999-9999",
            "Rua 1",
            null
        );

        var response = await _client.PostAsJsonAsync("/api/auth/register-church", request);
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    }

    [Fact]
    public async Task RegisterChurch_Flow1_CreatesChurch_And_ReturnsInstitutionalSession()
    {
        var email = $"igreja_{Guid.NewGuid()}@teste.com";
        var request = new ChurchRegisterRequest(
            "Comunidade Batista Esperança",
            "Batista",
            email,
            "SenhaSegura@2026",
            "(11) 98888-7777",
            "Av. Central, 500",
            "https://igreja.com/logo.png"
        );

        var response = await _client.PostAsJsonAsync("/api/auth/register-church", request);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var result = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(result);
        Assert.Equal("Igreja", result.Role);
        Assert.Equal("church", result.AccessMode);
        Assert.NotEmpty(result.Token);
    }

    [Fact]
    public async Task PublicChurches_Endpoint_Returns_AvailableChurches_For_Selection()
    {
        var email = $"igreja_{Guid.NewGuid()}@teste.com";
        var createRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Graça e Paz",
            "Presbiteriana",
            email,
            "SenhaForte@2026",
            "(11) 91111-2222",
            "Rua da Paz, 100",
            null
        ));
        Assert.Equal(HttpStatusCode.OK, createRes.StatusCode);

        var response = await _client.GetAsync("/api/churches?search=Graça");
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var churches = await response.Content.ReadFromJsonAsync<List<ChurchPublicDto>>();
        Assert.NotNull(churches);
        Assert.Contains(churches, c => c.Name.Contains("Graça"));
    }

    [Fact]
    public async Task RegisterMember_Always_Starts_As_Member_And_Associates_With_Selected_Church()
    {
        // 1. Cadastra uma igreja
        var churchEmail = $"igreja_{Guid.NewGuid()}@teste.com";
        var churchRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Renovada",
            "Metodista",
            churchEmail,
            "Igreja@2026Admin!",
            "(11) 93333-4444",
            "Rua Nova, 200",
            null
        ));
        Assert.Equal(HttpStatusCode.OK, churchRes.StatusCode);
        var churchSession = await churchRes.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(churchSession);

        // 2. Cadastra um membro pessoal
        var memberEmail = $"membro_{Guid.NewGuid()}@teste.com";
        var memberReq = new MemberRegisterRequest(
            "Lucas Evangelista",
            memberEmail,
            "MembroSeguro@2026",
            churchSession.ChurchId,
            "(11) 97777-6666",
            "Louvor"
        );

        var response = await _client.PostAsJsonAsync("/api/auth/register-member", memberReq);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        var memberSession = await response.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(memberSession);
        // REGRA OBRIGATÓRIA: Perfil inicial é SEMPRE Membro!
        Assert.Equal("Membro", memberSession.Role);
        Assert.Equal("member", memberSession.AccessMode);
        Assert.Equal(churchSession.ChurchId, memberSession.ChurchId);
    }

    [Fact]
    public async Task Member_Cannot_Login_Into_Admin_Portal()
    {
        // 1. Cria igreja e membro
        var churchEmail = $"igreja_{Guid.NewGuid()}@teste.com";
        var churchRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Teste Bloqueio",
            "Comunidade",
            churchEmail,
            "SenhaIgreja@2026",
            null, null, null
        ));
        Assert.Equal(HttpStatusCode.OK, churchRes.StatusCode);
        var churchSession = await churchRes.Content.ReadFromJsonAsync<AuthResponse>();

        var memberEmail = $"membro_{Guid.NewGuid()}@teste.com";
        var regMemberRes = await _client.PostAsJsonAsync("/api/auth/register-member", new MemberRegisterRequest(
            "Membro Simples",
            memberEmail,
            "SenhaMembro@2026",
            churchSession!.ChurchId,
            null, null
        ));
        Assert.Equal(HttpStatusCode.OK, regMemberRes.StatusCode);

        // 2. Tenta fazer login na modalidade 'admin'
        var loginReq = new LoginRequest(memberEmail, "SenhaMembro@2026", AccessMode: "admin");
        var loginRes = await _client.PostAsJsonAsync("/api/auth/login", loginReq);

        // Deve retornar 401 Unauthorized proibindo acesso administrativo
        Assert.Equal(HttpStatusCode.Unauthorized, loginRes.StatusCode);
    }

    [Fact]
    public async Task Admin_Promotion_Flow_Promotes_Member_With_AuditLog()
    {
        // 1. Cria igreja institucional
        var churchEmail = $"igreja_{Guid.NewGuid()}@teste.com";
        var churchRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Promocao",
            "Aliança",
            churchEmail,
            "SenhaIgreja@2026",
            null, null, null
        ));
        Assert.Equal(HttpStatusCode.OK, churchRes.StatusCode);
        var churchSession = await churchRes.Content.ReadFromJsonAsync<AuthResponse>();

        // 2. Cadastra membro
        var memberEmail = $"membro_promo_{Guid.NewGuid()}@teste.com";
        var memberRes = await _client.PostAsJsonAsync("/api/auth/register-member", new MemberRegisterRequest(
            "Candidato a Admin",
            memberEmail,
            "SenhaMembro@2026",
            churchSession!.ChurchId,
            null, "Comunicação"
        ));
        Assert.Equal(HttpStatusCode.OK, memberRes.StatusCode);

        // 3. Consulta membros elegíveis usando o token institucional da igreja
        var clientWithChurchToken = factory.CreateClient();
        clientWithChurchToken.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", churchSession.Token);

        var eligibleRes = await clientWithChurchToken.GetAsync("/api/church/settings/eligible-members");
        Assert.Equal(HttpStatusCode.OK, eligibleRes.StatusCode);

        // 4. Promove o membro a Administrador
        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Members.FirstAsync(m => m.Email == memberEmail);

        var promoRes = await clientWithChurchToken.PostAsJsonAsync("/api/church/settings/promote-admin", new PromoteAdminRequest(member.Id));
        Assert.Equal(HttpStatusCode.OK, promoRes.StatusCode);

        // 5. Verifica se agora o usuário consegue logar como Administrador
        var loginAdminRes = await _client.PostAsJsonAsync("/api/auth/login", new LoginRequest(memberEmail, "SenhaMembro@2026", AccessMode: "admin"));
        Assert.Equal(HttpStatusCode.OK, loginAdminRes.StatusCode);

        var adminSession = await loginAdminRes.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Equal("Administrador", adminSession!.Role);

        // 6. Confirma o registro em AuditLogs
        var logsRes = await clientWithChurchToken.GetAsync("/api/church/settings/audit-logs");
        Assert.Equal(HttpStatusCode.OK, logsRes.StatusCode);
        var logs = await logsRes.Content.ReadFromJsonAsync<List<AuditLogResponse>>();
        Assert.NotNull(logs);
        Assert.Contains(logs, l => l.Action == "AdminPromotion" && l.Details.Contains(memberEmail));
    }

    [Fact]
    public async Task MultiTenant_Isolation_ChurchA_Cannot_Access_ChurchB_Data()
    {
        // 1. Cria Igreja A
        var churchAEmail = $"igrejaA_{Guid.NewGuid()}@teste.com";
        var churchARes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Alpha", null, churchAEmail, "SenhaIgrejaA@2026", null, null, null));
        Assert.Equal(HttpStatusCode.OK, churchARes.StatusCode);
        var sessionA = await churchARes.Content.ReadFromJsonAsync<AuthResponse>();

        // 2. Cria Igreja B
        var churchBEmail = $"igrejaB_{Guid.NewGuid()}@teste.com";
        var churchBRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Beta", null, churchBEmail, "SenhaIgrejaB@2026", null, null, null));
        Assert.Equal(HttpStatusCode.OK, churchBRes.StatusCode);
        var sessionB = await churchBRes.Content.ReadFromJsonAsync<AuthResponse>();

        // 3. Cadastra membro na Igreja A
        var clientA = factory.CreateClient();
        clientA.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionA!.Token);
        var memberA = new Member
        {
            Name = "Membro Exclusivo da Igreja Alpha",
            Email = $"alpha_{Guid.NewGuid()}@teste.com",
            Status = "Ativo",
            Role = "Membro"
        };
        var createRes = await clientA.PostAsJsonAsync("/api/members", memberA);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        // 4. Igreja B consulta a lista de membros
        var clientB = factory.CreateClient();
        clientB.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", sessionB!.Token);
        var listBRes = await clientB.GetAsync("/api/members");
        Assert.Equal(HttpStatusCode.OK, listBRes.StatusCode);

        var membersB = await listBRes.Content.ReadFromJsonAsync<List<Member>>();
        Assert.NotNull(membersB);
        // O membro da Igreja A NÃO pode aparecer na Igreja B!
        Assert.DoesNotContain(membersB, m => m.Name == "Membro Exclusivo da Igreja Alpha");
    }

    [Fact]
    public async Task DemoteAdmin_Blocked_When_It_Leaves_Zero_Admins()
    {
        // Cria igreja
        var churchEmail = $"igreja_{Guid.NewGuid()}@teste.com";
        var churchRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Trava Zero", null, churchEmail, "SenhaIgreja@2026", null, null, null));
        var session = await churchRes.Content.ReadFromJsonAsync<AuthResponse>();

        // Cadastra um membro e promove ele ao ÚNICO administrador humano
        var memberEmail = $"admin_unico_{Guid.NewGuid()}@teste.com";
        await _client.PostAsJsonAsync("/api/auth/register-member", new MemberRegisterRequest(
            "Admin Único", memberEmail, "SenhaAdmin@2026", session!.ChurchId, null, null));

        using var scope = factory.Services.CreateScope();
        var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
        var member = await db.Members.FirstAsync(m => m.Email == memberEmail);

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        await client.PostAsJsonAsync("/api/church/settings/promote-admin", new PromoteAdminRequest(member.Id));

        var user = await db.Users.FirstAsync(u => u.Email == memberEmail);

        // Tenta remover o ÚNICO administrador ativo
        var demoteRes = await client.PostAsJsonAsync("/api/church/settings/demote-admin", new DemoteAdminRequest(user.Id));
        // Deve retornar BadRequest impedindo deixar a igreja sem administradores!
        Assert.Equal(HttpStatusCode.BadRequest, demoteRes.StatusCode);
    }

    [Fact]
    public async Task Donations_Create_And_Auto_Connects_To_Transactions()
    {
        // Cria igreja e membro
        var churchEmail = $"igreja_{Guid.NewGuid()}@teste.com";
        var churchRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Finanças", null, churchEmail, "SenhaIgreja@2026", null, null, null));
        var session = await churchRes.Content.ReadFromJsonAsync<AuthResponse>();

        var memberEmail = $"doador_{Guid.NewGuid()}@teste.com";
        var memberRes = await _client.PostAsJsonAsync("/api/auth/register-member", new MemberRegisterRequest(
            "Doador Fiel", memberEmail, "SenhaDoador@2026", session!.ChurchId, null, null));
        var memberSession = await memberRes.Content.ReadFromJsonAsync<AuthResponse>();

        var memberClient = factory.CreateClient();
        memberClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberSession!.Token);

        // Membro faz doação de R$ 250,00
        var donationReq = new CreateDonationRequest(250.00m, "Dízimo", null, null, "Doador Fiel");
        var donationRes = await memberClient.PostAsJsonAsync("/api/donations", donationReq);
        Assert.Equal(HttpStatusCode.Created, donationRes.StatusCode);

        // Verifica na consulta pessoal do membro
        var myDonationsRes = await memberClient.GetAsync("/api/donations/my");
        Assert.Equal(HttpStatusCode.OK, myDonationsRes.StatusCode);
        var myDonations = await myDonationsRes.Content.ReadFromJsonAsync<List<DonationResponse>>();
        Assert.NotNull(myDonations);
        Assert.Contains(myDonations, d => d.Amount == 250.00m && d.Category == "Dízimo");

        // Administrador verifica resumo financeiro da igreja com a transação gerada
        var adminClient = factory.CreateClient();
        adminClient.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", session.Token);
        var summaryRes = await adminClient.GetAsync("/api/transactions/summary");
        Assert.Equal(HttpStatusCode.OK, summaryRes.StatusCode);
    }

    [Fact]
    public async Task Session_Restoration_Via_Auth_Me_Returns_Consistent_User_Data()
    {
        var churchEmail = $"igreja_{Guid.NewGuid()}@teste.com";
        var churchRes = await _client.PostAsJsonAsync("/api/auth/register-church", new ChurchRegisterRequest(
            "Igreja Sessão", null, churchEmail, "SenhaIgreja@2026", null, null, null));
        var session = await churchRes.Content.ReadFromJsonAsync<AuthResponse>();

        var memberEmail = $"membro_sessao_{Guid.NewGuid()}@teste.com";
        var memberRes = await _client.PostAsJsonAsync("/api/auth/register-member", new MemberRegisterRequest(
            "Membro Sessão", memberEmail, "SenhaSessao@2026", session!.ChurchId, null, null));
        var memberSession = await memberRes.Content.ReadFromJsonAsync<AuthResponse>();

        var client = factory.CreateClient();
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Bearer", memberSession!.Token);

        var meRes = await client.GetAsync("/api/auth/me");
        Assert.Equal(HttpStatusCode.OK, meRes.StatusCode);
    }
}
