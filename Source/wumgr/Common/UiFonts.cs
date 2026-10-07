using System;
using System.Collections.Generic;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace wumgr
{
    internal static class UiFonts
    {
        private static readonly PrivateFontCollection fonts = new PrivateFontCollection();
        private static readonly FontFamily family;
        // GDI+ keeps references to these buffers; retain them for the process lifetime.
        private static readonly List<IntPtr> fontBuffers = new List<IntPtr>();

        [DllImport("gdi32.dll", ExactSpelling = true)]
        private static extern IntPtr AddFontMemResourceEx(IntPtr data, uint size, IntPtr reserved, ref uint count);

        static UiFonts()
        {
            foreach (string file in new[] { "Ubuntu-Regular.ttf", "Ubuntu-Bold.ttf", "Ubuntu-Italic.ttf", "Ubuntu-BoldItalic.ttf" })
            {
                using (Stream stream = typeof(UiFonts).Assembly.GetManifestResourceStream("wumgr.Fonts." + file))
                using (MemoryStream buffer = new MemoryStream())
                {
                    if (stream == null)
                        throw new InvalidOperationException("Missing embedded font: " + file);
                    stream.CopyTo(buffer);
                    byte[] bytes = buffer.ToArray();
                    IntPtr memory = Marshal.AllocHGlobal(bytes.Length);
                    Marshal.Copy(bytes, 0, memory, bytes.Length);
                    fontBuffers.Add(memory);
                    fonts.AddMemoryFont(memory, bytes.Length);
                    uint count = 0;
                    // TextRenderer and native controls use GDI rather than GDI+.
                    if (AddFontMemResourceEx(memory, (uint)bytes.Length, IntPtr.Zero, ref count) == IntPtr.Zero)
                        throw new InvalidOperationException("Unable to register embedded font: " + file);
                }
            }
            family = fonts.Families[0];
        }

        internal static Font Create(float size, FontStyle style = FontStyle.Regular, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return new Font(family, size, style, unit);
        }

        internal static void Apply(Control control)
        {
            if (control.Font.Name != "Segoe Fluent Icons" && control.Font.Name != "Ubuntu")
                control.Font = Create(control.Font.SizeInPoints, control.Font.Style);
            foreach (Control child in control.Controls)
                Apply(child);
        }
    }
}
