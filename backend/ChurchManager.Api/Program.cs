using System.Text;
using System.Security.Claims;
using System.Threading.RateLimiting;
using ChurchManager.Api.Data;
using ChurchManager.Api.Hubs;
using ChurchManager.Api.Interfaces;
using ChurchManager.Api.Middleware;
using ChurchManager.Api.Models;
using ChurchManager.Api.Services;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Npgsql;

var builder = WebApplication.CreateBuilder(args);

builder.WebHost.ConfigureKestrel(options => options.Limits.MaxRequestBodySize = 2_097_152); // 2 MB

var databaseProvider = (
    Environment.GetEnvironmentVariable("CHURCHMANAGER_DB_PROVIDER")
    ?? builder.Configuration["Database:Provider"]
    ?? "postgres").Trim();

var connectionString = Environment.GetEnvironmentVariable("CHURCHMANAGER_DB")
    ?? builder.Configuration.GetConnectionString("DefaultConnection")
    ?? "";

var usesPostgres = databaseProvider.Equals("postgres", StringComparison.OrdinalIgnoreCase)
    || databaseProvider.Equals("postgresql", StringComparison.OrdinalIgnoreCase);

if (!usesPostgres)
    throw new InvalidOperationException("CHURCHMANAGER_DB_PROVIDER deve ser 'postgres'. MySQL não é suportado.");

var jwtKey = Environment.GetEnvironmentVariable("JWT_KEY")
    ?? builder.Configuration["Jwt:Key"]
    ?? (builder.Environment.IsDevelopment() ? "LocalDevelopmentSecretKey-ChurchManager-2026-TccEtec-Secure!" : "");

var jwtIssuer = Environment.GetEnvironmentVariable("JWT_ISSUER") ?? builder.Configuration["Jwt:Issuer"] ?? "ChurchManager.Api";
var jwtAudience = Environment.GetEnvironmentVariable("JWT_AUDIENCE") ?? builder.Configuration["Jwt:Audience"] ?? "ChurchManager.Web";
var configuredFrontend = Environment.GetEnvironmentVariable("FRONTEND_ORIGIN") ?? builder.Configuration["FRONTEND_ORIGIN"] ?? "http://localhost:5173";

if (jwtKey.Length < 32)
    throw new InvalidOperationException("Configure JWT_KEY com pelo menos 32 caracteres.");

// Configuração do DbContext PostgreSQL com Npgsql
builder.Services.AddDbContext<AppDbContext>(options =>
{
    if (!string.IsNullOrWhiteSpace(connectionString))
    {
        var postgresConnectionString = PreparePostgresConnectionString(
            connectionString,
            requireSsl: !builder.Environment.IsDevelopment());

        options.UseNpgsql(postgresConnectionString);
    }
    else
    {
        // Fallback para design-time e inicialização local
        options.UseNpgsql("Host=localhost;Database=churchmanager_dev;Username=postgres;Password=postgres");
    }
});

// Registro de Serviços de Domínio e Segurança
builder.Services.AddScoped<IPasswordHasher<AppUser>, PasswordHasher<AppUser>>();
builder.Services.AddScoped<IPasswordHasher<Church>, PasswordHasher<Church>>();
builder.Services.AddScoped<IPasswordHasher<PlatformAdmin>, PasswordHasher<PlatformAdmin>>();
builder.Services.AddScoped<ITokenService, TokenService>();
builder.Services.AddScoped<IChurchNotificationService, ChurchNotificationService>();

// SignalR para atualizações em tempo real entre membros e administradores da mesma igreja
builder.Services.AddSignalR();

builder.Services.AddControllers();
builder.Services.Configure<ApiBehaviorOptions>(options =>
{
    options.InvalidModelStateResponseFactory = context =>
    {
        var errors = context.ModelState.Values
            .SelectMany(v => v.Errors)
            .Select(e => e.ErrorMessage)
            .FirstOrDefault();

        return new BadRequestObjectResult(new
        {
            message = errors ?? "Confira os campos obrigatórios e formatos informados."
        });
    };
});

builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// CORS
var originCandidates = new[]
{
    "http://localhost:5173",
    "http://127.0.0.1:5173",
    "http://localhost:3000",
    configuredFrontend,
    "https://churchmanager-tcc.onrender.com"
};

var allowedOrigins = originCandidates
    .Where(x => !string.IsNullOrWhiteSpace(x))
    .Distinct(StringComparer.OrdinalIgnoreCase)
    .ToArray();

builder.Services.AddCors(options => options.AddPolicy("Frontend", policy =>
{
    policy.WithOrigins(allowedOrigins)
        .AllowAnyHeader()
        .AllowAnyMethod()
        .AllowCredentials(); // Obrigatório para SignalR
}));

// Rate Limiting
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    var authLimit = builder.Environment.IsDevelopment() ? 1000 : 15;
    var loginLimit = builder.Environment.IsDevelopment() ? 1000 : 10;

    options.AddPolicy("Auth", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = authLimit,
            Window = TimeSpan.FromMinutes(1),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
    options.AddPolicy("Login", context => RateLimitPartition.GetFixedWindowLimiter(
        partitionKey: context.Connection.RemoteIpAddress?.ToString() ?? "unknown-client",
        factory: _ => new FixedWindowRateLimiterOptions
        {
            PermitLimit = loginLimit,
            Window = TimeSpan.FromMinutes(2),
            QueueLimit = 0,
            AutoReplenishment = true
        }));
});

