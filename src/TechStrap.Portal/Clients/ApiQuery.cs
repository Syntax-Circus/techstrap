namespace TechStrap.Portal.Clients;

/// <summary>A path with a query string for an API call. Every name and value is escaped, so a visitor's text can never add a parameter, end the query or start a fragment; a pair with no value is left out.</summary>
internal static class ApiQuery
{
    public static string Build(string path, params (string Name, string? Value)[] pairs)
    {
        var query = string.Join('&', pairs.Where(p => p.Value is not null).Select(p => $"{Uri.EscapeDataString(p.Name)}={Uri.EscapeDataString(p.Value!)}"));
        return query.Length == 0 ? path : $"{path}?{query}";
    }
}
