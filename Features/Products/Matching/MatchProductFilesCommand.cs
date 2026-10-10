using MediatR;
using prohpharmacy_trekking_app.Services.DocumentExtraction;
using prohpharmacy_trekking_app.Shared;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class MatchProductFilesCommand : IRequest<Result<MatchProductFilesResponse>>
{
    public List<IFormFile> Files { get; set; } = [];
    public List<ExcelColumnMapping> ExcelMappings { get; set; } = [];
}
