using AvaloniaFingerprint.Converters;
using AvaloniaFingerprint.Models.Master;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Services
{
    public class ParamedicServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>> ParamedicScheduleBpjsQuota(DateTime startDate, DateTime endDate, string parId, string suId)
        {
            var result = new Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>>();
            var header = headers(Session.Username);

            List<Parameter> param =
            [
                new Parameter
                {
                    name = "startDate",
                    value = Converter.DateFormat(startDate, DateFormat.Yearmonthdate)
                },
                new Parameter
                {
                    name = "endDate",
                    value = Converter.DateFormat(endDate, DateFormat.Yearmonthdate)
                },
                new Parameter
                {
                    name = "parId",
                    value = parId
                },
                new Parameter
                {
                    name = "suId",
                    value = suId
                }
            ];

            var response = RestResponse(url, "Paramedic/ParamedicScheduleBpjsQuota", timeout, header, 1, 1, param: param);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>>>(response.Content ?? string.Empty) ?? new Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>>();
                else result = JsonConvert.DeserializeObject<Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>>>(response.Content ?? string.Empty) ?? new Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>>();
            }
            catch (Exception e)
            {
                return result = new Root<List<Models.Paramedic.ParamedicScheduleBpjsQuota>>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
