using System.Reflection;

namespace DmPayQuery;

public static class AppInfo
{
    private static readonly Assembly s_assembly = Assembly.GetExecutingAssembly();

    public static string ProductName { get; } =
        s_assembly.GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "DmPayQuery";

    public static string Version { get; } =
        GetDisplayVersion();

    public static string MainWindowTitle => $"{ProductName} v{Version}";

    private static string GetDisplayVersion()
    {
        var informationalVersion = s_assembly.GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion;
        if (!string.IsNullOrWhiteSpace(informationalVersion))
            return informationalVersion.Split('+')[0];

        return s_assembly.GetName().Version?.ToString()
        ?? "1.0.0";
    }
}
