using AvaloniaFingerprint.Models.Master;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Services
{
    public class AppUserServices(IConfiguration config) : BaseService(config)
    {
        public Root<string> GetUserAuth(string userId, string password)
        {
            var result = new Root<string>();
            List<Parameter> header =
            [
                new Parameter
                {
                    name = "accept",
                    value = "application/json"
                },
                new Parameter
                {
                    name = "Content-Type",
                    value = "application/json"
                },
                new Parameter
                {
                    name = "x-pass",
                    value = password
                }
            ];

            var response = RestResponse(url, $"AppUser/UserAuthentication/{userId}", timeout, header, 1, 1);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<string>>(response.Content ?? string.Empty) ?? new Root<string>();
                else result = JsonConvert.DeserializeObject<Root<string>>(response.Content ?? string.Empty) ?? new Root<string>();
            }
            catch (Exception e)
            {
                return result = new Root<string>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
