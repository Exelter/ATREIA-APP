namespace AionDPS;

/// <summary>
/// Opening a web page in the player's browser (12 November 2026, the security check). Windows runs whatever it is given to open: a program, a file, a
/// network share (\\server\…, which also hands over the Windows sign-in to that server) or another program's address (ms-…:, search-ms:, file:). So only a
/// plain https:// address ever goes to it. The links come from the site's answers, a fight's saved page and the news, which are checked where they are read;
/// this is the second lock, in case one ever slips through.
/// </summary>
public static class Links
{
    /// <summary>A web page that may be opened: https, a host, no user name or password in it, nothing around it.</summary>
    public static bool IsWeb(string? url) =>
        url is { Length: > 8 and <= 2048 }
        && url.StartsWith("https://", StringComparison.OrdinalIgnoreCase)
        && url.Trim().Length == url.Length
        && !url.Any(char.IsControl)
        && Uri.TryCreate(url, UriKind.Absolute, out Uri? at)
        && at.Scheme == Uri.UriSchemeHttps
        && !at.IsUnc
        && at.Host.Length > 0
        && at.UserInfo.Length == 0;
}
