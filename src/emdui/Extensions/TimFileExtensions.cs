﻿using System;
using System.IO;
using System.Windows;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using IntelOrca.Biohazard;

namespace emdui.Extensions
{
    internal static class TimFileExtensions
    {
        public static TimFile ToTimFile(this BitmapSource source)
        {
            var timFile = new TimFile(source.PixelWidth, source.PixelHeight, 8);
            var pixels = new uint[timFile.Width * timFile.Height];
            var convertedFrame = new FormatConvertedBitmap(source, PixelFormats.Bgr32, null, 0);
            convertedFrame.CopyPixels(pixels, timFile.Width * 4, 0);

            var numPages = timFile.Width / 128;
            for (var page = 0; page < numPages; page++)
            {
                var palette = GetPalette(pixels, timFile.Width, new Int32Rect(page * 128, 0, 128, timFile.Height));
                timFile.SetPalette(page, palette);
            }

            timFile.ImportPixels(pixels, (x, y) => x / 128);
            return timFile;
        }

        public static BitmapSource ToBitmap(this TimFile timFile)
        {
            var pixels = timFile.GetPixels((x, y) => x / 128);
            return BitmapSource.Create(timFile.Width, timFile.Height, 96, 96, PixelFormats.Bgra32, null, pixels, timFile.Width * 4);
        }

        public static void Save(this BitmapSource source, string path)
        {
            var bitmapEncoder = new PngBitmapEncoder();
            bitmapEncoder.Frames.Add(BitmapFrame.Create(source));
            using (var fs = new FileStream(path, FileMode.Create, FileAccess.Write))
            {
                bitmapEncoder.Save(fs);
            }
        }

        private static ushort[] GetPalette(Span<uint> pixels, int stride, Int32Rect rect)
        {
            var palette = new ushort[256];
            var paletteIndex = 0;
            for (var y = rect.Y; y < rect.Y + rect.Height; y++)
            {
                for (var x = rect.X; x < rect.X + rect.Width; x++)
                {
                    var p = pixels[y * stride + x];
                    var entry = TimFile.Convert32to16(p);
                    var entryIndex = -1;
                    for (var k = 0; k < paletteIndex; k++)
                    {
                        if (palette[k] == entry)
                        {
                            entryIndex = k;
                            break;
                        }
                    }
                    if (entryIndex == -1)
                    {
                        palette[paletteIndex] = TimFile.Convert32to16(p);
                        paletteIndex++;
                        if (paletteIndex >= palette.Length)
                        {
                            break;
                        }
                    }
                }
            }
            return palette;
        }

        public static TimFile WithWeaponTexture(this TimFile texture, TimFile weaponTexture)
        {
            var tim = texture.Clone();
            for (var y = 0; y < 32; y++)
            {
                for (var x = 0; x < 56; x++)
                {
                    var p = weaponTexture.GetPixel(x, y);
                    tim.SetPixel(200 + x, 224 + y, 1, p);
                }
            }
            return tim;
        }

        public static TimFile ExportPage(this TimFile tim, int page)
        {
            var palette = tim.GetPalette(page);
            var timFile = new TimFile(128, 256, 8);
            timFile.SetPalette(0, palette);
            var srcX = page * 128;
            for (var y = 0; y < 256; y++)
            {
                for (var x = 0; x < 128; x++)
                {
                    var p = tim.GetRawPixel(srcX + x, y);
                    timFile.SetRawPixel(x, y, p);
                }
            }
            return timFile;
        }

        public static void ImportPage(this TimFile target, int targetPage, TimFile source)
        {
            var palette = source.GetPalette(0);
            target.SetPalette(targetPage, palette);

            var minWidth = (targetPage + 1) * 128;
            if (target.Width < minWidth)
            {
                target.ResizeImage(minWidth, target.Height);
            }

            var dstX = targetPage * 128;
            for (var y = 0; y < 256; y++)
            {
                for (var x = 0; x < 128; x++)
                {
                    var p = source.GetRawPixel(x, y);
                    target.SetRawPixel(dstX + x, y, p);
                }
            }
        }
    }
}
