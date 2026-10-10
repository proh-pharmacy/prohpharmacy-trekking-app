using FluentValidation;
using Microsoft.Extensions.Options;
using prohpharmacy_trekking_app.Services.DocumentExtraction;

namespace prohpharmacy_trekking_app.Features.Products.Matching;

public sealed class MatchProductFilesValidator : AbstractValidator<MatchProductFilesCommand>
{
    public MatchProductFilesValidator(IOptions<DocumentMatchingOptions> options)
    {
        var limits = options.Value;
        RuleFor(x => x.Files).NotEmpty().Must(files => files.Count <= limits.MaxFiles)
            .WithMessage($"Upload between 1 and {limits.MaxFiles} files.");
        RuleFor(x => x.Files).Must(files => files.Sum(f => f.Length) <= limits.MaxTotalBytes)
            .WithMessage("The batch exceeds the total upload-size limit.");
        RuleFor(x => x.ExcelMappings).Must(m => m.Select(x => x.FileIndex).Distinct().Count() == m.Count)
            .WithMessage("Only one Excel mapping per file is allowed.");
        RuleForEach(x => x.ExcelMappings).ChildRules(mapping =>
        {
            mapping.RuleFor(m => m.HeaderRow).InclusiveBetween(1, 100);
            mapping.RuleFor(m => m.Worksheet).MaximumLength(100);
            mapping.RuleFor(m => m.ProductNameColumn).MaximumLength(100);
            mapping.RuleFor(m => m.QuantityColumn).MaximumLength(100);
            mapping.RuleFor(m => m.UnitColumn).MaximumLength(100);
            mapping.RuleFor(m => m.BasicQtyColumn).MaximumLength(100);
            mapping.RuleFor(m => m.PackagingQtyColumn).MaximumLength(100);
        });
        RuleFor(x => x).Must(x => x.ExcelMappings.All(m => m.FileIndex >= 0 && m.FileIndex < x.Files.Count &&
                Path.GetExtension(x.Files[m.FileIndex].FileName).Equals(".xlsx", StringComparison.OrdinalIgnoreCase)))
            .WithMessage("Excel mappings must reference an uploaded .xlsx file by its zero-based index.");
    }
}
