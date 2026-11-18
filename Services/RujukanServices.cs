using AvaloniaFingerprint.Models.Master;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Services
{
    public class RujukanServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<Models.Rujukan.FirstRujukanFromPatientId.Root> FirstRujukanFromPatientId(string patId)
        {
            var result = new Root<Models.Rujukan.FirstRujukanFromPatientId.Root>();
            var header = headers(Session.Username);
            List<Parameter> param =
            [
                new Parameter
                {
                    name = "patId",
                    value = patId
                }
            ];

            var response = RestResponse(url, "Rujukan/FirstRujukanFromPatientId", timeout, header, 1, 1, param: param);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.Rujukan.FirstRujukanFromPatientId.Root>>(response.Content ?? string.Empty) ?? new Root<Models.Rujukan.FirstRujukanFromPatientId.Root>();
                else result = JsonConvert.DeserializeObject<Root<Models.Rujukan.FirstRujukanFromPatientId.Root>>(response.Content ?? string.Empty) ?? new Root<Models.Rujukan.FirstRujukanFromPatientId.Root>();
            }
            catch (Exception e)
            {
                return result = new Root<Models.Rujukan.FirstRujukanFromPatientId.Root>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
