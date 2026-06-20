using Tagster.Shell;

namespace Tagster.App;

/// <summary>Headless check (via <c>--integration-test</c>) of the HKCU context-menu round-trip.</summary>
internal static class IntegrationSelfTest
{
    public static (bool Ok, string Message) Run(IExplorerIntegration integration)
    {
        var wasRegistered = integration.IsRegistered;
        try
        {
            integration.Register();
            if (!integration.IsRegistered) return (false, "FAIL: not registered after Register()");

            integration.Unregister();
            if (integration.IsRegistered) return (false, "FAIL: still registered after Unregister()");

            return (true, "PASS: context-menu register/unregister round-trip works");
        }
        catch (Exception ex)
        {
            return (false, "FAIL: " + ex);
        }
        finally
        {
            try
            {
                if (wasRegistered) integration.Register();
                else integration.Unregister();
            }
            catch { /* best effort to restore prior state */ }
        }
    }
}
