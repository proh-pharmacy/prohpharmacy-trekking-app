using Carter;
using MediatR;
using Microsoft.EntityFrameworkCore;
using prohpharmacy_trekking_app.Database;
using prohpharmacy_trekking_app.Extensions;
using prohpharmacy_trekking_app.Services.ImageKit;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Staff;

public static class UploadStaffPhoto
{
    public class Command : IRequest<Result<PhotoResponse>>
    {
        public Guid StaffMemberId { get; set; }
        public IFormFile File { get; set; } = null!;
    }

    public class PhotoResponse
    {
        public Guid StaffMemberId { get; set; }
        public string ProfilePhotoUrl { get; set; } = string.Empty;
    }

    internal sealed class Handler(AppDbContext db, ImageKitService imageKit)
        : IRequestHandler<Command, Result<PhotoResponse>>
    {
        public async Task<Result<PhotoResponse>> Handle(Command request, CancellationToken cancellationToken)
        {
            var staff = await db.StaffMembers
                .FirstOrDefaultAsync(s => s.Id == request.StaffMemberId, cancellationToken);

            if (staff is null)
                return Result.Failure<PhotoResponse>(Error.CreateNotFoundError("Staff member not found."));

            var allowedTypes = new[] { "image/jpeg", "image/png", "image/webp" };
            if (!allowedTypes.Contains(request.File.ContentType.ToLower()))
                return Result.Failure<PhotoResponse>(Error.BadRequest("Only JPEG, PNG, and WebP images are accepted."));

            if (request.File.Length > 5 * 1024 * 1024)
                return Result.Failure<PhotoResponse>(Error.BadRequest("Photo must be 5 MB or less."));

            var url = await imageKit.UploadAsync(request.File, "staff/photos");

            staff.ProfilePhotoObjectKey = url;
            staff.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync(cancellationToken);

            return Result.Success(new PhotoResponse
            {
                StaffMemberId = staff.Id,
                ProfilePhotoUrl = url
            });
        }
    }
}

public class UploadStaffPhotoEndpoint : ICarterModule
{
    public void AddRoutes(IEndpointRouteBuilder app)
    {
        app.MapPost("api/v1/staff/{id:guid}/photo",
            async (Guid id, IFormFile file, ISender sender) =>
            {
                var result = await sender.Send(new UploadStaffPhoto.Command
                {
                    StaffMemberId = id,
                    File = file
                });

                return result.IsFailure
                    ? Results.UnprocessableEntity(result.Error)
                    : Results.Ok(result.Value);
            })
            .WithTags("Staff")
            .WithGroupName(SwaggerDoc.SwaggerEndpointDefinitions.Staff)
            .WithSummary("Upload staff passport photo")
            .WithDescription("Uploads a passport photo for a staff member to ImageKit. Accepts JPEG, PNG, or WebP up to 5 MB.")
            .Produces<UploadStaffPhoto.PhotoResponse>(200)
            .Produces<Error>(404)
            .Produces<Error>(422)
            .DisableAntiforgery()
            .RequireAuthorization();
    }
}
