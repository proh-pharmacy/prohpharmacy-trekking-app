using System.Net;
using System.Net.Mail;
using FluentEmail.Core;
using FluentEmail.Razor;
using FluentEmail.Smtp;

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
        var host = settings["Host"] ?? throw new InvalidOperationException("EmailSettings:Host is required.");
        var port = int.Parse(settings["Port"] ?? "587");
        var username = settings["Username"] ?? string.Empty;
        var password = settings["Password"] ?? string.Empty;
        var fromEmail = settings["FromEmail"] ?? throw new InvalidOperationException("EmailSettings:FromEmail is required.");
        var fromName = settings["FromName"] ?? "Proh Pharmacy";
        var enableSsl = bool.Parse(settings["EnableSsl"] ?? "true");

        var smtpClient = new SmtpClient(host, port)
        {
            Credentials = new NetworkCredential(username, password),
            EnableSsl = enableSsl
        };

        services
            .AddFluentEmail(fromEmail, fromName)
            .AddRazorRenderer()
            .AddSmtpSender(smtpClient);

        services.AddScoped<IEmailService, EmailService>();

        return services;
    }
}