// Autenticação JWT com suporte a SignalR
builder.Services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer(options =>
{
    options.MapInboundClaims = true;
    options.TokenValidationParameters = new TokenValidationParameters
    {
        ValidateIssuer = true,
        ValidIssuer = jwtIssuer,
        ValidateAudience = true,
        ValidAudience = jwtAudience,
        ValidateLifetime = true,
        RequireExpirationTime = true,
        RequireSignedTokens = true,
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwtKey)),
        ClockSkew = TimeSpan.FromSeconds(30)
    };

    options.Events = new JwtBearerEvents
    {
        OnMessageReceived = context =>
        {
            var accessToken = context.Request.Query["access_token"];
            var path = context.HttpContext.Request.Path;
            if (!string.IsNullOrEmpty(accessToken) && path.StartsWithSegments("/hub/church"))
            {
                context.Token = accessToken;
            }
            return Task.CompletedTask;
        },
        OnTokenValidated = async context =>
        {
            var principal = context.Principal;
            var role = principal?.FindFirstValue(ClaimTypes.Role) ?? string.Empty;
            var email = principal?.FindFirstValue(ClaimTypes.Email)?.Trim().ToLowerInvariant() ?? string.Empty;

            if (string.Equals(principal?.FindFirstValue("access_mode"), "superadmin", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var platformAdminId))
                {
                    context.Fail("Sessão de plataforma inválida.");
                    return;
                }
                using var platformScope = context.HttpContext.RequestServices.CreateScope();
                var platformDb = platformScope.ServiceProvider.GetRequiredService<AppDbContext>();
                var activePlatformAdmin = await platformDb.PlatformAdmins.AsNoTracking()
                    .AnyAsync(admin => admin.Id == platformAdminId && admin.IsActive && admin.Email == email);
                if (!activePlatformAdmin) context.Fail("A conta de plataforma foi desativada.");
                return;
            }

            if (string.IsNullOrWhiteSpace(email)
                || !int.TryParse(principal?.FindFirstValue("church_id"), out var churchId))
            {
                context.Fail("Sessão sem identidade institucional válida.");
                return;
            }

            using var scope = context.HttpContext.RequestServices.CreateScope();
            var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

            if (string.Equals(role, "Igreja", StringComparison.OrdinalIgnoreCase))
            {
                if (!int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var institutionalId)
                    || institutionalId != -churchId)
                {
                    context.Fail("Sessão institucional inválida.");
                    return;
                }

                var activeChurch = await db.Churches.AsNoTracking()
                    .AnyAsync(church => church.Id == churchId && church.IsActive && church.Email == email);

                if (!activeChurch) context.Fail("A instituição está inativa ou suas credenciais foram alteradas.");
                return;
            }

            if (!int.TryParse(principal?.FindFirstValue(ClaimTypes.NameIdentifier), out var userId))
            {
                context.Fail("Sessão sem usuário vinculado.");
                return;
            }

            var activeUser = await db.Users.AsNoTracking()
                .AnyAsync(user =>
                    user.Id == userId &&
                    user.ChurchId == churchId &&
                    user.IsActive &&
                    user.Role == role &&
                    user.Email == email);

            if (!activeUser) context.Fail("A conta foi desativada ou teve suas permissões alteradas.");
        }
    };
});

builder.Services.AddAuthorization();

var app = builder.Build();

app.UseMiddleware<GlobalExceptionMiddleware>();

app.Use(async (context, next) =>
{
    context.Response.Headers["X-Content-Type-Options"] = "nosniff";
    context.Response.Headers["X-Frame-Options"] = "DENY";
    context.Response.Headers["Referrer-Policy"] = "no-referrer";
    context.Response.Headers["Permissions-Policy"] = "camera=(), microphone=(), geolocation=()";
    if (!app.Environment.IsDevelopment()) context.Response.Headers["Strict-Transport-Security"] = "max-age=31536000";
    if (context.Request.Path.StartsWithSegments("/api"))
    {
        context.Response.Headers["Cache-Control"] = "no-store, max-age=0";
        context.Response.Headers["Pragma"] = "no-cache";
    }
    await next();
});

if (!app.Environment.IsDevelopment()) app.UseHsts();

app.UseRouting();
app.UseCors("Frontend");
app.UseRateLimiter();

app.UseSwagger();
app.UseSwaggerUI();

app.UseAuthentication();
app.UseAuthorization();

app.MapGet("/health", () => Results.Ok(new
{
    status = "ok",
    service = "ChurchManager API",
    version = "2.0.0",
    database = "PostgreSQL"
}));

