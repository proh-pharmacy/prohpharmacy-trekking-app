using FluentEmail.Core;

namespace prohpharmacy_trekking_app.Services.Email;

public class EmailService : IEmailService
{
    private readonly IFluentEmailFactory _factory;
    private readonly IConfiguration _configuration;
    private readonly ILogger<EmailService> _logger;

    public EmailService(IFluentEmailFactory factory, IConfiguration configuration, ILogger<EmailService> logger)
    {
        _factory = factory;
        _configuration = configuration;
        _logger = logger;
    }

    public async Task SendStaffInvitationEmailAsync(string to, StaffInvitationEmailModel model)
    {
        try
        {
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "StaffInvitationEmail.cshtml");

            var response = await _factory.Create()
                .To(to)
                .Subject($"You're invited to join {model.AppName}")
                .UsingTemplateFromFile(templatePath, model)
                .SendAsync();

            if (!response.Successful)
                _logger.LogWarning("Failed to send invitation email to {Email}: {Errors}",
                    to, string.Join(", ", response.ErrorMessages));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending invitation email to {Email}", to);
        }
    }

    public async Task SendStaffWelcomeEmailAsync(string to, StaffWelcomeEmailModel model)
    {
        try
        {
            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "StaffWelcomeEmail.cshtml");

            var response = await _factory.Create()
                .To(to)
                .Subject($"Welcome to {model.AppName} — Your account is ready")
                .UsingTemplateFromFile(templatePath, model)
                .SendAsync();

            if (!response.Successful)
                _logger.LogWarning("Failed to send welcome email to {Email}: {Errors}",
                    to, string.Join(", ", response.ErrorMessages));
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error sending welcome email to {Email}", to);
        }
    }
}
