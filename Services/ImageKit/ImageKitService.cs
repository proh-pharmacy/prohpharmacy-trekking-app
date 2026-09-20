using System.Net.Http.Headers;
using System.Text;
using System.Text.Json;

namespace prohpharmacy_trekking_app.Services.ImageKit;

public class ImageKitService(IConfiguration configuration, IHttpClientFactory httpClientFactory,
    ILogger<ImageKitService> logger)
{
    private readonly string _privateKey = (configuration["ImageKitSettings:PrivateKey"]
        ?? throw new InvalidOperationException("ImageKitSettings:PrivateKey is not configured.")).Trim();

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

        using var client = httpClientFactory.CreateClient("imagekit");
        var credentials = Convert.ToBase64String(Encoding.UTF8.GetBytes($"{_privateKey}:"));
        client.DefaultRequestHeaders.Authorization = new AuthenticationHeaderValue("Basic", credentials);
        logger.LogInformation("ImageKit upload — key length: {Len}, first 8: {Start}, credentials length: {CredLen}",
            _privateKey.Length, _privateKey[..Math.Min(8, _privateKey.Length)], credentials.Length);

        using var content = new MultipartFormDataContent();
        content.Add(new ByteArrayContent(bytes), "file", fileName);
        content.Add(new StringContent(fileName), "fileName");
        content.Add(new StringContent($"/prohpharmacy/{folder.Trim('/')}"), "folder");

        var response = await client.PostAsync("https://upload.imagekit.io/api/v1/files/upload", content);
        var body = await response.Content.ReadAsStringAsync();

        if (!response.IsSuccessStatusCode)
            throw new InvalidOperationException($"ImageKit upload failed ({(int)response.StatusCode}): {body}");

        using var doc = JsonDocument.Parse(body);
        var url = doc.RootElement.GetProperty("url").GetString();

        return url ?? throw new InvalidOperationException("ImageKit upload succeeded but returned no URL.");
    }
}
