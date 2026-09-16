using AvaloniaFingerprint.Models.Master;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.ViewModels
{
    public class AppUserViewModels(IConfiguration config) : ViewModelBase(config)
    {
        public MasterResponse<string> GetUserAuth(string userId, string password)
        {
            var result = new MasterResponse<string>();
            if (string.IsNullOrEmpty(userId)) return result = new MasterResponse<string>
            {
                message = "UserId dilarang kosong"
            };
            else if (string.IsNullOrEmpty(password)) return new MasterResponse<string>
            {
                message = "Password dilarang kosong"
            };

            var header = new List<MasterHeader>
            {
                new()
                {
                    name = "accept",
                    value = "application/json"
                },
                new()
                {
                    name = "x-pass",
                    value = password
                }
            };

            var response = RestResponse(url, "AppUser/UserAuthentication/ilham", timeOut, header, 2, 1);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<MasterResponse<string>>(response.Content ?? string.Empty) ?? new MasterResponse<string>();
                else result = JsonConvert.DeserializeObject<MasterResponse<string>>(response.Content ?? string.Empty) ?? new MasterResponse<string>();
            }
            catch (Exception e)
            {
                result = new MasterResponse<string>
                {
                    errors = [e.Message, e.InnerException?.InnerException?.Message ?? string.Empty],
                    message = e.Message
                };
            }

            return result;
        }
    }
}
