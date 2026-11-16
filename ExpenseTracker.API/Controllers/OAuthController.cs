using System.Net.Http.Headers;
using System.Security.Claims;
using System.Text.Json;
using ExpenseTracker.Infrastructure.OAuth;
using ExpenseTracker.Infrastructure.Security;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ExpenseTracker.API.Controllers
{
    /// <summary>
    /// The protocol half of OAuth for MCP: discovery, registration, token and revocation.
    /// Every endpoint here is called by the client's server, not by a signed-in browser,
    /// so all of them are anonymous and answer in OAuth's own error format rather than
    /// ProblemDetails — hence no [ApiController].
    /// <para>
    /// The authorization endpoint itself is the SPA page at /oauth/authorize; it uses
    /// <see cref="OAuthConsentController"/> to check the request and record the decision.
    /// </para>
    /// </summary>
    [AllowAnonymous]
    public sealed class OAuthController : ControllerBase
    {
        private readonly OAuthService _oauth;
        private readonly IConfiguration _configuration;

        public OAuthController(OAuthService oauth, IConfiguration configuration)
        {
            _oauth = oauth;
            _configuration = configuration;
        }

        private string Origin => OAuthDefaults.PublicOrigin(Request, _configuration);
        private string Resource => OAuthDefaults.Resource(Request, _configuration);

        private static readonly string[] Scopes = { OAuthDefaults.ScopeRead, OAuthDefaults.ScopeWrite };
        private static readonly string[] ClientAuthMethods = { "none", "client_secret_post", "client_secret_basic" };

        /// <summary>RFC 9728. Served at the bare path and the path-suffixed one clients try first.</summary>
        [HttpGet(OAuthDefaults.ProtectedResourceMetadataPath)]
        [HttpGet(OAuthDefaults.ProtectedResourceMetadataPath + McpAuthDefaults.Path)]
        public IActionResult ProtectedResourceMetadata() => Ok(new Dictionary<string, object>
        {
            ["resource"] = Resource,
            ["authorization_servers"] = new[] { Origin },
            ["scopes_supported"] = Scopes,
            ["bearer_methods_supported"] = new[] { "header" },
            ["resource_name"] = "ExpenseTracker"
        });

        /// <summary>RFC 8414.</summary>
        [HttpGet(OAuthDefaults.AuthorizationServerMetadataPath)]
        public IActionResult AuthorizationServerMetadata() => Ok(new Dictionary<string, object>
        {
            ["issuer"] = Origin,
            ["authorization_endpoint"] = Origin + OAuthDefaults.AuthorizePagePath,
            ["token_endpoint"] = Origin + OAuthDefaults.TokenPath,
            ["registration_endpoint"] = Origin + OAuthDefaults.RegisterPath,
            ["revocation_endpoint"] = Origin + OAuthDefaults.RevokePath,
            ["response_types_supported"] = new[] { "code" },
            ["response_modes_supported"] = new[] { "query" },
            ["grant_types_supported"] = new[] { "authorization_code", "refresh_token" },
            ["code_challenge_methods_supported"] = new[] { "S256" },
            ["token_endpoint_auth_methods_supported"] = ClientAuthMethods,
            ["revocation_endpoint_auth_methods_supported"] = ClientAuthMethods,
            ["scopes_supported"] = Scopes,
            ["client_id_metadata_document_supported"] = true,
            ["authorization_response_iss_parameter_supported"] = true
        });

        /// <summary>RFC 7591 dynamic client registration. Open, as the MCP spec expects, but rate limited.</summary>
        [HttpPost(OAuthDefaults.RegisterPath)]
        [EnableRateLimiting("oauth-register")]
        public async Task<IActionResult> Register([FromBody] JsonElement body, CancellationToken cancellationToken)
        {
            var (response, error) = await _oauth.RegisterAsync(body, cancellationToken);
            return error is not null ? Error(error) : StatusCode(StatusCodes.Status201Created, response);
        }

        [HttpPost(OAuthDefaults.TokenPath)]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Token([FromForm] IFormCollection form, CancellationToken cancellationToken)
        {
            NoStore();

            var (client, clientError) = await AuthenticateClientAsync(form, cancellationToken);
            if (clientError is not null) return Error(clientError);

            var (response, error) = form["grant_type"].ToString() switch
            {
                "authorization_code" => await _oauth.ExchangeCodeAsync(client!, form["code"], form["redirect_uri"],
                    form["code_verifier"], form["resource"], Resource, cancellationToken),
                "refresh_token" => await _oauth.RefreshAsync(client!, form["refresh_token"], cancellationToken),
                _ => (null, OAuthError.UnsupportedGrantType("Supported: authorization_code, refresh_token."))
            };

            return error is not null ? Error(error) : Ok(response);
        }

        /// <summary>RFC 7009. 200 whether or not the token existed.</summary>
        [HttpPost(OAuthDefaults.RevokePath)]
        [Consumes("application/x-www-form-urlencoded")]
        public async Task<IActionResult> Revoke([FromForm] IFormCollection form, CancellationToken cancellationToken)
        {
            var (client, clientError) = await AuthenticateClientAsync(form, cancellationToken);
            if (clientError is not null) return Error(clientError);

            await _oauth.RevokeAsync(client!, form["token"], cancellationToken);
            return Ok();
        }

        private Task<(ResolvedClient?, OAuthError?)> AuthenticateClientAsync(IFormCollection form, CancellationToken cancellationToken)
        {
            string? basic = null;
            if (AuthenticationHeaderValue.TryParse(Request.Headers.Authorization, out var header) &&
                string.Equals(header.Scheme, "Basic", StringComparison.OrdinalIgnoreCase))
                basic = header.Parameter;

            var id = form["client_id"].ToString();
            var secret = form["client_secret"].ToString();
            return _oauth.AuthenticateClientAsync(basic, string.IsNullOrEmpty(id) ? null : id,
                string.IsNullOrEmpty(secret) ? null : secret, cancellationToken);
        }

        private void NoStore()
        {
            Response.Headers.CacheControl = "no-store";
            Response.Headers.Pragma = "no-cache";
        }

        private ObjectResult Error(OAuthError error)
        {
            if (error.Status == StatusCodes.Status401Unauthorized)
                Response.Headers.WWWAuthenticate = "Basic realm=\"ExpenseTracker\"";
            return StatusCode(error.Status, new { error = error.Error, error_description = error.Description });
        }
    }

    /// <summary>
    /// The consent half: called by the SPA's /oauth/authorize page with the signed-in
    /// user's JWT. The page passes through the client's query parameters untouched.
    /// </summary>
    [Authorize]
    [ApiController]
    [Route("api/oauth/authorize")]
    public sealed class OAuthConsentController : ControllerBase
    {
        private readonly OAuthService _oauth;
        private readonly IConfiguration _configuration;

        public OAuthConsentController(OAuthService oauth, IConfiguration configuration)
        {
            _oauth = oauth;
            _configuration = configuration;
        }

        private string Origin => OAuthDefaults.PublicOrigin(Request, _configuration);
        private string Resource => OAuthDefaults.Resource(Request, _configuration);

        /// <summary>400 with a message to show the user, or what the consent screen needs.</summary>
        [HttpGet]
        public async Task<IActionResult> Describe([FromQuery] AuthorizeParameters parameters, CancellationToken cancellationToken)
        {
            var (info, fatal, _) = await _oauth.ValidateAsync(parameters, Origin, Resource, cancellationToken);
            return fatal is not null ? BadRequest(new { message = fatal }) : Ok(info);
        }

        /// <summary>Returns the URL to send the browser to: back to the client, with a code or an error.</summary>
        [HttpPost]
        public async Task<IActionResult> Decide([FromBody] AuthorizeDecision decision, CancellationToken cancellationToken)
        {
            var userId = User.FindFirstValue(ClaimTypes.NameIdentifier)!;
            var (redirect, fatal) = await _oauth.DecideAsync(userId, decision, Origin, Resource, cancellationToken);
            return fatal is not null ? BadRequest(new { message = fatal }) : Ok(new { redirect_url = redirect });
        }
    }
}
