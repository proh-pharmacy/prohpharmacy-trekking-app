using System.Text.Json.Serialization;
using Carter;
using Serilog;
using FluentValidation;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Features.Identity.Seeding;
using prohpharmacy_trekking_app.Features.Organisation.Seeding;
using prohpharmacy_trekking_app.Hubs;
using prohpharmacy_trekking_app.Services.Email;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Services.Traccar;
using prohpharmacy_trekking_app.Middlewares;
using prohpharmacy_trekking_app.Providers;
using prohpharmacy_trekking_app.Utilities;
using Scalar.AspNetCore;

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog((ctx, lc) => lc
    .ReadFrom.Configuration(ctx.Configuration)
    .WriteTo.Console());
var assembly = typeof(Program).Assembly;
var appKey = builder.Configuration.GetValue<string>("SiteSettings:AppKey")
    ?? throw new InvalidOperationException("SiteSettings:AppKey is not configured.");
var pathBase = builder.Configuration.GetValue<string>("PathBase") ?? string.Empty;

// ─── Forwarded Headers (for reverse-proxy / container deployments) ────────────
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders =
        ForwardedHeaders.XForwardedFor |
        ForwardedHeaders.XForwardedProto |
        ForwardedHeaders.XForwardedHost;
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

// ─── Core Infrastructure ──────────────────────────────────────────────────────
builder.Services.AddHttpContextAccessor();

// ─── Database ─────────────────────────────────────────────────────────────────
builder.Services.AddDbContext<AppDbContext>(options =>
{
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"));
});

// ─── Auth ─────────────────────────────────────────────────────────────────────
JWTStartupConfig.ConfigureJwt(builder.Services, builder.Configuration);
builder.Services.AddSingleton<JWTProvider>(new JWTProvider(appKey));
builder.Services.AddScoped<AuthProvider>();
builder.Services.AddAuthorization();

// ─── MediatR ─────────────────────────────────────────────────────────────────
builder.Services.AddMediatR(config => config.RegisterServicesFromAssemblies(assembly));

// ─── Carter (minimal-API module routing) ─────────────────────────────────────
builder.Services.AddCarter();

// ─── FluentValidation ────────────────────────────────────────────────────────
builder.Services.AddValidatorsFromAssembly(assembly);

// ─── AutoMapper ───────────────────────────────────────────────────────────────
builder.Services.AddAutoMapper(assembly);

// ─── Swagger / OpenAPI ────────────────────────────────────────────────────────
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen(option => SwaggerDoc.OpenAuthentication(option));

// ─── JSON Options ─────────────────────────────────────────────────────────────
builder.Services.Configure<Microsoft.AspNetCore.Http.Json.JsonOptions>(options =>
{
    options.SerializerOptions.ReferenceHandler = System.Text.Json.Serialization.ReferenceHandler.IgnoreCycles;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
});

// ─── Email ────────────────────────────────────────────────────────────────────
builder.Services.AddEmailServices(builder.Configuration);

// ─── ImageKit ─────────────────────────────────────────────────────────────────
builder.Services.AddScoped<ImageKitService>();

// ─── Traccar ──────────────────────────────────────────────────────────────────
builder.Services.AddTraccarServices(builder.Configuration);

// ─── SignalR ──────────────────────────────────────────────────────────────────
builder.Services.AddSignalR();

// ─── Caching & Problem Details ────────────────────────────────────────────────
builder.Services.AddMemoryCache();
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<CustomExceptionHandler>();

// ─── CORS ─────────────────────────────────────────────────────────────────────
const string CorsPolicy = "_allowedOrigins";
builder.Services.AddCors(options =>
{
    options.AddPolicy(name: CorsPolicy, policy =>
    {
        policy
            .WithOrigins(
                "http://localhost:3000",
                "http://localhost:5173",
                "https://trekking.prohpharmacy.com",
                "https://prohpharmacy.com")
            .AllowAnyHeader()
            .AllowAnyMethod()
            .AllowCredentials();
    });
});

// ─── Build ────────────────────────────────────────────────────────────────────
var app = builder.Build();

app.UseForwardedHeaders();

if (!string.IsNullOrEmpty(pathBase))
    app.UsePathBase(pathBase);

// Wire up Paginator's HttpContextAccessor and optional public base URL
Paginator.SetHttpContextAccessor(app.Services.GetRequiredService<IHttpContextAccessor>());
Paginator.SetPublicBaseUrl(builder.Configuration.GetValue<string>("Pagination:PublicBaseUrl"));

// ─── Swagger ──────────────────────────────────────────────────────────────────
app.UseSwagger();

app.MapScalarApiReference(options =>
{
    options.Title = "prohpharmacy-trekking API";
    options.Theme = ScalarTheme.Purple;
    options.DefaultHttpClient = new(ScalarTarget.CSharp, ScalarClient.HttpClient);
    options.WithOpenApiRoutePattern("/swagger/{documentName}/swagger.json");
    options.AddPreferredSecuritySchemes(["Bearer"]);
    options.WithCustomCss("a[href*=\"scalar.com\"], [data-testid*=\"scalar\"], .scalar-powered-by { display: none !important; }");
});

// ─── Middleware Pipeline ──────────────────────────────────────────────────────
app.UseSerilogRequestLogging();
app.UseCors(CorsPolicy);
app.UseMiddleware<JsonExceptionHandlingMiddleware>();
app.UseExceptionHandler();
if (!app.Environment.IsDevelopment())
    app.UseHttpsRedirection();
app.UseAuthentication();
app.UseAuthorization();

// ─── Endpoints ────────────────────────────────────────────────────────────────
app.MapCarter();
app.MapHub<TrackingHub>("/hubs/tracking");

// ─── Apply Pending Migrations + Seed ─────────────────────────────────────────
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    await db.Database.MigrateAsync();
    await RoleSeeder.SeedAsync(db);
    await GhanaRegionSeeder.SeedAsync(db);
}

app.Run();