app.MapGet("/health/ready", async (AppDbContext db, CancellationToken cancellationToken) =>
{
    try
    {
        var connected = await db.Database.CanConnectAsync(cancellationToken);
        return connected
            ? Results.Ok(new { status = "ok", database = "connected" })
            : Results.Json(new { status = "error", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
    catch
    {
        return Results.Json(new { status = "error", database = "unavailable" }, statusCode: StatusCodes.Status503ServiceUnavailable);
    }
});

app.MapControllers();
app.MapHub<ChurchHub>("/hub/church");

// Aplicação de migrações automáticas e seed se habilitado
if (app.Configuration.GetValue<bool>("Database:ApplyMigrations") ||
    string.Equals(Environment.GetEnvironmentVariable("DATABASE_APPLY_MIGRATIONS"), "true", StringComparison.OrdinalIgnoreCase))
{
    using var scope = app.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var userHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<AppUser>>();
    var churchHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<Church>>();
    var platformHasher = scope.ServiceProvider.GetRequiredService<IPasswordHasher<PlatformAdmin>>();
    try
    {
        await db.Database.MigrateAsync();
        await BootstrapPlatformAdminsAsync(db, platformHasher, app.Logger);
        // Dados de demonstração nunca são inseridos em produção.
        // Em produção, a criação da primeira igreja ocorre pelo fluxo institucional.
        if (app.Environment.IsDevelopment() &&
            app.Configuration.GetValue<bool>("Database:SeedDevelopmentData"))
        {
            await DatabaseSeeder.SeedAsync(db, userHasher, churchHasher, app.Logger);
        }
    }
    catch (Exception ex)
    {
        app.Logger.LogWarning(ex, "Erro ao aplicar migrações ou seed na inicialização.");
    }
}

app.Run();

static string PreparePostgresConnectionString(string value, bool requireSsl)
{
    if (Uri.TryCreate(value, UriKind.Absolute, out var uri)
        && (uri.Scheme.Equals("postgres", StringComparison.OrdinalIgnoreCase)
            || uri.Scheme.Equals("postgresql", StringComparison.OrdinalIgnoreCase)))
    {
        var userInfo = uri.UserInfo.Split(':', 2);
        var database = Uri.UnescapeDataString(uri.AbsolutePath.Trim('/'));
        if (userInfo.Length != 2 || string.IsNullOrWhiteSpace(database))
            throw new InvalidOperationException("A URL de conexão PostgreSQL está incompleta.");

        var parsedUri = new NpgsqlConnectionStringBuilder
        {
            Host = uri.Host,
            Port = uri.Port > 0 ? uri.Port : 5432,
            Database = database,
            Username = Uri.UnescapeDataString(userInfo[0]),
            Password = Uri.UnescapeDataString(userInfo[1]),
            SslMode = requireSsl ? SslMode.Require : SslMode.Prefer,
            Timeout = 15,
            CommandTimeout = 30,
            ApplicationName = "ChurchManager.Api"
        };
        return parsedUri.ConnectionString;
    }

    var parsed = new NpgsqlConnectionStringBuilder(value);
    if (requireSsl) parsed.SslMode = SslMode.Require;
    if (string.IsNullOrWhiteSpace(parsed.ApplicationName)) parsed.ApplicationName = "ChurchManager.Api";
    return parsed.ConnectionString;
}

public partial class Program { }


static async Task BootstrapPlatformAdminsAsync(AppDbContext db, IPasswordHasher<PlatformAdmin> hasher, ILogger logger)
{
    var raw = Environment.GetEnvironmentVariable("PLATFORM_ADMINS_JSON");
    var initialPassword = Environment.GetEnvironmentVariable("PLATFORM_ADMIN_INITIAL_PASSWORD");
    if (string.IsNullOrWhiteSpace(raw)) return;
    if (string.IsNullOrWhiteSpace(initialPassword) || initialPassword.Length < 12)
        throw new InvalidOperationException("PLATFORM_ADMIN_INITIAL_PASSWORD precisa ter pelo menos 12 caracteres para provisionar superadministradores.");

    var admins = System.Text.Json.JsonSerializer.Deserialize<List<PlatformAdminSeed>>(raw,
        new System.Text.Json.JsonSerializerOptions { PropertyNameCaseInsensitive = true }) ?? new();
    foreach (var seed in admins)
    {
        var username = seed.Username?.Trim().ToLowerInvariant();
        var email = seed.Email?.Trim().ToLowerInvariant();
        var name = seed.Name?.Trim();
        if (string.IsNullOrWhiteSpace(username) || string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(name))
            throw new InvalidOperationException("Cada superadministrador precisa conter username, name e email válidos.");
        var exists = await db.PlatformAdmins.AnyAsync(x => x.Username == username || x.Email == email);
        if (exists) continue;
        var admin = new PlatformAdmin { Username = username, Name = name, Email = email, IsActive = true, CreatedAt = DateTime.UtcNow };
        admin.PasswordHash = hasher.HashPassword(admin, initialPassword);
        db.PlatformAdmins.Add(admin);
    }
    await db.SaveChangesAsync();
    logger.LogInformation("Provisionamento inicial de superadministradores concluído (contas existentes preservadas).");
}

sealed record PlatformAdminSeed(string? Username, string? Name, string? Email);
