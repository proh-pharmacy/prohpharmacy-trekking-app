using System.Net.Mail;
using System.Text;
using System.Text.Json;
using FluentEmail.Core;
using FluentEmail.Core.Interfaces;
using FluentEmail.Core.Models;
using FluentEmail.Razor;

namespace prohpharmacy_trekking_app.Services.Email;

// ── Models ────────────────────────────────────────────────────────────────────

public class StaffInvitationEmailModel
{
    public string StaffFullName { get; set; } = string.Empty;
    public string InvitationLink { get; set; } = string.Empty;
    public string ExpiresAt { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
}

public class StaffWelcomeEmailModel
{
    public string StaffFullName { get; set; } = string.Empty;
    public string EmployeeNumber { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public string InitialPassword { get; set; } = string.Empty;
    public string LoginUrl { get; set; } = string.Empty;
    public string AppName { get; set; } = string.Empty;
    public string SupportEmail { get; set; } = string.Empty;
}

// ── Interface ─────────────────────────────────────────────────────────────────

public interface IEmailService
{
    Task SendStaffInvitationEmailAsync(string to, StaffInvitationEmailModel model);
    Task SendStaffWelcomeEmailAsync(string to, StaffWelcomeEmailModel model);
}

// ── Registration ──────────────────────────────────────────────────────────────

public static class EmailServiceExtensions
{
    public static IServiceCollection AddEmailServices(
        this IServiceCollection services, IConfiguration configuration)
    {
        var settings = configuration.GetSection("EmailSettings");
        var apiKey = settings["ResendApiKey"] ?? string.Empty;
        var fromEmail = settings["FromEmail"] ?? throw new InvalidOperationException("EmailSettings:FromEmail is required.");
        var fromName = settings["FromName"] ?? "Proh Pharmacy";

        if (string.IsNullOrWhiteSpace(apiKey))
        {
            services.AddFluentEmail(fromEmail, fromName).AddRazorRenderer();
            services.AddScoped<IEmailService, NullEmailService>();
            return services;
        }

        services.AddHttpClient<ResendSender>();
        services.AddTransient<ISender, ResendSender>();

        services
            .AddFluentEmail(fromEmail, fromName)
            .AddRazorRenderer();

        services.AddScoped<IEmailService, EmailService>();

        return services;
    }
}

// ── Resend HTTP Sender ────────────────────────────────────────────────────────

public class ResendSender : ISender
{
    private readonly HttpClient _http;
    private readonly IConfiguration _config;

    public ResendSender(HttpClient http, IConfiguration config)
    {
        _http = http;
        _config = config;
    }

    public SendResponse Send(IFluentEmail email, CancellationToken? token = null)
        => SendAsync(email, token).GetAwaiter().GetResult();

    public async Task<SendResponse> SendAsync(IFluentEmail email, CancellationToken? token = null)
    {
        var settings = _config.GetSection("EmailSettings");
        var apiKey = settings["ResendApiKey"] ?? string.Empty;
        var supportEmail = settings["SupportEmail"] ?? string.Empty;

        var payload = new
        {
            from = $"{email.Data.FromAddress.Name} <{email.Data.FromAddress.EmailAddress}>",
            to = email.Data.ToAddresses.Select(a => a.EmailAddress).ToArray(),
            reply_to = supportEmail,
            subject = email.Data.Subject,
            html = email.Data.Body
        };

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.resend.com/emails")
        {
            Headers = { { "Authorization", $"Bearer {apiKey}" } },
            Content = new StringContent(JsonSerializer.Serialize(payload), Encoding.UTF8, "application/json")
        };

        var ct = token ?? CancellationToken.None;
        var response = await _http.SendAsync(request, ct);

        if (response.IsSuccessStatusCode)
            return new SendResponse();

        var error = await response.Content.ReadAsStringAsync(ct);
        return new SendResponse { ErrorMessages = [$"Resend error {(int)response.StatusCode}: {error}"] };
    }
}

// ── No-op fallback (dev / unconfigured) ───────────────────────────────────────

public class NullEmailService : IEmailService
{
    private readonly ILogger<NullEmailService> _logger;

    public NullEmailService(ILogger<NullEmailService> logger) => _logger = logger;

    public Task SendStaffInvitationEmailAsync(string to, StaffInvitationEmailModel model)
    {
        _logger.LogWarning("[NullEmailService] Invitation email NOT sent to {Email} — configure EmailSettings:ResendApiKey to enable.", to);
        return Task.CompletedTask;
    }

    public Task SendStaffWelcomeEmailAsync(string to, StaffWelcomeEmailModel model)
    {
        _logger.LogWarning("[NullEmailService] Welcome email NOT sent to {Email} — configure EmailSettings:ResendApiKey to enable.", to);
        return Task.CompletedTask;
    }
}
