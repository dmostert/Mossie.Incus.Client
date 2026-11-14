namespace Mossie.Incus.Client;

/// <summary>
/// Exception raised when the Incus server returns an error response.
/// Mirrors Go's <c>api.StatusError</c>.
/// </summary>
public class IncusStatusException : Exception
{
    public IncusStatusException(int statusCode, string message)
        : base(string.IsNullOrEmpty(message) ? GetStatusText(statusCode) : message)
    {
        StatusCode = statusCode;
    }

    public IncusStatusException(int statusCode, string message, Exception innerException)
        : base(string.IsNullOrEmpty(message) ? GetStatusText(statusCode) : message, innerException)
    {
        StatusCode = statusCode;
    }

    /// <summary>HTTP status code associated with the error.</summary>
    public int StatusCode { get; }

    /// <summary>Returns true when this error has the supplied HTTP status code.</summary>
    public bool Matches(int statusCode) => StatusCode == statusCode;

    private static string GetStatusText(int statusCode)
        => Enum.IsDefined(typeof(System.Net.HttpStatusCode), statusCode)
            ? ((System.Net.HttpStatusCode)statusCode).ToString()
            : string.Empty;
}

/// <summary>
/// Exception raised for protocol-level failures (unexpected payloads, missing extensions, ...).
/// </summary>
public class IncusException : Exception
{
    public IncusException(string message)
        : base(message)
    {
    }

    public IncusException(string message, Exception innerException)
        : base(message, innerException)
    {
    }
}
