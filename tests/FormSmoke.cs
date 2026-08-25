using System;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    /// <summary>Constructs the real form outside the message loop so exceptions print instead of popping a dialog.</summary>
    internal static class FormSmoke
    {
        [STAThread]
        private static int Main()
        {
            try
            {
                Application.EnableVisualStyles();
                // Otherwise a message-loop exception opens the modal WinForms error dialog
                // and this harness just appears to hang.
                Application.SetUnhandledExceptionMode(UnhandledExceptionMode.ThrowException);
                string source;
                Catalog cat = Catalog.Load(out source);
                Console.WriteLine("catalog: " + source + " (" + cat.Datacenters.Count + " datacenters, "
                                  + cat.Warnings.Count + " warnings)");
                foreach (string w in cat.Warnings) Console.WriteLine("  warn: " + w);

                using (MainForm f = new MainForm(cat, source))
                {
                    f.Show();
                    for (int i = 0; i < 20; i++) Application.DoEvents();
                    Console.WriteLine("form constructed and shown OK; size=" + f.ClientSize);
                    f.Close();
                }
                Console.WriteLine("SMOKE OK");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("SMOKE FAILED");
                Console.WriteLine(ex.ToString());
                return 1;
            }
        }
    }
}
