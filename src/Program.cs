using System;
using System.Windows.Forms;

namespace Ow2ServerPicker
{
    internal static class Program
    {
        [STAThread]
        private static void Main()
        {
            Application.EnableVisualStyles();
            Application.SetCompatibleTextRenderingDefault(false);

            Catalog catalog;
            string source;
            try
            {
                catalog = Catalog.Load(out source);
            }
            catch (Exception ex)
            {
                MessageBox.Show(
                    "Could not load the server catalog.\r\n\r\n" + ex.Message
                    + "\r\n\r\nPut a valid servers.json next to this executable and try again.",
                    "Overwatch 2 Server Picker", MessageBoxButtons.OK, MessageBoxIcon.Error);
                return;
            }

            if (catalog.Warnings.Count > 0)
            {
                // Surfaced rather than swallowed: a silently dropped range is a range that
                // quietly stops being blocked.
                string text = "The catalog loaded with warnings:\r\n\r\n"
                            + string.Join("\r\n", catalog.Warnings.ToArray());
                MessageBox.Show(text, "Catalog warnings", MessageBoxButtons.OK, MessageBoxIcon.Warning);
            }

            Application.Run(new MainForm(catalog, source));
        }
    }
}
