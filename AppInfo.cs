using System.Reflection;

namespace DmPayQuery;

public static class AppInfo
{
    public static string ProductName { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyProductAttribute>()?.Product
        ?? "DmPayQuery";

    public static string Version { get; } =
        Assembly.GetExecutingAssembly().GetCustomAttribute<AssemblyInformationalVersionAttribute>()?.InformationalVersion
        ?? Assembly.GetExecutingAssembly().GetName().Version?.ToString()
        ?? "1.0.0";

    public static string MainWindowTitle => $"{ProductName} v{Version}";
}
