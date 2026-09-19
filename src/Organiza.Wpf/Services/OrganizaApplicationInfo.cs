namespace Organiza.Wpf.Services;

public static class OrganizaApplicationInfo
{
    private static readonly Version Version = typeof(OrganizaApplicationInfo).Assembly.GetName().Version
                                              ?? new Version(0, 0, 0);

    public static string ProductName => "Organiza";

    public static string DisplayName => $"{ProductName} V{Version.Major}.{Version.Minor}.{Version.Build}";
}
