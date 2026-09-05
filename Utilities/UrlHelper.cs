using System.Web;

namespace prohpharmacy_trekking_app.Utilities
{
    public static class UrlHelper
    {
        public static string GeneratePageUrl(string baseUrl, int? page, int? pageSize, string? searchKey)
        {
            var queryString = new Dictionary<string, string>();
            if (page.HasValue)
                queryString["page"] = page.Value.ToString();
            if (pageSize.HasValue)
                queryString["pageSize"] = pageSize.Value.ToString();
            if (!string.IsNullOrEmpty(searchKey))
                queryString["search"] = searchKey;
            return baseUrl + ToQueryString(queryString);
        }

        public static string ToQueryString(Dictionary<string, string> dict)
        {
            var queryParts = dict.Select(kv => $"{kv.Key}={Uri.EscapeDataString(kv.Value)}");
            return "?" + string.Join("&", queryParts);
        }

        public static string UpdateQueryStringParameters(string fullUrl, Dictionary<string, string> queryParams)
        {
            var uriBuilder = new UriBuilder(fullUrl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);

            foreach (var kvp in queryParams)
                query[kvp.Key] = kvp.Value;

            uriBuilder.Query = query.ToString();
            return uriBuilder.Uri.ToString();
        }

        public static string UpdateQueryStringParameter(string fullUrl, string paramName, string paramValue)
        {
            var uriBuilder = new UriBuilder(fullUrl);
            var query = HttpUtility.ParseQueryString(uriBuilder.Query);
            query[paramName] = paramValue;
            uriBuilder.Query = query.ToString();
            return uriBuilder.Uri.ToString();
        }
    }
}
