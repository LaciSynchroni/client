using LaciSynchroni.Common.Dto.User;
using LaciSynchroni.Services.ServerConfiguration;
using Microsoft.AspNetCore.SignalR.Client;
using Microsoft.Extensions.Logging;
using SixLabors.ImageSharp;
using SixLabors.ImageSharp.Formats;
using SixLabors.ImageSharp.Processing;
using System.Net.Http.Headers;

namespace LaciSynchroni.WebAPI.SignalR.SyncHubOverrides;

/// <summary>
/// Lightless's profiles: its own profile DTO (<see cref="UserProfileDtoLL"/>), with the picture as a URL that gets
/// downloaded and shrunk, and a description with FFXIV markup (see <see cref="ProfileMarkupLL"/>).
/// </summary>
internal partial class SyncHubClientLL
{
    // Lightless profile pictures are downloaded and shrunk to Laci's profile picture size
    private const int MaxPictureBytes = 5 * 1024 * 1024;
    private const int MaxPictureDimension = 256;
    private const long MaxDecodedPixels = 4096L * 4096L;
    private static readonly TimeSpan PictureDownloadTimeout = TimeSpan.FromSeconds(15);

    // Laci's shared HttpClient follows redirects, which could lead a picture request to any address.
    // Per connection rather than static, so it's disposed with the connection and doesn't outlive a plugin unload.
    private readonly HttpClient _pictureHttpClient = new(new SocketsHttpHandler
    {
        AllowAutoRedirect = false,
        UseCookies = false,
        PooledConnectionLifetime = TimeSpan.FromMinutes(5),
    });

    private readonly ServerConfigurationManager _llServerConfigurationManager;
    private readonly MultiConnectTokenService _llTokenService;
    private readonly HttpClient _llHttpClient;

    public override async Task<UserProfileDto> UserGetProfile(UserDto dto)
    {
        if (!IsConnected) return await base.UserGetProfile(dto).ConfigureAwait(false);

        var profile = await _connection!.InvokeAsync<UserProfileDtoLL>(nameof(UserGetProfile), dto).ConfigureAwait(false);
        var picture = !string.IsNullOrEmpty(profile.ProfilePictureBase64)
            ? profile.ProfilePictureBase64
            : await DownloadProfilePictureAsync(profile.ProfilePictureUrl).ConfigureAwait(false);

        // Own profile: the raw markup, so editing in Laci keeps the colours. The server only overwrites fields that are
        // sent, so saving the description from Laci leaves tags, colours and layout set in Lightless untouched.
        // Other users: markup removed, since Laci shows descriptions as plain text.
        var isOwnProfile = string.Equals((profile.User ?? dto.User).UID, UID, StringComparison.Ordinal);
        var description = isOwnProfile ? profile.Description : ProfileMarkupLL.StripMarkup(profile.Description);

        return new UserProfileDto(profile.User ?? dto.User, profile.IsModerationDisabled || profile.Disabled, profile.IsNSFW,
            picture, description);
    }

