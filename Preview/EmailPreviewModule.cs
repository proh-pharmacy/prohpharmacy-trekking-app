using Carter;
using FluentEmail.Core;
using prohpharmacy_trekking_app.Services.Email;

namespace prohpharmacy_trekking_app.Preview;

public class EmailPreviewModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("preview/welcome-email", (IFluentEmailFactory factory) =>
        {
            var model = new StaffWelcomeEmailModel
            {
                StaffFullName = "Ama Owusu",
                EmployeeNumber = "EMP-2026-0012",
                Email = "a.owusu@prohpharmacy.com",
                InitialPassword = "amaowusu",
                LoginUrl = "https://yourapp.com/auth/reset-password?token=SAMPLE_SETUP_TOKEN_FOR_PREVIEW",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };

            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "StaffWelcomeEmail.cshtml");
            var email = factory.Create().UsingTemplateFromFile(templatePath, model);
            return Results.Content(email.Data.Body, "text/html");
        })
        .WithTags("Preview")
        .WithSummary("Preview — Staff Welcome Email")
        .AllowAnonymous();

        app.MapGet("preview/trek-assignment-email", (IFluentEmailFactory factory) =>
        {
            var model = new TrekAssignmentEmailModel
            {
                RecipientName = "Kofi Mensah",
                TrekNumber = "TRK-00001",
                ScheduledDate = "06 Sep 2026",
                DriverName = "Kwame Asante",
                BranchName = "Tema Branch",
                DriverLinkUrl = "https://yourapp.com/treks/driver?token=00000000-0000-0000-0000-000000000001",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };

            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "TrekAssignmentEmail.cshtml");
            var email = factory.Create().UsingTemplateFromFile(templatePath, model);
            return Results.Content(email.Data.Body, "text/html");
        })
        .WithTags("Preview")
        .WithSummary("Preview — Trek Assignment Email")
        .AllowAnonymous();

        app.MapPost("preview/send-welcome-email", async (IEmailService emailService) =>
        {
            var model = new StaffWelcomeEmailModel
            {
                StaffFullName = "Ama Owusu",
                EmployeeNumber = "EMP-2026-0012",
                Email = "a.owusu@prohpharmacy.com",
                InitialPassword = "amaowusu",
                LoginUrl = "https://yourapp.com/auth/reset-password?token=SAMPLE_SETUP_TOKEN_FOR_PREVIEW",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };
            await emailService.SendStaffWelcomeEmailAsync("akorlicourage@gmail.com", model);
            return Results.Ok("Welcome email sent to akorlicourage@gmail.com");
        })
        .WithTags("Preview")
        .WithSummary("Send sample — Staff Welcome Email")
        .AllowAnonymous();

        app.MapGet("preview/password-reset-email", (IFluentEmailFactory factory) =>
        {
            var model = new PasswordResetEmailModel
            {
                StaffFullName = "Kwame Asante",
                ResetLink = "https://yourapp.com/auth/reset-password?token=SAMPLE_TOKEN_FOR_PREVIEW",
                ExpiresAt = DateTime.UtcNow.AddHours(1).ToString("dd MMM yyyy, h:mm tt") + " UTC",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };

            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "PasswordResetEmail.cshtml");
            var email = factory.Create().UsingTemplateFromFile(templatePath, model);
            return Results.Content(email.Data.Body, "text/html");
        })
        .WithTags("Preview")
        .WithSummary("Preview — Password Reset Email")
        .AllowAnonymous();

        app.MapPost("preview/send-password-reset-email", async (IEmailService emailService) =>
        {
            var model = new PasswordResetEmailModel
            {
                StaffFullName = "Kwame Asante",
                ResetLink = "https://yourapp.com/auth/reset-password?token=SAMPLE_TOKEN_FOR_PREVIEW",
                ExpiresAt = DateTime.UtcNow.AddHours(1).ToString("dd MMM yyyy, h:mm tt") + " UTC",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };
            await emailService.SendPasswordResetEmailAsync("akorlicourage@gmail.com", model);
            return Results.Ok("Password reset email sent to akorlicourage@gmail.com");
        })
        .WithTags("Preview")
        .WithSummary("Send sample — Password Reset Email")
        .AllowAnonymous();

        app.MapPost("preview/send-trek-assignment-email", async (IEmailService emailService) =>
        {
            var model = new TrekAssignmentEmailModel
            {
                RecipientName = "Kofi Mensah",
                TrekNumber = "TRK-00001",
                ScheduledDate = "06 Sep 2026",
                DriverName = "Kwame Asante",
                BranchName = "Tema Branch",
                DriverLinkUrl = "https://yourapp.com/treks/driver?token=00000000-0000-0000-0000-000000000001",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };
            await emailService.SendTrekAssignmentEmailAsync("akorlicourage@gmail.com", model, []);
            return Results.Ok("Trek assignment email sent to akorlicourage@gmail.com");
        })
        .WithTags("Preview")
        .WithSummary("Send sample — Trek Assignment Email")
        .AllowAnonymous();
    }
}
