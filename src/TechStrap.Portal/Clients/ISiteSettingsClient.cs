using SyntaxCircus.Common;
using TechStrap.Contracts.Settings;

namespace TechStrap.Portal.Clients;

/// <summary>The deployment-wide settings the Portal needs, anonymously (D-053): the default theme pack.</summary>
public interface ISiteSettingsClient
{
    /// <summary>The default pack key (<c>GET api/public/site</c>). Any failure is an error result, never an exception the page sees.</summary>
    Task<Result<PublicSiteDto>> GetAsync(CancellationToken cancellationToken);
}
