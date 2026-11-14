using System.Globalization;
using System.Net;
using System.Text.Json;
using Mossie.Incus.Client.Api;
using Mossie.Incus.Client.Internal;
using Mossie.Incus.Client.Serialization;

namespace Mossie.Incus.Client;

public sealed partial class IncusClient
{
    /// <summary>Retrieves a file or directory listing from an instance or storage volume endpoint.</summary>
    internal async Task<(Stream? Content, InstanceFileResponse Response)> GetFileAsync(
        string path,
        string filePath,
        CancellationToken cancellationToken)
    {
        string url = IncusQuery.SetParameter(BuildQueryUrl(path), "path", filePath);

        using HttpRequestMessage request = new(HttpMethod.Get, url);
        await AddClientHeadersAsync(request, cancellationToken).ConfigureAwait(false);

        HttpResponseMessage response = await _http
            .SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cancellationToken)
            .ConfigureAwait(false);

        if (response.StatusCode != HttpStatusCode.OK)
        {
            using (response)
                await ParseResponseAsync(response, url, cancellationToken).ConfigureAwait(false);
        }

        InstanceFileResponse fileResponse = new()
        {
            UID = ParseInt64Header(response, "X-Incus-uid"),
            GID = ParseInt64Header(response, "X-Incus-gid"),
            Mode = ParseModeHeader(response, "X-Incus-mode"),
            Type = response.Headers.TryGetValues("X-Incus-type", out IEnumerable<string>? types)
                ? types.FirstOrDefault() ?? ""
                : "",
        };

        if (fileResponse.Type == "directory")
        {
            using (response)
            {
                string body = await response.Content.ReadAsStringAsync(cancellationToken).ConfigureAwait(false);
                Api.Response parsed = IncusJson.Deserialize<Api.Response>(body)
                    ?? throw new IncusException("The server returned an empty response.");

                if (parsed.Metadata.ValueKind is JsonValueKind.Undefined or JsonValueKind.Null)
                    fileResponse.Entries = new List<string>();
                else
                    fileResponse.Entries = parsed.Metadata.Deserialize<List<string>>(IncusJson.Options) ?? new List<string>();
            }

            return (null, fileResponse);
        }

        Stream content = await response.Content.ReadAsStreamAsync(cancellationToken).ConfigureAwait(false);
        return (new ResponseStream(content, response), fileResponse);
    }

    /// <summary>Uploads a file to an instance or storage volume endpoint.</summary>
    internal async Task CreateFileAsync(string path, string filePath, InstanceFileArgs args, CancellationToken cancellationToken)
    {
        Dictionary<string, string[]> headers = new();

        if (args.UID > -1)
            headers["X-Incus-uid"] = new[] { args.UID.ToString(CultureInfo.InvariantCulture) };

        if (args.GID > -1)
            headers["X-Incus-gid"] = new[] { args.GID.ToString(CultureInfo.InvariantCulture) };

        if (args.Mode > -1)
            headers["X-Incus-mode"] = new[] { Convert.ToString(args.Mode, 8).PadLeft(4, '0') };

        if (!string.IsNullOrEmpty(args.Type))
            headers["X-Incus-type"] = new[] { args.Type };

        if (!string.IsNullOrEmpty(args.WriteMode))
            headers["X-Incus-write"] = new[] { args.WriteMode };

        string url = IncusQuery.SetParameter(BuildQueryUrl(path), "path", filePath);
        await RawQueryWithHeadersAsync(HttpMethod.Post, url, args.Content, null, headers, cancellationToken)
            .ConfigureAwait(false);
    }

    private static long ParseInt64Header(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            return 0;

        string? value = values.FirstOrDefault();
        return long.TryParse(value, NumberStyles.Integer, CultureInfo.InvariantCulture, out long parsed) ? parsed : 0;
    }

    private static int ParseModeHeader(HttpResponseMessage response, string name)
    {
        if (!response.Headers.TryGetValues(name, out IEnumerable<string>? values))
            return 0;

        string? value = values.FirstOrDefault();
        if (string.IsNullOrEmpty(value))
            return 0;

        try
        {
            return Convert.ToInt32(value, 8);
        }
        catch (FormatException)
        {
            return 0;
        }
        catch (ArgumentException)
        {
            return 0;
        }
    }
}
