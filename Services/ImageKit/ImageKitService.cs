using Imagekit;
using Imagekit.Models.Files;

namespace prohpharmacy_trekking_app.Services.ImageKit;

public class ImageKitService(IConfiguration configuration)
{
    private readonly ImageKitClient _client = new()
    {
        PrivateKey = configuration["ImageKitSettings:PrivateKey"]
            ?? throw new InvalidOperationException("ImageKitSettings:PrivateKey is not configured.")
    };

    public async Task<string> UploadAsync(IFormFile file, string folder)
    {
        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid()}{extension}";

        using var stream = file.OpenReadStream();

        var response = await _client.Files.Upload(new FileUploadParams
        {
            File = stream,
            FileName = fileName,
            Folder = $"/prohpharmacy/{folder.Trim('/')}"
        });

        return response.Url
            ?? throw new InvalidOperationException("ImageKit upload succeeded but returned no URL.");
    }
}
