using SyntaxCircus.Common;
using TechStrap.Contracts.Kb;
using TechStrap.Contracts.Paging;

namespace TechStrap.Portal.Clients;

/// <summary>The public knowledge base (P09-T02). 09b needs the search only, for the contact page's suggestions; 09c extends this interface with the categories and the articles.</summary>
public interface IPublicKbClient
{
    /// <summary>
    /// The published articles of the product (and the shared ones) that match the text, best first, <paramref name="pageSize"/> at most. Every text field of a hit is plain text: a consumer encodes it. A key that
    /// is not a slug is the uniform not-found error and no call is made. The API cuts a longer text at <see cref="KbLimits.MaxSearchTextChars"/> and answers a blank text or an unknown product with an empty page.
    /// </summary>
    Task<Result<PagedResponse<PublicKbSearchResultDto>>> SearchAsync(string productKey, string text, int pageSize, CancellationToken cancellationToken);
}
