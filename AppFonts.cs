using System;
using System.Drawing;
using System.Drawing.Text;
using System.IO;
using System.Runtime.InteropServices;

namespace Translater
{
    internal static class AppFonts
    {
        private static readonly PrivateFontCollection Fonts = Load();
        [DllImport("gdi32.dll", CharSet = CharSet.Unicode)]
        private static extern int AddFontResourceEx(string path, uint flags, IntPtr reserved);

        private static PrivateFontCollection Load()
        {
            var fonts = new PrivateFontCollection();
            string directory = Path.Combine(AppDomain.CurrentDomain.BaseDirectory, "fonts");
            foreach (string file in new[] { "Ubuntu-R.ttf", "NotoSansCJKsc-Regular.otf" })
            {
                string path = Path.Combine(directory, file);
                AddFontResourceEx(path, 0x10, IntPtr.Zero);
                fonts.AddFontFile(path);
            }
            return fonts;
        }

        public static Font Chinese(float size, GraphicsUnit unit = GraphicsUnit.Point)
        {
            return Create("Noto Sans CJK SC", size, unit);
        }

        public static Font Latin(float size)
        {
            return Create("Ubuntu", size, GraphicsUnit.Point);
        }

        private static Font Create(string name, float size, GraphicsUnit unit)
        {
            foreach (var family in Fonts.Families)
                if (family.Name == name) return new Font(family, size, FontStyle.Regular, unit);
            throw new InvalidOperationException("应用字体缺失，请保留程序旁的 fonts 文件夹。");
        }
    }
}
