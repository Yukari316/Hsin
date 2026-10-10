using System.Net;
using System.Text.RegularExpressions;
using Hsin.Configuration.Models;
using Serilog.Events;

namespace Hsin.Configuration;

internal static class ConfigurationValidator
{
    private const int    MINIMUM_PORT         = 1;
    private const int    MAXIMUM_PORT         = 65535;
    private const string CONTROL_USER_PATTERN = "^[A-Za-z0-9_-]+$";

    internal static void Validate(HsinOptions options, string configPath)
    {
        ValidatePort(options.WebUi.Port, "WebUi.Port");
        ValidatePort(options.MediaMtx.Port, "MediaMtx.Port");
        ValidatePort(options.MediaMtx.ControlApi.Port, "MediaMtx.ControlApi.Port");
        ValidateListenAddress(options.WebUi.ListenAddress, "WebUi.ListenAddress");
        ValidateListenAddress(options.MediaMtx.ListenAddress, "MediaMtx.ListenAddress");
        ValidateListenAddress(options.MediaMtx.ControlApi.ListenAddress, "MediaMtx.ControlApi.ListenAddress");
        if (EndpointConflicts(
                options.WebUi.ListenAddress,
                options.WebUi.Port,
                options.MediaMtx.ListenAddress,
                options.MediaMtx.Port))
            throw new InvalidDataException("WebUi and MediaMtx listeners overlap.");
        if (EndpointConflicts(
                options.MediaMtx.ListenAddress,
                options.MediaMtx.Port,
                options.MediaMtx.ControlApi.ListenAddress,
                options.MediaMtx.ControlApi.Port))
            throw new InvalidDataException("MediaMtx and ControlApi listeners overlap.");
        if (EndpointConflicts(
                options.WebUi.ListenAddress,
                options.WebUi.Port,
                options.MediaMtx.ControlApi.ListenAddress,
                options.MediaMtx.ControlApi.Port))
            throw new InvalidDataException("WebUi and ControlApi listeners overlap.");
        switch (options.Logging.FileEnabled)
        {
            case true when options.Logging.RetentionDays <= 0:
                throw new InvalidDataException("Logging.RetentionDays must be positive when file logging is enabled.");
            case true when string.IsNullOrWhiteSpace(options.Logging.Directory):
                throw new InvalidDataException("Logging.Directory cannot be empty when file logging is enabled.");
        }

        bool certificateConfigured = !string.IsNullOrWhiteSpace(options.Tls.CertificateFile);
        bool keyConfigured         = !string.IsNullOrWhiteSpace(options.Tls.PrivateKeyFile);
        if (certificateConfigured != keyConfigured)
            throw new InvalidDataException("Tls.CertificateFile and Tls.PrivateKeyFile must be configured together.");
        string expectedScheme = certificateConfigured ? Uri.UriSchemeHttps : Uri.UriSchemeHttp;
        ValidateOrigin(options.WebUi.PublicOrigin, "WebUi.PublicOrigin", expectedScheme);
        ValidateOrigin(options.MediaMtx.PublicOrigin, "MediaMtx.PublicOrigin", expectedScheme);
        if (string.IsNullOrWhiteSpace(options.MediaMtx.DataDirectory))
            throw new InvalidDataException("MediaMtx.DataDirectory cannot be empty.");
        ValidateControlApi(options);
        if (options.Publish.BearerToken.Any(char.IsControl) || options.WebUi.Token.Any(char.IsControl))
            throw new InvalidDataException($"Credentials in '{configPath}' cannot contain control characters.");
        if (options.MediaMtx.ControlApi.User.Any(char.IsControl)
            || options.MediaMtx.ControlApi.Password.Any(char.IsControl))
            throw new InvalidDataException("MediaMtx.ControlApi credentials cannot contain control characters.");
        if (string.IsNullOrWhiteSpace(options.Logging.MinimumLevel))
            throw new InvalidDataException("Logging.MinimumLevel cannot be empty.");
        if (!Enum.TryParse(options.Logging.MinimumLevel, true, out LogEventLevel level)
            || !Enum.IsDefined(level))
            throw new InvalidDataException(
                $"Logging.MinimumLevel '{options.Logging.MinimumLevel}' is not a valid Serilog level.");
    }

    internal static void ValidateSections(HsinOptions options)
    {
        if (options.WebUi is null
            || options.Publish is null
            || options.Appearance is null
            || options.Tls is null
            || options.Logging is null
            || options.MediaMtx is null
            || options.MediaMtx.ControlApi is null)
            throw new InvalidDataException("Configuration sections cannot be null.");
        if (options.Publish.BearerToken is null
            || options.WebUi.Token is null
            || options.Tls.CertificateFile is null
            || options.Tls.PrivateKeyFile is null
            || options.MediaMtx.ControlApi.User is null
            || options.MediaMtx.ControlApi.Password is null)
            throw new InvalidDataException("Credential and TLS configuration fields cannot be null.");
        if (options.MediaMtx.AdditionalHosts is null
            || options.MediaMtx.IceServers is null
            || options.MediaMtx.ControlApi.AllowedIps is null)
            throw new InvalidDataException("MediaMtx array configuration fields cannot be null.");
    }