    /// <summary>
    /// Downloads a Lightless profile picture and returns it as a base64 PNG, or null if there's none or it fails
    /// (Laci then shows its default picture). The login token is only sent to the Lightless server itself.
    /// </summary>
    private async Task<string?> DownloadProfilePictureAsync(string? url)
    {
        if (string.IsNullOrWhiteSpace(url)) return null;

        try
        {
            var serverUri = new Uri(_llServerConfigurationManager.GetServerByIndex(ServerIndex).ServerUri);
            var baseUri = new UriBuilder(serverUri)
            {
                Scheme = serverUri.Scheme switch
                {
                    "wss" => Uri.UriSchemeHttps,
                    "ws" => Uri.UriSchemeHttp,
                    _ => serverUri.Scheme,
                },
                Path = "/",
            }.Uri;

            // Lightless sends a path on its own server ("/profile-images/{hash}.png"). Anything that resolves elsewhere
            // (another host, "//host/...", other schemes) is refused, so a server can't point clients at other addresses.
            var pictureUri = new Uri(baseUri, url);
            if (!IsSameOrigin(pictureUri, baseUri))
            {
                Logger.LogDebug("[LL] Ignoring profile picture outside the server's origin: {uri}", pictureUri);
                return null;
            }

            using var cts = new CancellationTokenSource(PictureDownloadTimeout);
            using var request = new HttpRequestMessage(HttpMethod.Get, pictureUri)
            {
                Version = _llHttpClient.DefaultRequestVersion,
                VersionPolicy = _llHttpClient.DefaultVersionPolicy,
            };
            // Same headers the shared client sends (user agent, client capabilities)
            foreach (var header in _llHttpClient.DefaultRequestHeaders)
            {
                request.Headers.TryAddWithoutValidation(header.Key, header.Value);
            }
            var token = await _llTokenService.GetOrUpdateToken(ServerIndex, cts.Token).ConfigureAwait(false);
            if (!string.IsNullOrEmpty(token)) request.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);

            // Redirects are not followed (they'd lead off the server's origin). Lightless serves the picture directly.
            using var response = await _pictureHttpClient.SendAsync(request, HttpCompletionOption.ResponseHeadersRead, cts.Token).ConfigureAwait(false);
            if (!response.IsSuccessStatusCode)
            {
                Logger.LogDebug("[LL] Profile picture download from {uri} failed: {status}", pictureUri, response.StatusCode);
                return null;
            }
            if (response.Content.Headers.ContentLength > MaxPictureBytes)
            {
                Logger.LogDebug("[LL] Profile picture at {uri} is too large: {size} bytes", pictureUri, response.Content.Headers.ContentLength);
                return null;
            }

            var bytes = await ReadBoundedAsync(response, cts.Token).ConfigureAwait(false);
            if (bytes == null)
            {
                Logger.LogDebug("[LL] Profile picture at {uri} is larger than {max} bytes", pictureUri, MaxPictureBytes);
                return null;
            }

            var png = ToBase64Png(bytes);
            if (png == null) Logger.LogDebug("[LL] Profile picture at {uri} has too many pixels", pictureUri);
            return png;
        }
        catch (Exception ex)
        {
            Logger.LogDebug(ex, "[LL] Could not load profile picture from {url}", url);
            return null;
        }
    }

    protected override void Dispose(bool disposing)
    {
        base.Dispose(disposing);
        if (disposing) _pictureHttpClient.Dispose();
    }

    private static bool IsSameOrigin(Uri uri, Uri origin) =>
        (string.Equals(uri.Scheme, Uri.UriSchemeHttps, StringComparison.Ordinal)
         || string.Equals(uri.Scheme, Uri.UriSchemeHttp, StringComparison.Ordinal))
        && string.Equals(uri.Scheme, origin.Scheme, StringComparison.OrdinalIgnoreCase)
        && string.Equals(uri.Host, origin.Host, StringComparison.OrdinalIgnoreCase)
        && uri.Port == origin.Port;

    private static async Task<byte[]?> ReadBoundedAsync(HttpResponseMessage response, CancellationToken ct)
    {
        using var stream = await response.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
        using var buffer = new MemoryStream();
        var chunk = new byte[81920];
        int read;
        while ((read = await stream.ReadAsync(chunk, ct).ConfigureAwait(false)) > 0)
        {
            await buffer.WriteAsync(chunk.AsMemory(0, read), ct).ConfigureAwait(false);
            if (buffer.Length > MaxPictureBytes) return null;
        }
        return buffer.ToArray();
    }

    /// <summary>
    /// Re-encodes any image format ImageSharp reads (e.g. WebP) as a still PNG of at most 256x256,
    /// which Laci's profile windows can display.
    /// </summary>
    private static string? ToBase64Png(byte[] imageBytes)
    {
        // Check the header before decoding: a small file can declare huge dimensions (decompression bomb)
        var info = Image.Identify(imageBytes);
        if ((long)info.Width * info.Height > MaxDecodedPixels) return null;

        // Only the first frame of animated images is decoded
        using var image = Image.Load(new DecoderOptions { MaxFrames = 1 }, imageBytes);
        if (image.Width > MaxPictureDimension || image.Height > MaxPictureDimension)
        {
            image.Mutate(x => x.Resize(new ResizeOptions
            {
                Mode = ResizeMode.Max,
                Size = new Size(MaxPictureDimension, MaxPictureDimension),
            }));
        }

        using var png = new MemoryStream();
        image.SaveAsPng(png);
        return Convert.ToBase64String(png.ToArray());
    }
}
