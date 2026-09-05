using Microsoft.EntityFrameworkCore;

namespace prohpharmacy_trekking_app.Utilities
{
    /// <summary>
    /// Self-contained paginator — no separate class library required.
    /// Wire up via Paginator.SetHttpContextAccessor() in Program.cs.
    /// </summary>
    public static class Paginator
    {
        private static IHttpContextAccessor _httpContextAccessor = null!;
        private static string? _publicBaseUrl;

        /// <summary>
        /// Must be called once in Program.cs after the app is built.
        /// </summary>
        public static void SetHttpContextAccessor(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor ?? throw new ArgumentNullException(nameof(httpContextAccessor));
        }

        /// <summary>
        /// Set a fixed public base URL (e.g. "https://api.example.com/v1").
        /// When set, all pagination links use this value instead of inspecting request headers.
        /// </summary>
        public static void SetPublicBaseUrl(string? publicBaseUrl)
        {
            _publicBaseUrl = publicBaseUrl?.TrimEnd('/');
        }

        public class PaginatedData<T>
        {
            public int TotalCount { get; set; }
            public int TotalPages { get; set; }
            public int CurrentPage { get; set; }
            public int PageSize { get; set; }
            public string? NextPageUrl { get; set; }
            public string? PreviousPageUrl { get; set; }
            public string[] Links { get; set; } = [];
            public string Path { get; set; } = string.Empty;
            public List<T> Data { get; set; } = [];
        }

        private static string GetPath()
        {
            var ctx = _httpContextAccessor?.HttpContext;
            if (ctx == null) return string.Empty;

            var req = ctx.Request;
            var path = req.Path.HasValue ? req.Path.Value : string.Empty;
            var query = req.QueryString.HasValue ? req.QueryString.Value : string.Empty;

            if (!string.IsNullOrWhiteSpace(_publicBaseUrl))
                return $"{_publicBaseUrl}{path}{query}";

            var scheme = req.Scheme;
            var host = req.Host.HasValue ? req.Host.Value : string.Empty;
            var pathBase = req.PathBase.HasValue ? req.PathBase.Value : string.Empty;

            return $"{scheme}://{host}{pathBase}{path}{query}";
        }

        private static string GetPageUrl(int pageNumber)
        {
            return UrlHelper.UpdateQueryStringParameters(GetPath(), new Dictionary<string, string>
            {
                { "pageNumber", pageNumber.ToString() }
            });
        }

        private static string[] GetPaginationLinks(int totalPages)
        {
            var links = new string[totalPages];
            for (int i = 1; i <= totalPages; i++)
                links[i - 1] = GetPageUrl(i);
            return links;
        }

        public static async Task<PaginatedData<T>> PaginateAsync<T>(IQueryable<T> source, int pageNumber, int pageSize)
        {
            var totalCount = await source.CountAsync();
            var data = await source.Skip((pageNumber - 1) * pageSize).Take(pageSize).ToListAsync();
            var totalPages = (int)Math.Ceiling(totalCount / (double)pageSize);

            return new PaginatedData<T>
            {
                TotalCount = totalCount,
                TotalPages = totalPages,
                CurrentPage = pageNumber,
                PageSize = pageSize,
                NextPageUrl = pageNumber < totalPages ? GetPageUrl(pageNumber + 1) : null,
                PreviousPageUrl = pageNumber > 1 ? GetPageUrl(pageNumber - 1) : null,
                Path = GetPath(),
                Links = GetPaginationLinks(totalPages),
                Data = data
            };
        }
    }
}
