using System.Diagnostics;
using System.Security.Principal;

namespace Devlight;

internal static class Program
{
    [STAThread]
    private static void Main()
    {
        using var identity = WindowsIdentity.GetCurrent();
        using var mutex = new Mutex(true, $@"Local\Devlight-{identity.User?.Value}", out bool firstInstance);
        if (!firstInstance) return;
        try
        {
            ApplicationConfiguration.Initialize();
            using var context = new TrayApplicationContext();
            Application.Run(context);
        }
        catch (Exception exception)
        {
            Debug.WriteLine(exception);
            MessageBox.Show("Devlight could not start. Check your user profile permissions and try again.",
                "Devlight", MessageBoxButtons.OK, MessageBoxIcon.Error);
        }
        finally { mutex.ReleaseMutex(); }
    }
}
