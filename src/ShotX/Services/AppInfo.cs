using System.Reflection;

namespace ShotX.Services;

public static class AppInfo
{
    public const string RepoOwner = "JaisCoreX423";
    public const string RepoName = "ShotX";
    public const string RepoUrl = "https://github.com/" + RepoOwner + "/" + RepoName;
    public const string Credits = "Developed by JaisCoreX & Claude";

    public static Version Version { get; } = ResolveVersion();

    public static string VersionText => $"{Version.Major}.{Version.Minor}.{Version.Build}";

    private static Version ResolveVersion()
    {
        // SHOTX_TEST_VERSION lets a test build pretend to be older, to exercise the updater.
        var overrideText = Environment.GetEnvironmentVariable("SHOTX_TEST_VERSION");
        if (!string.IsNullOrEmpty(overrideText) && Version.TryParse(overrideText, out var overridden))
            return overridden;

        return Assembly.GetExecutingAssembly().GetName().Version ?? new Version(0, 0, 0);
    }
}
