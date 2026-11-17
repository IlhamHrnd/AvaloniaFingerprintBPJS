using AvaloniaFingerprint.Models.Master;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Services
{
    public class ServiceUnitServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<List<Models.ServiceUnit.ServiceUnitBridging>> ServiceUnitBridgings(string suId)
        {
            var result = new Root<List<Models.ServiceUnit.ServiceUnitBridging>>();
            var header = headers(Session.Username);

            List<Parameter> param =
            [
                new Parameter
                {
                    name = "suId",
                    value = suId
                }
            ];

            var response = RestResponse(url, "ServiceUnit/ServiceUnitBridgings", timeout, header, 1, 1, param: param);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<List<Models.ServiceUnit.ServiceUnitBridging>>>(response.Content ?? string.Empty) ?? new Root<List<Models.ServiceUnit.ServiceUnitBridging>>();
                else result = JsonConvert.DeserializeObject<Root<List<Models.ServiceUnit.ServiceUnitBridging>>>(response.Content ?? string.Empty) ?? new Root<List<Models.ServiceUnit.ServiceUnitBridging>>();
            }
            catch (Exception e)
            {
                return result = new Root<List<Models.ServiceUnit.ServiceUnitBridging>>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
