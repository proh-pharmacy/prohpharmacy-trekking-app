using System.Security.Claims;

namespace prohpharmacy_trekking_app.Providers
{
    /// <summary>
    /// Provides access to the currently authenticated user's claims from the HTTP context.
    /// Register as a Scoped service in Program.cs.
    /// 
    /// Extend this class to resolve typed user/entity records from the database 
    /// (e.g., GetAuthUser(), GetAuthAdmin()) once your entities are in place.
    /// </summary>
    public class AuthProvider
    {
        private readonly IHttpContextAccessor _httpContextAccessor;

        public AuthProvider(IHttpContextAccessor httpContextAccessor)
        {
            _httpContextAccessor = httpContextAccessor;
        }

        private ClaimsPrincipal? CurrentUser => _httpContextAccessor.HttpContext?.User;

        /// <summary>Returns the raw ClaimsPrincipal for the authenticated user, or null.</summary>
        public ClaimsPrincipal? GetClaimsPrincipal() => CurrentUser;

        /// <summary>Returns the authenticated user's ID claim, or null if unauthenticated.</summary>
        public string? GetUserId()
            => CurrentUser?.FindFirstValue(ClaimTypes.NameIdentifier)
               ?? CurrentUser?.FindFirstValue("sub");

        /// <summary>Returns the authenticated user's email claim, or null.</summary>
        public string? GetEmail()
            => CurrentUser?.FindFirstValue(ClaimTypes.Email);

        /// <summary>Returns the authenticated user's role claim, or null.</summary>
        public string? GetRole()
            => CurrentUser?.FindFirstValue(ClaimTypes.Role);

        /// <summary>Returns true if the user is authenticated.</summary>
        public bool IsAuthenticated()
            => CurrentUser?.Identity?.IsAuthenticated ?? false;
    }
}
