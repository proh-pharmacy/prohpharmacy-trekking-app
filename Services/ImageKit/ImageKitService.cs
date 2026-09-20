using Imagekit;
using Imagekit.Models.Files;

namespace prohpharmacy_trekking_app.Services.ImageKit;

public class ImageKitService(IConfiguration configuration)
{
    private readonly ImageKitClient _client = new()
    {
        PrivateKey = configuration["ImageKitSettings:PrivateKey"]
            ?? throw new InvalidOperationException("ImageKitSettings:PrivateKey is not configured."),
        MaxRetries = 0
    };

    public async Task<string> UploadAsync(IFormFile file, string folder)
    {
        var extension = Path.GetExtension(file.FileName);
        var fileName = $"{Guid.NewGuid()}{extension}";

        byte[] bytes;
        using (var ms = new MemoryStream())
        {
            await file.CopyToAsync(ms);
            bytes = ms.ToArray();
        }

        Exception? lastException = null;
        for (var attempt = 0; attempt < 3; attempt++)
        {
            try
            {
                var response = await _client.Files.Upload(new FileUploadParams
                {
                    File = bytes,
                    FileName = fileName,
                    Folder = $"/prohpharmacy/{folder.Trim('/')}"
                });

                return response.Url
                    ?? throw new InvalidOperationException("ImageKit upload succeeded but returned no URL.");
            }
            catch (Exception ex) when (attempt < 2)
            {
                lastException = ex;
            }
        }

        throw lastException!;
    }
}
