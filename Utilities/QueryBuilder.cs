using Microsoft.EntityFrameworkCore;

namespace prohpharmacy_trekking_app.Utilities
{
    /// <summary>
    /// Fluent query builder supporting search, sort, and pagination.
    /// Usage:
    ///   var result = await new QueryBuilder&lt;MyEntity&gt;(query)
    ///       .WithSearch(searchKey, "Name", "Description")
    ///       .WithSort(sortBy)
    ///       .Paginate(pageNumber, pageSize)
    ///       .BuildAsync();
    /// </summary>
    public class QueryBuilder<T>
    {
        private IQueryable<T> _query;
        private string? _searchKey;
        private readonly List<string> _searchColumns = new();
        private string? _sortBy;
        private string _sortDirection = "desc";
        private int? _pageSize = null;
        private int? _pageNumber = 1;

        public QueryBuilder(IQueryable<T> query)
        {
            _query = query ?? throw new ArgumentNullException(nameof(query));
        }

        /// <summary>
        /// Adds case-insensitive contains search across the specified columns.
        /// </summary>
        public QueryBuilder<T> WithSearch(string? searchKey, params string[] searchColumns)
        {
            if (string.IsNullOrWhiteSpace(searchKey))
                return this;

            _searchKey = searchKey;

            if (searchColumns != null)
                _searchColumns.AddRange(searchColumns);

            return this;
        }

        /// <summary>
        /// Adds dynamic sorting. Format: "fieldName_asc" or "fieldName_desc".
        /// </summary>
        public QueryBuilder<T> WithSort(string? sortBy)
        {
            if (string.IsNullOrWhiteSpace(sortBy))
                return this;

            var parts = sortBy.Split('_');
            if (parts.Length != 2)
                throw new ArgumentException("SortBy must be in the format 'fieldName_asc' or 'fieldName_desc'.");

            var fieldName = parts[0];
            var direction = parts[1].ToLower() == "desc" ? "desc" : "asc";

            var property = typeof(T).GetProperties()
                .FirstOrDefault(p => p.Name.Equals(fieldName, StringComparison.OrdinalIgnoreCase));

            if (property == null)
                throw new ArgumentException($"Property '{fieldName}' not found on '{typeof(T).Name}'.");

            _sortBy = property.Name;
            _sortDirection = direction;
            return this;
        }

        /// <summary>
        /// Enables pagination. Pass null pageSize to return all results (no pagination).
        /// </summary>
        public QueryBuilder<T> Paginate(int? pageNumber, int? pageSize = null)
        {
            if (pageSize is not null)
            {
                _pageSize = pageSize;
                _pageNumber = pageNumber ?? 1;
            }
            return this;
        }

        public async Task<object> BuildAsync(Func<T, object>? selector = null)
        {
            var result = _query;

            // Apply search
            if (!string.IsNullOrWhiteSpace(_searchKey) && _searchColumns.Any())
            {
                var searchKeyLower = _searchKey.ToLower();
                foreach (var column in _searchColumns)
                {
                    result = result.Where(x =>
                        EF.Property<string>(x!, column).ToLower().Contains(searchKeyLower));
                }
            }

            // Apply sort
            if (!string.IsNullOrWhiteSpace(_sortBy))
            {
                result = _sortDirection.ToLower() == "asc"
                    ? result.OrderBy(x => EF.Property<object>(x!, _sortBy))
                    : result.OrderByDescending(x => EF.Property<object>(x!, _sortBy));
            }

            // Apply pagination
            if (_pageSize != null)
            {
                var paginatedData = await Paginator.PaginateAsync(result, (int)_pageNumber!, (int)_pageSize);

                if (selector != null)
                {
                    var selectedData = paginatedData.Data.Select(selector).ToList();
                    return new Paginator.PaginatedData<object>
                    {
                        TotalCount = paginatedData.TotalCount,
                        TotalPages = paginatedData.TotalPages,
                        CurrentPage = paginatedData.CurrentPage,
                        PageSize = paginatedData.PageSize,
                        NextPageUrl = paginatedData.NextPageUrl,
                        PreviousPageUrl = paginatedData.PreviousPageUrl,
                        Path = paginatedData.Path,
                        Links = paginatedData.Links,
                        Data = selectedData
                    };
                }

                return paginatedData;
            }

            return selector != null
                ? result.Select(selector).ToList()
                : await result.ToListAsync();
        }
    }
}
