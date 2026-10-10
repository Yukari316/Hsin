namespace Hsin.Application;

internal static class CommandLine
{
    public static CommandOptions Parse(string[] arguments)
    {
        if (arguments.Length > 0 && arguments[0] == "--media-guardian")
        {
            if (!OperatingSystem.IsLinux() || arguments.Length != 3)
                throw new ArgumentException("Linux MediaMTX guardian requires an executable and configuration path.");
            return new CommandOptions
            {
                Kind               = CommandKind.Guardian,
                MediaExecutable    = Path.GetFullPath(arguments[1]),
                MediaConfiguration = Path.GetFullPath(arguments[2])
            };
        }

        CommandKind kind  = CommandKind.Server;
        int         index = 0;
        if (arguments is ["mediamtx", _, ..])
        {
            kind = arguments[1] switch
                   {
                       "update"   => CommandKind.Update,
                       "rollback" => CommandKind.Rollback,
                       _          => throw new ArgumentException($"Unknown MediaMTX command: {arguments[1]}")
                   };
            index = 2;
        }

        string  configPath   = Path.Combine(AppContext.BaseDirectory, "hsin.json");
        string? version      = null;
        string? mediaPackage = null;
        for (; index < arguments.Length; index++)
            switch (arguments[index])
            {
                case "--config" when index + 1 < arguments.Length:
                    configPath = Path.GetFullPath(arguments[++index]);
                    break;
                case "--version" when kind == CommandKind.Update && index + 1 < arguments.Length:
                    version = arguments[++index];
                    break;
                case "--mediamtx-package" when kind == CommandKind.Server && index + 1 < arguments.Length:
                    mediaPackage = Path.GetFullPath(arguments[++index]);
                    break;
                default:
                    throw new ArgumentException($"Unknown command or option: {arguments[index]}");
            }

        return new CommandOptions
        {
            Kind         = kind,
            ConfigPath   = configPath,
            Version      = version,
            MediaPackage = mediaPackage
        };
    }
}
