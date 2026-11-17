using System.Text.RegularExpressions;

namespace prohpharmacy_trekking_app.Services.DocumentExtraction;

public static class DocumentColumnValueReader
{
    public static string? Read(string key, IReadOnlyList<(string Key, double Left, double Right)> headers,
        IReadOnlyList<DocumentToken> line, double textHeight)
    {
        var index = Enumerable.Range(0, headers.Count).FirstOrDefault(i => headers[i].Key == key, -1);
        if (index < 0) return null;
        var left = index == 0 ? double.NegativeInfinity : Boundary(index);
        var right = index == headers.Count - 1 ? double.PositiveInfinity : Boundary(index + 1);
        return string.Join(" ", line.Where(t => t.Left >= left && t.Left < right).Select(t => t.Text));

        double Boundary(int rightIndex)
        {
            var previous = headers[rightIndex - 1];
            var next = headers[rightIndex];
            if (previous.Key != "name") return (previous.Right + next.Left) / 2;

            // A short Description heading can sit above a much wider product
            // column. Use the next column's start, not the header text midpoint.
            var boundary = next.Left - textHeight;
            if (next.Key is "price" or "quantity" or "basic" or "packaging")
            {
                var end = rightIndex == headers.Count - 1 ? double.PositiveInfinity
                    : (next.Right + headers[rightIndex + 1].Left) / 2;
                var number = line.Where(t => t.Right >= next.Left && t.Left < end &&
                        Regex.IsMatch(t.Text.Trim(), @"^(?:GHS\s*)?\d[\d,.]*$",
                            RegexOptions.IgnoreCase | RegexOptions.CultureInvariant, TimeSpan.FromMilliseconds(100)))
                    .OrderBy(t => Math.Abs((t.Left + t.Right - next.Left - next.Right) / 2))
                    .FirstOrDefault();
                if (number is not null) boundary = Math.Min(boundary, number.Left);
            }
            return Math.Max(previous.Right, boundary);
        }
    }
}
