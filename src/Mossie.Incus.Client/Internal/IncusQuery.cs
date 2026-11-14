using System.Text;

namespace Mossie.Incus.Client.Internal;

/// <summary>Query-string helpers that mirror Go's <c>net/url.Values.Encode</c> ordering.</summary>
internal static class IncusQuery
{
    /// <summary>Parses a query string into an ordered dictionary.</summary>
    internal static Dictionary<string, string> Parse(string query)
    {
        Dictionary<string, string> values = new(StringComparer.Ordinal);

        if (string.IsNullOrEmpty(query))
            return values;

        foreach (string pair in query.Split('&', StringSplitOptions.RemoveEmptyEntries))
        {
            int separator = pair.IndexOf('=');
            string key;
            string value;

            if (separator < 0)
            {
                key = pair;
                value = "";
            }
            else
            {
                key = pair[..separator];
                value = pair[(separator + 1)..];
            }

            values[Unescape(key)] = Unescape(value);
        }

        return values;
    }

    /// <summary>Encodes values sorted by key, like Go's <c>url.Values.Encode</c>.</summary>
    internal static string Encode(IEnumerable<KeyValuePair<string, string>> values)
    {
        StringBuilder builder = new();

        foreach (KeyValuePair<string, string> item in values.OrderBy(x => x.Key, StringComparer.Ordinal))
        {
            if (builder.Length > 0)
                builder.Append('&');

            builder.Append(Escape(item.Key));
            builder.Append('=');
            builder.Append(Escape(item.Value));
        }

        return builder.ToString();
    }

    /// <summary>Adds <c>target</c> and <c>project</c> attributes to a relative path.</summary>
    internal static string AddClientAttributes(string path, string? project, string? target)
    {
        int question = path.IndexOf('?');
        string pathPart = question < 0 ? path : path[..question];
        string queryPart = question < 0 ? "" : path[(question + 1)..];

        if (string.IsNullOrEmpty(project) && string.IsNullOrEmpty(target))
            return path;

        Dictionary<string, string> values = Parse(queryPart);

        if (!string.IsNullOrEmpty(target) && !values.ContainsKey("target"))
            values["target"] = target;

        if (!string.IsNullOrEmpty(project) && !values.ContainsKey("project") && !values.ContainsKey("all-projects"))
            values["project"] = project;

        string encoded = Encode(values);
        return encoded.Length == 0 ? pathPart : $"{pathPart}?{encoded}";
    }

    /// <summary>Sets a single query parameter on a URI, escaping the value like Go's <c>QueryEscape</c>.</summary>
    internal static string SetParameter(string uri, string parameter, string value)
    {
        int question = uri.IndexOf('?');
        string pathPart = question < 0 ? uri : uri[..question];
        string queryPart = question < 0 ? "" : uri[(question + 1)..];

        Dictionary<string, string> values = Parse(queryPart);
        values[parameter] = value;

        string encoded = Encode(values);
        return encoded.Length == 0 ? pathPart : $"{pathPart}?{encoded}";
    }

    internal static string Escape(string value) => Uri.EscapeDataString(value).Replace("%20", "+", StringComparison.Ordinal);

    internal static string Unescape(string value) => Uri.UnescapeDataString(value.Replace("+", " ", StringComparison.Ordinal));

    /// <summary>Translates client-side <c>key=value</c> filters to the server's filter expression.</summary>
    internal static string ParseFilters(IEnumerable<string>? filters)
    {
        if (filters is null)
            return "";

        List<string> result = new();

        foreach (string filter in filters)
        {
            int separator = filter.IndexOf('=');
            if (separator < 0)
                continue;

            result.Add($"{filter[..separator]} eq {filter[(separator + 1)..]}");
        }

        return string.Join(" and ", result);
    }

    /// <summary>Extracts resource names from a list of URLs sharing the supplied path prefix.</summary>
    internal static List<string> UrlsToResourceNames(string matchPathPrefix, IEnumerable<string> urls)
    {
        List<string> names = new();

        foreach (string raw in urls)
        {
            if (!Uri.TryCreate(raw, UriKind.Absolute, out Uri? uri))
                throw new IncusException($"Failed parsing URL \"{raw}\".");

            string marker = matchPathPrefix.TrimEnd('/') + "/";
            int index = uri.AbsolutePath.IndexOf(marker, StringComparison.Ordinal);
            if (index < 0)
                throw new IncusException($"Unexpected URL path \"{uri.AbsolutePath}\".");

            names.Add(uri.AbsolutePath[(index + marker.Length)..]);
        }

        return names;
    }
}
