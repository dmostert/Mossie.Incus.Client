using Mossie.Incus.Client.Api;

namespace Mossie.Incus.Client;

/// <summary>
/// Image-server surface shared by <see cref="IncusClient"/>, <see cref="SimpleStreamsClient"/>
/// and <see cref="OciClient"/>. Mirrors the Go <c>incus.ImageServer</c> image handling methods.
/// </summary>
public interface IImageServer
{
    /// <summary>Returns basic connection information.</summary>
    IncusConnectionInfo GetConnectionInfo();

    /// <summary>Returns a list of available images.</summary>
    Task<List<Image>> GetImagesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a list of available images across all projects.</summary>
    Task<List<Image>> GetImagesAllProjectsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns a filtered list of available images across all projects.</summary>
    Task<List<Image>> GetImagesAllProjectsWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a filtered list of available images.</summary>
    Task<List<Image>> GetImagesWithFilterAsync(
        IEnumerable<string> filters,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the fingerprints of all available images.</summary>
    Task<List<string>> GetImageFingerprintsAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns an image.</summary>
    Task<(Image Image, string ETag)> GetImageAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Downloads an image.</summary>
    Task<ImageFileResponse> GetImageFileAsync(
        string fingerprint,
        ImageFileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns a temporary download secret for an image.</summary>
    Task<string> GetImageSecretAsync(string fingerprint, CancellationToken cancellationToken = default);

    /// <summary>Returns a private image using a download secret.</summary>
    Task<(Image Image, string ETag)> GetPrivateImageAsync(
        string fingerprint,
        string secret,
        CancellationToken cancellationToken = default);

    /// <summary>Downloads a private image using a download secret.</summary>
    Task<ImageFileResponse> GetPrivateImageFileAsync(
        string fingerprint,
        string secret,
        ImageFileRequest request,
        CancellationToken cancellationToken = default);

    /// <summary>Returns all image aliases.</summary>
    Task<List<ImageAliasesEntry>> GetImageAliasesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns the names of all image aliases.</summary>
    Task<List<string>> GetImageAliasNamesAsync(CancellationToken cancellationToken = default);

    /// <summary>Returns an image alias.</summary>
    Task<(ImageAliasesEntry Alias, string ETag)> GetImageAliasAsync(
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Returns an image alias for a specific image type.</summary>
    Task<(ImageAliasesEntry Alias, string ETag)> GetImageAliasTypeAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Returns the alias targets grouped by architecture.</summary>
    Task<Dictionary<string, ImageAliasesEntry>> GetImageAliasArchitecturesAsync(
        string imageType,
        string name,
        CancellationToken cancellationToken = default);

    /// <summary>Exports (copies) an image to a remote server.</summary>
    Task<Operation> ExportImageAsync(
        string fingerprint,
        ImageExportPost image,
        CancellationToken cancellationToken = default);
}
