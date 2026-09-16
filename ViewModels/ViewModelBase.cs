using AvaloniaFingerprint.Models.Master;
using CommunityToolkit.Mvvm.ComponentModel;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Configuration;
using RestSharp;
using System;
using System.Collections.Generic;
using System.IO;

namespace AvaloniaFingerprint.ViewModels;

/// <summary>Base class for all view models.</summary>
public abstract class ViewModelBase(IConfiguration config) : ObservableObject
{
    protected IConfiguration config { get; set; } = config;

    #region Variabel
    protected string url = config["App:Url"] ?? string.Empty;
    protected int timeOut = int.Parse(config["App:TimeoutSeconds"] ?? string.Empty);
    #endregion

    #region Function
    protected RestResponse RestResponse(string url, string endpoint, int timeOut, List<MasterHeader> headers, int unit, int method, object? body = null, IFormFile? file = null, List<MasterHeader>? param = null)
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
    #endregion
}
