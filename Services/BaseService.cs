global using static AvaloniaFingerprint.Models.Master.Response;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RestSharp;
using SkiaSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace AvaloniaFingerprint.Services
{
    public  class BaseService(IConfiguration config, SessionService session)
    {
        protected IConfiguration config { get; set; } = config;
        protected string url = config["Apps:DatinAPI"] ?? string.Empty;
        protected int timeout = int.Parse(config["Apps:Timeout"] ?? string.Empty);
        protected SessionService Session { get; set; } = session;
        protected List<Models.Master.Parameter> headers(string userId)
        {
            List<Models.Master.Parameter> headers =
            [
                new Models.Master.Parameter
                {
                    name = "accept",
                    value = "application/json"
                },
                new Models.Master.Parameter
                {
                    name = "Content-Type",
                    value = "application/json"
                }
            ];

            if (!string.IsNullOrEmpty(userId)) headers.Add(new Models.Master.Parameter
            {
                name = "Authorization",
                value = $"Bearer {userId}"
            });

            return headers;
        }

        // Noted
        // 1. Unit -> untuk konversi timeout dalam detik, menit atau jam
        // 2. Method -> untuk proses pengiriman berupa Get, Put, Patch, Delete atau yang lain
        // 3. Method wajib diisi
        // 4. Jika file tidak null berarti dia akan upload file entah gambar atau dokumen
        // 5. Dan field diletakkan didalam body
        protected RestResponse RestResponse(string url, string endpoint, int timeOut, List<Models.Master.Parameter> headers, int unit, int method, object? body = null, IFormFile? file = null, List<Models.Master.Parameter>? param = null)
        {
            if (string.IsNullOrEmpty(endpoint)) return new RestResponse();
            if (method <= 0) return new RestResponse();
            TimeSpan timeSpan = unit switch
            {
                1 => TimeSpan.FromSeconds(timeOut),
                2 => TimeSpan.FromMinutes(timeOut),
                3 => TimeSpan.FromHours(timeOut),
                4 => TimeSpan.FromDays(timeOut),
                _ => TimeSpan.FromMilliseconds(timeOut),
            };

            var client = new RestClient(url);
            var request = new RestRequest(endpoint)
            {
                Timeout = timeSpan,
                Method = method switch
                {
                    1 => Method.Get,
                    2 => Method.Post,
                    3 => Method.Put,
                    4 => Method.Delete,
                    5 => Method.Patch,
                    6 => Method.Head,
                    7 => Method.Options,
                    _ => throw new ArgumentException("Invalid HTTP method"),
                }
            };

            if (file != null && file.Length > 0)
            {
                request.AlwaysMultipartFormData = true;
                using var ms = new MemoryStream();
                file.CopyTo(ms);
                request.AddFile("file", ms.ToArray(), file.FileName, file.ContentType);
            }

            if (headers.Count > 0)
            {
                foreach (var header in headers)
                {
                    request.AddHeader(header.name, header.value);
                }
            }

            if (param != null && param.Count > 0)
            {
                foreach (var par in param)
                {
                    request.AddParameter(par.name, par.value);
                }
            }

            if (body != null && request.Method != Method.Get) request.AddJsonBody(body);
            var response = client.Execute(request);
            return response;
        }

        // Format untuk hasil scan finger agar bisa diolah.
        // Static + internal so non-derived services (e.g. FingerprintService) can reuse it.
        protected internal static byte[] GrayscaleToJpeg(byte[] gray, int width, int height, int quality = 90)
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

        protected internal static string GrayscaleToJpegBase64(byte[] gray, int width, int height)
        {
            var jpeg = GrayscaleToJpeg(gray, width, height);
            return jpeg.Length == 0 ? string.Empty : Convert.ToBase64String(jpeg);
        }

        public static Avalonia.Media.Imaging.Bitmap? Base64ToBitmap(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            try
            {
                using var stream = new MemoryStream(Convert.FromBase64String(base64));
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }
}
