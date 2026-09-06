using Carter;
using FluentEmail.Core;
using prohpharmacy_trekking_app.Services.Email;

namespace prohpharmacy_trekking_app.Preview;

public class EmailPreviewModule : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapGet("preview/invitation-email", (IFluentEmailFactory factory) =>
        {
            var model = new StaffInvitationEmailModel
            {
                StaffFullName = "Kwame Asante",
                InvitationLink = "https://yourapp.com/accept-invitation?token=SAMPLE_TOKEN_FOR_PREVIEW",
                ExpiresAt = DateTime.UtcNow.AddHours(48).ToString("dd MMM yyyy, h:mm tt") + " UTC",
                AppName = "Proh Pharmacy Trekking",
                SupportEmail = "support@prohpharmacy.com"
            };

            var templatePath = Path.Combine(Directory.GetCurrentDirectory(), "Templates", "StaffInvitationEmail.cshtml");
            var email = factory.Create().UsingTemplateFromFile(templatePath, model);
            return Results.Content(email.Data.Body, "text/html");
        })
        .WithTags("Preview")
        .WithSummary("Preview — Staff Invitation Email")
        .AllowAnonymous();

        app.MapGet("preview/welcome-email", (IFluentEmailFactory factory) =>
        {
            var model = new StaffWelcomeEmailModel
            {
                StaffFullName = "Ama Owusu",
                EmployeeNumber = "EMP-2026-0012",
                Email = "a.owusu@prohpharmacy.com",
                InitialPassword = "amaowusu",
                LoginUrl = "https://yourapp.com/login",
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
    }
}
