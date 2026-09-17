using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace AvaloniaFingerprint.Services
{
    public  class BaseService(IConfiguration config)
    {
        protected IConfiguration config { get; set; } = config;
        protected string url = config["Link:DatinAPI"] ?? string.Empty;

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
    }
}
