using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Reflection;
using System.Runtime.InteropServices;
using System.Security.Cryptography;

namespace AzerothLauncher
{
    // Small, self-contained WebP reader for SurvivorsForge artwork. The native decoder is
    // bundled as an embedded resource so Windows installations do not need a codec pack.
    internal static class WebPDecoder
    {
        const uint LoadWithAlteredSearchPath = 0x00000008;
        const int MaxPixels = 32000000;
        static readonly object gate = new object();
        static bool attempted;
        static IntPtr module;

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, EntryPoint = "LoadLibraryExW", SetLastError = true)]
        static extern IntPtr LoadLibraryEx(string fileName, IntPtr file, uint flags);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPGetInfo")]
        static extern int GetInfo(byte[] data, UIntPtr dataSize, out int width, out int height);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPDecodeBGRA")]
        static extern IntPtr DecodeBgra(byte[] data, UIntPtr dataSize, out int width, out int height);

        [DllImport("libwebp.dll", CallingConvention = CallingConvention.Cdecl, EntryPoint = "WebPFree")]
        static extern void Free(IntPtr data);

        public static Image LoadImage(string path)
        {
            if (string.IsNullOrWhiteSpace(path) || !File.Exists(path)) return null;
            try { using (Image image = Image.FromFile(path)) return (Image)image.Clone(); }
            catch (Exception)
            {
                if (!string.Equals(Path.GetExtension(path), ".webp", StringComparison.OrdinalIgnoreCase)) return null;
                try
                {
                    if (new FileInfo(path).Length > 16 * 1024 * 1024) return null;
                    return Decode(File.ReadAllBytes(path));
                }
                catch (Exception) { return null; }
            }
        }

        public static Image LoadImage(byte[] bytes)
        {
            if (bytes == null || bytes.Length == 0 || bytes.Length > 16 * 1024 * 1024) return null;
            try { using (var stream = new MemoryStream(bytes)) using (Image image = Image.FromStream(stream)) return (Image)image.Clone(); }
            catch (Exception) { return Decode(bytes); }
        }

        static Image Decode(byte[] bytes)
        {
            if (bytes == null || bytes.Length < 12 || bytes.Length > 16 * 1024 * 1024 || !EnsureLoaded()) return null;
            int width, height;
            if (GetInfo(bytes, (UIntPtr)(uint)bytes.Length, out width, out height) == 0 || !ValidSize(width, height)) return null;
            IntPtr pixels = DecodeBgra(bytes, (UIntPtr)(uint)bytes.Length, out width, out height);
            if (pixels == IntPtr.Zero || !ValidSize(width, height))
            {
                if (pixels != IntPtr.Zero) Free(pixels);
                return null;
            }

            Bitmap result = null;
            try
            {
                result = new Bitmap(width, height, PixelFormat.Format32bppArgb);
                Rectangle bounds = new Rectangle(0, 0, width, height);
                BitmapData target = result.LockBits(bounds, ImageLockMode.WriteOnly, PixelFormat.Format32bppArgb);
                try
                {
                    int rowBytes = checked(width * 4);
                    byte[] row = new byte[rowBytes];
                    for (int y = 0; y < height; y++)
                    {
                        Marshal.Copy(IntPtr.Add(pixels, checked(y * rowBytes)), row, 0, rowBytes);
                        Marshal.Copy(row, 0, IntPtr.Add(target.Scan0, checked(y * target.Stride)), rowBytes);
                    }
                }
                finally { result.UnlockBits(target); }
                Image image = result;
                result = null;
                return image;
            }
            catch (Exception) { return null; }
            finally
            {
                if (result != null) result.Dispose();
                Free(pixels);
            }
        }

        static bool ValidSize(int width, int height)
        {
            return width > 0 && height > 0 && width <= 8192 && height <= 8192 && (long)width * height <= MaxPixels;
        }

        static bool EnsureLoaded()
        {
            lock (gate)
            {
                if (attempted) return module != IntPtr.Zero;
                attempted = true;
                try
                {
                    string arch = IntPtr.Size == 8 ? "x64" : "x86";
                    string dir = Path.Combine(Path.Combine(Path.Combine(Util.DataDir, "SurvivorsForge"), "codec"), arch);
                    Directory.CreateDirectory(dir);
                    bool x64 = arch == "x64";
                    Extract("AS.webp." + arch + ".libsharpyuv.dll", Path.Combine(dir, "libsharpyuv.dll"),
                        x64 ? "CEBA59E40848E8E14960FC8C0F6ED125A6BFAE216B3814C5AD202CB36D6A2A0D" : "F77FEA0112CC1B806DC7D5DA79D7C6AD034C5728BD6369318FD2546CB2AC0D0B");
                    Extract("AS.webp." + arch + ".libwebp.dll", Path.Combine(dir, "libwebp.dll"),
                        x64 ? "3DE66254C8C0F47F299C7D55750589BF0E9FB6AB65778ED004A81E958AD1C024" : "F44546680737179D734C1F0EFE322C8C81CC276C6778ADD5156DF3DE48CF6946");
                    module = LoadLibraryEx(Path.Combine(dir, "libwebp.dll"), IntPtr.Zero, LoadWithAlteredSearchPath);
                }
                catch (Exception) { module = IntPtr.Zero; }
                return module != IntPtr.Zero;
            }
        }

        static void Extract(string resourceName, string path, string expectedSha256)
        {
            Assembly assembly = Assembly.GetExecutingAssembly();
            using (Stream input = assembly.GetManifestResourceStream(resourceName))
            {
                if (input == null) throw new FileNotFoundException("Embedded WebP decoder is missing.", resourceName);
                if (File.Exists(path) && string.Equals(HashFile(path), expectedSha256, StringComparison.OrdinalIgnoreCase)) return;
                string temp = path + ".tmp";
                try
                {
                    using (FileStream output = new FileStream(temp, FileMode.Create, FileAccess.Write, FileShare.None))
                        input.CopyTo(output);
                    if (!string.Equals(HashFile(temp), expectedSha256, StringComparison.OrdinalIgnoreCase))
                        throw new InvalidDataException("Embedded WebP decoder checksum mismatch.");
                    Util.Replace(temp, path);
                }
                finally { try { if (File.Exists(temp)) File.Delete(temp); } catch (Exception) { } }
            }
        }

        static string HashFile(string path)
        {
            using (SHA256 sha = SHA256.Create())
            using (FileStream stream = File.OpenRead(path))
                return BitConverter.ToString(sha.ComputeHash(stream)).Replace("-", "");
        }
    }
}
