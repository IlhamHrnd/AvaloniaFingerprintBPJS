using AvaloniaFingerprint.Models.Master;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;

namespace AvaloniaFingerprint.Services
{
    public class PatientServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<List<Models.Patient.PatientList>> GetPatientList(string filter, int searchType)
        {
            var result = new Root<List<Models.Patient.PatientList>>();
            var header = headers(Session.Username);

            List<Parameter> param =
            [
                new Parameter
                {
                    name = "filter",
                    value = filter
                },
                new Parameter
                {
                    name = "searchType",
                    value = searchType.ToString()
                }
            ];

            var response = RestResponse(url, "Patient/GetPatientList", timeout, header, 1, 1, param: param);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<List<Models.Patient.PatientList>>>(response.Content ?? string.Empty) ?? new Root<List<Models.Patient.PatientList>>();
                else result = JsonConvert.DeserializeObject<Root<List<Models.Patient.PatientList>>>(response.Content ?? string.Empty) ?? new Root<List<Models.Patient.PatientList>>();
            }
            catch (Exception e)
            {
                return result = new Root<List<Models.Patient.PatientList>>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
