using Microsoft.Extensions.Options;
using System.Text.RegularExpressions;
using prohpharmacy_trekking_app.Services.ProductMatching;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public sealed class DocumentTableRowParser(IOptions<DocumentMatchingOptions> options)
{
    public IReadOnlyList<ExtractedProductRow> Parse(IReadOnlyList<DocumentToken> tokens, int page,
        CancellationToken cancellationToken, IReadOnlySet<string>? knownUnits = null)
    {
        var slope = EstimateRowSlope(tokens);
        var prepared = tokens.Where(t => !string.IsNullOrWhiteSpace(t.Text))
            .Select(t => (Token: t, Centre: (t.Top + t.Bottom) / 2 - slope * ((t.Left + t.Right) / 2)))
            .OrderBy(t => t.Centre).ThenBy(t => t.Token.Left).ToList();
        var lines = new List<List<DocumentToken>>();
        foreach (var item in prepared)
        {
            cancellationToken.ThrowIfCancellationRequested();
            var token = item.Token;
            if (string.IsNullOrWhiteSpace(token.Text)) continue;
            var line = lines.LastOrDefault(l => Math.Abs(l.Average(t =>
                (t.Top + t.Bottom) / 2 - slope * ((t.Left + t.Right) / 2)) - item.Centre)
                <= Math.Max(14, Math.Min(token.Bottom - token.Top, l[0].Bottom - l[0].Top) * 0.8));
            if (line is null) lines.Add([token]);
            else line.Add(token);
        }

        var rows = new List<ExtractedProductRow>();
        List<(string Key, double Left)>? headers = null;
        var headerLineIndex = lines.FindIndex(line =>
        {
            var cells = Cells(line.OrderBy(t => t.Left).ToList());
            var keys = cells.Select(c => DocumentColumnNames.Identify(c.Text)).Where(k => k is not null).ToList();
            return keys.Contains("name") && keys.Contains("quantity");
        });
        var hasTableHeader = headerLineIndex >= 0;
        var rowNumber = 0;
        for (var lineIndex = 0; lineIndex < lines.Count; lineIndex++)
        {
            var line = lines[lineIndex];
            cancellationToken.ThrowIfCancellationRequested();
            rowNumber++;
            var ordered = line.OrderBy(t => t.Left).ToList();
            var cells = Cells(ordered);
            var detected = cells.Select(c => (Key: DocumentColumnNames.Identify(c.Text), c.Left))
                .Where(c => c.Key is not null).Select(c => (Key: c.Key!, c.Left)).ToList();
            if (detected.Any(c => c.Key == "name") &&
                detected.Count(c => c.Key is "name" or "quantity" or "unit" or "basic" or "packaging") == 1)
                continue;
            if (detected.Any(c => c.Key == "name") &&
                (detected.Count(c => c.Key is "name" or "quantity" or "unit" or "basic" or "packaging") >= 2))
            {
                if (detected.Select(c => c.Key).Distinct().Count() != detected.Count)
                    throw new DocumentExtractionException("AmbiguousColumns", "Multiple product tables or duplicate headers were detected.");
                headers = cells.Select((c, index) =>
                    (Key: DocumentColumnNames.Identify(c.Text) ?? $"ignored{index}", c.Left)).ToList();
                continue;
            }
            if (headers is null && hasTableHeader)
                continue;
            if (headers is not null && IsDocumentFooter(ordered))
                break;
            var row = new ExtractedProductRow { Page = page, Row = rowNumber };
            if (headers is null)
            {
                // Some OCR table pipelines return body cells but omit the
                // visible header row. Recover the same two-column meaning from
                // the right-hand quantity tokens before matching the product.
                var split = SplitUnstructuredLine(ordered, knownUnits);
                row.ExtractedName = split.Name;
                row.QuantityText = split.Quantity;
                if (string.IsNullOrWhiteSpace(split.Quantity))
                    row.Warnings.Add("No supported table header was found; this is an unstructured text line.");
            }
            else
            {
                string? Value(string key)
                {
                    var index = headers.FindIndex(h => h.Key == key);
                    if (index < 0) return null;
                    var quantityLeft = headers.FirstOrDefault(h => h.Key == "quantity").Left;
                    var left = key == "quantity" && quantityLeft > 0
                        ? quantityLeft
                        : index == 0 ? double.NegativeInfinity : (headers[index - 1].Left + headers[index].Left) / 2;
                    var right = key == "name" && quantityLeft > 0
                        ? quantityLeft
                        : index == headers.Count - 1 ? double.PositiveInfinity : (headers[index].Left + headers[index + 1].Left) / 2;
                    return string.Join(" ", ordered.Where(t => t.Left >= left && t.Left < right).Select(t => t.Text));
                }
                row.ExtractedName = Value("name") ?? "";
                row.QuantityText = Value("quantity");
                row.UnitText = Value("unit");
                row.BasicQtyText = Value("basic");
                row.PackagingQtyText = Value("packaging");
                row.Warnings.Add("Confirm the document row and column alignment.");
            }
            row.ExtractedName = RemoveLeadingRowIndex(row.ExtractedName);
            if (string.IsNullOrWhiteSpace(row.ExtractedName)) continue;
            // Isolated one-character OCR artefacts are not usable product rows.
            if (row.ExtractedName.Trim().Length < 2 && string.IsNullOrWhiteSpace(row.QuantityText))
                continue;
            if (row.ExtractedName.Length > options.Value.MaxTextLength)
                throw new DocumentExtractionException("TextLimit", "An extracted line exceeds the text limit.");
            var confidence = ordered.Where(t => t.Confidence.HasValue).Select(t => t.Confidence!.Value).ToList();
            row.OcrConfidence = confidence.Count == 0 ? null : confidence.Min();
            if (row.OcrConfidence < 0.85)
                row.Warnings.Add("OCR confidence is low; compare this row with the original document.");
            QuantityParser.Populate(row);
            rows.Add(row);
            if (rows.Count > options.Value.MaxRowsPerFile)
                throw new DocumentExtractionException("RowLimit", "The document exceeds the extracted-row limit.");
        }
        // Table OCR can place a right-column quantity slightly above its
        // matching product. Fold a quantity-only line into the following
        // product row before matching.
        for (var i = rows.Count - 2; i >= 0; i--)
        {
            if (!IsQuantityOnly(rows[i].ExtractedName) ||
                string.IsNullOrWhiteSpace(rows[i + 1].ExtractedName) ||
                !string.IsNullOrWhiteSpace(rows[i + 1].QuantityText))
                continue;
            rows[i + 1].QuantityText = string.Join(" ", rows[i].ExtractedName, rows[i].QuantityText).Trim();
            QuantityParser.Populate(rows[i + 1]);
            rows[i + 1].Warnings.Remove("No supported table header was found; this is an unstructured text line.");
            rows[i + 1].Warnings.AddRange(rows[i].Warnings.Where(w => !rows[i + 1].Warnings.Contains(w)));
            rows.RemoveAt(i);
        }
        rows.RemoveAll(row => row.OcrConfidence is < 0.85 && string.IsNullOrWhiteSpace(row.QuantityText));
        return rows;
    }

    private static double EstimateRowSlope(IReadOnlyList<DocumentToken> tokens)
    {
        var anchors = tokens.Where(t => DocumentColumnNames.Identify(t.Text) is "name" or "quantity").ToList();
        var name = anchors.FirstOrDefault(t => DocumentColumnNames.Identify(t.Text) == "name");
        var quantity = anchors.FirstOrDefault(t => DocumentColumnNames.Identify(t.Text) == "quantity");
        if (name is null || quantity is null || Math.Abs(quantity.Left - name.Left) < 100)
        {
            // When table OCR omits the header, estimate the camera perspective
            // from body cells in the left and right columns. Matching each left
            // token to the nearest right token at a similar vertical position
            // gives a stable row baseline without requiring a header.
            var centres = tokens.Select(t => new
            {
                Token = t,
                X = (t.Left + t.Right) / 2,
                Y = (t.Top + t.Bottom) / 2,
                Height = Math.Max(1, t.Bottom - t.Top)
            }).OrderBy(x => x.X).ToList();
            var slopes = new List<double>();
            var maxX = centres.Count == 0 ? 0 : centres.Max(x => x.X);
            var splitX = maxX * 0.55;
            foreach (var left in centres.Where(x => x.X < splitX))
            {
                var right = centres.Where(x => x.X > splitX)
                    .OrderBy(x => Math.Abs(x.Y - left.Y)).FirstOrDefault();
                if (right is null || Math.Abs(right.Y - left.Y) > Math.Max(left.Height, right.Height) * 3)
                    continue;
                slopes.Add((right.Y - left.Y) / (right.X - left.X));
            }
            if (slopes.Count == 0) return 0;
            slopes.Sort();
            return Math.Clamp(slopes[slopes.Count / 2], -0.5, 0.5);
        }
        var slope = ((quantity.Top + quantity.Bottom) / 2 - (name.Top + name.Bottom) / 2) /
            (((quantity.Left + quantity.Right) / 2) - ((name.Left + name.Right) / 2));
        return Math.Clamp(slope, -0.5, 0.5);
    }

    private static List<DocumentToken> Cells(List<DocumentToken> line)
    {
        var cells = new List<DocumentToken>();
        foreach (var token in line)
        {
            if (cells.Count > 0 && token.Left - cells[^1].Right < Math.Max(10, (token.Bottom - token.Top) * 1.2))
            {
                var previous = cells[^1];
                cells[^1] = previous with { Text = previous.Text + " " + token.Text, Right = token.Right };
            }
            else cells.Add(token);
        }
        return cells;
    }

    private static (string Name, string? Quantity) SplitUnstructuredLine(List<DocumentToken> ordered,
        IReadOnlySet<string>? knownUnits)
    {
        var fullText = string.Join(" ", ordered.Select(t => t.Text)).Trim();
        var hardcodedUnits = new[] { "strip", "strips", "box", "boxes", "tablet", "tablets", "tab", "tabs", "bottle", "bottles", "piece", "pieces", "pack", "packs", "unit", "units", "vial", "vials", "ampoule", "ampoules" };
        var units = hardcodedUnits.Concat(knownUnits ?? Enumerable.Empty<string>()).Where(u => !string.IsNullOrWhiteSpace(u))
            .Select(u => Regex.Escape(u.Trim())).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        var unitPattern = string.Join("|", units.OrderByDescending(u => u.Length));

        // A photographed row may be returned as one OCR token, so split the
        // text itself when a quantity followed by a known inventory unit starts.
        // Dosages such as 500mg/100ml are deliberately not in this pattern.
        var embedded = Regex.Match(fullText,
            $@"(?<name>.+?)[\s:]+(?=\d+(?:\.\d+)?\s*(?:{unitPattern})\b)(?<quantity>\d+(?:\.\d+)?\s*(?:{unitPattern})\b.*)$",
            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
        if (embedded.Success && embedded.Groups["name"].Value.Trim().Length > 0)
            return (embedded.Groups["name"].Value.Trim(), embedded.Groups["quantity"].Value.Trim());

        var quantityUnits = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "strip", "strips", "box", "boxes", "tablet", "tablets", "tab",
            "tabs", "bottle", "bottles", "piece", "pieces", "pack", "packs",
            "unit", "units", "vial", "vials", "ampoule", "ampoules"
        };
        if (knownUnits is not null)
            foreach (var unit in knownUnits) quantityUnits.Add(unit);
        for (var i = 1; i < ordered.Count; i++)
        {
            var text = ordered[i].Text.Trim();
            var match = Regex.Match(text, @"^[^A-Za-z0-9]*(\d+(?:\.\d+)?)\s*([A-Za-z]+)?",
                RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100));
            if (!match.Success) continue;
            var unit = match.Groups[2].Value;
            if (string.IsNullOrWhiteSpace(unit) && !Regex.IsMatch(text, @"^[^A-Za-z0-9]*\d+(?:\.\d+)?\s*$"))
                continue;
            if (!string.IsNullOrWhiteSpace(unit) && !quantityUnits.Contains(unit))
                continue;
            var name = string.Join(" ", ordered.Take(i).Select(t => t.Text)).Trim();
            var quantity = string.Join(" ", ordered.Skip(i).Select(t => t.Text)).Trim();
            if (name.Length > 0 && quantity.Length > 0)
                return (name, quantity);
        }
        return (string.Join(" ", ordered.Select(t => t.Text)).Trim(), null);
    }

    private static bool IsQuantityOnly(string value) => Regex.IsMatch(value.Trim(),
        @"^\d+(?:\.\d+)?\s*[A-Za-z]+(?:\s*[·.]?)?$", RegexOptions.CultureInvariant,
        TimeSpan.FromMilliseconds(100));

    private static bool IsDocumentFooter(List<DocumentToken> line)
    {
        var text = ProductNameNormalizer.Normalize(string.Join(" ", line.Select(t => t.Text)));
        return text.StartsWith("subtotal", StringComparison.Ordinal) ||
            text.StartsWith("tax discount", StringComparison.Ordinal) ||
            text.StartsWith("total", StringComparison.Ordinal) ||
            text.StartsWith("notes", StringComparison.Ordinal) ||
            text.StartsWith("unit prices", StringComparison.Ordinal);
    }

    private static string RemoveLeadingRowIndex(string value)
    {
        // Invoice tables often include a #/No column before the product. OCR
        // may merge one or more of those digits and punctuation into the name.
        return Regex.Replace(value,
            @"^[\s#№'"".,:;()\[\]-]*(?:\d+\s*[\s#№'"".,:;()\[\]-]*)+(?=[A-Za-z])",
            "", RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)).Trim();
    }
}
