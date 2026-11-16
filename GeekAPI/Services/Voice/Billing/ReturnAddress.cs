namespace GeekAPI.Services.Voice.Billing;

/// <summary>Where PayPal may send a visitor or owner back to: an absolute https address, or http on localhost for the portal run on this machine.</summary>
public static class ReturnAddress
{
    public static Uri? Parse(string? value)
    {
        if (!Uri.TryCreate(value, UriKind.Absolute, out var uri)) return null;
        if (uri.Scheme == Uri.UriSchemeHttps) return uri;
        return uri.Scheme == Uri.UriSchemeHttp && uri.IsLoopback ? uri : null;
    }
}