    private static void ValidatePort(int value, string name)
    {
        if (value is < MINIMUM_PORT or > MAXIMUM_PORT)
            throw new InvalidDataException($"{name} must be between 1 and 65535.");
    }

    private static void ValidateOrigin(string configured, string name, string expectedScheme)
    {
        if (!Uri.TryCreate(configured, UriKind.Absolute, out Uri? origin)
            || origin.Scheme != expectedScheme
            || !string.IsNullOrEmpty(origin.UserInfo)
            || origin.AbsolutePath != "/"
            || !string.IsNullOrEmpty(origin.Query)
            || !string.IsNullOrEmpty(origin.Fragment)
            || string.IsNullOrEmpty(origin.Host))
            throw new InvalidDataException(
                $"{name} must be a {expectedScheme} origin without credentials, path, query or fragment.");
    }

    private static void ValidateListenAddress(string address, string name)
    {
        if (!string.IsNullOrEmpty(address)
            && (address is "*" or "localhost" || IPAddress.TryParse(address, out _)))
            return;
        throw new InvalidDataException($"{name} must be an IP address, localhost or '*'.");
    }

    private static void ValidateControlApi(HsinOptions configuration)
    {
        ControlApiOptions options = configuration.MediaMtx.ControlApi;
        string publishSecret = configuration.Publish.BearerToken.StartsWith(
            PublishOptions.BEARER_PREFIX,
            StringComparison.Ordinal)
            ? configuration.Publish.BearerToken[PublishOptions.BEARER_PREFIX.Length..]
            : "";

        if (!Regex.IsMatch(options.User, CONTROL_USER_PATTERN)
            || options.User.Equals("viewer", StringComparison.OrdinalIgnoreCase)
            || options.User.Equals("publisher", StringComparison.OrdinalIgnoreCase)
            || options.User.Equals("any", StringComparison.OrdinalIgnoreCase))
            throw new InvalidDataException(
                "MediaMtx.ControlApi.User must be a distinct management username using ASCII letters, digits, '-' or '_'.");

        string    targetAddress = ControlTargetAddress(options.ListenAddress);
        IPAddress target        = IPAddress.Parse(targetAddress);
        if (!IPAddress.IsLoopback(target) || IsWildcard(options.ListenAddress))
        {
            if (string.IsNullOrEmpty(options.Password) || options.AllowedIps.Count == 0)
                throw new InvalidDataException("Non-loopback ControlApi requires a password and non-empty AllowedIps.");
            if (options.Password == configuration.WebUi.Token
                || options.Password == configuration.Publish.BearerToken
                || options.Password == publishSecret)
                throw new InvalidDataException(
                    "MediaMtx.ControlApi.Password must differ from viewer and publisher credentials.");
        }

        if (options.AllowedIps.Count == 0)
            throw new InvalidDataException(
                "MediaMtx.ControlApi.AllowedIps must include Hsin's local API connection source.");
        bool targetAllowed      = false;
        bool localSourceAllowed = false;
        foreach (string value in options.AllowedIps)
            if (IPAddress.TryParse(value, out IPAddress? address))
            {
                targetAllowed      |= address.Equals(target);
                localSourceAllowed |= address.Equals(IPAddress.Loopback) || address.Equals(IPAddress.IPv6Loopback);
            }
            else if (IPNetwork.TryParse(value, out IPNetwork network))
            {
                targetAllowed      |= network.Contains(target);
                localSourceAllowed |= network.Contains(IPAddress.Loopback) || network.Contains(IPAddress.IPv6Loopback);
            }
            else
            {
                throw new InvalidDataException(
                    "MediaMtx.ControlApi.AllowedIps must contain only IP addresses or CIDR ranges.");
            }

        if (!(IsWildcard(options.ListenAddress) ? localSourceAllowed : targetAllowed))
            throw new InvalidDataException(
                "MediaMtx.ControlApi.AllowedIps excludes Hsin's local API connection source.");
    }

    internal static string ControlTargetAddress(string listenAddress)
    {
        if (IsWildcard(listenAddress) || listenAddress == "localhost")
            return IPAddress.Loopback.ToString();
        return listenAddress;
    }

    private static bool EndpointConflicts(string firstAddress, int firstPort, string secondAddress, int secondPort)
    {
        if (firstPort != secondPort)
            return false;
        if (IsWildcard(firstAddress) || IsWildcard(secondAddress))
            return true;
        return string.Equals(
            firstAddress.Trim('[', ']'),
            secondAddress.Trim('[', ']'),
            StringComparison.OrdinalIgnoreCase);
    }

    private static bool IsWildcard(string address)
        => address is "0.0.0.0" or "::" or "*";
}
