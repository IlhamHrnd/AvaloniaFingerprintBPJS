using System;
using System.IO;
using SkiaSharp;
using AvaloniaBitmap = Avalonia.Media.Imaging.Bitmap;

namespace AvaloniaFingerprint.Services;

/// <summary>
/// Replacement for the WinForms <c>BitmapStr</c> helper: turns the reader's raw 8-bit
/// grayscale buffer into a JPEG (base64, exactly what the BPJS endpoint expects) and back
/// into an Avalonia bitmap for display. Uses Skia, which Avalonia already ships with,
/// instead of System.Drawing.
/// </summary>
public static class ImageCodec
{
    public static byte[] GrayscaleToJpeg(byte[] gray, int width, int height, int quality = 90)
    {
        if (gray is null || width <= 0 || height <= 0)
            return Array.Empty<byte>();

        var info = new SKImageInfo(width, height, SKColorType.Bgra8888, SKAlphaType.Opaque);
        using var bitmap = new SKBitmap(info);

        var pixels = new byte[width * height * 4];
        var count = Math.Min(gray.Length, width * height);
        for (var i = 0; i < count; i++)
        {
            var v = gray[i];
            var o = i * 4;
            pixels[o] = v;      // B
            pixels[o + 1] = v;  // G
            pixels[o + 2] = v;  // R
            pixels[o + 3] = 255;
        }

        System.Runtime.InteropServices.Marshal.Copy(pixels, 0, bitmap.GetPixels(), pixels.Length);

        using var image = SKImage.FromBitmap(bitmap);
        using var data = image.Encode(SKEncodedImageFormat.Jpeg, quality);
        return data.ToArray();
    }

    public static string GrayscaleToJpegBase64(byte[] gray, int width, int height)
    {
        var jpeg = GrayscaleToJpeg(gray, width, height);
        return jpeg.Length == 0 ? string.Empty : Convert.ToBase64String(jpeg);
    }

    public static AvaloniaBitmap? Base64ToBitmap(string? base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
            return null;

        try
        {
            using var stream = new MemoryStream(Convert.FromBase64String(base64));
            return new AvaloniaBitmap(stream);
        }
        catch (Exception)
        {
            return null;
        }
    }
}
