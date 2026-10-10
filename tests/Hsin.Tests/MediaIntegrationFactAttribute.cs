namespace Hsin.Tests;

public sealed class MediaIntegrationFactAttribute : FactAttribute
{
    public MediaIntegrationFactAttribute()
    {
        if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable("HSIN_TEST_MEDIAMTX")))
            Skip = "HSIN_TEST_MEDIAMTX is required for real media integration.";
    }
}
