namespace UsageIndicatorForCodex.Update;

internal static class ProductConstants
{
    internal const int BootstrapProtocolVersion = 1;
    internal const string InstallerPrefix = "UsageIndicatorForCodex-Setup-v";
    internal const string InstallerStateSubKey = @"Software\UsageIndicatorForCodex\Installer";
    internal const string BootstrapVersionValue = "BootstrapVersion";
    internal const string InstallPathValue = "InstallPath";
    internal const string InstalledVersionValue = "InstalledVersion";
    internal const string LauncherRelativePath = @"bin\usage-indicator.exe";
    internal const string UpdateHostRelativePath = @"updater\UsageIndicatorForCodex.UpdateHost.exe";
    internal const string GuiRelativePath = @"app\UsageIndicatorForCodex.Gui.exe";
}
