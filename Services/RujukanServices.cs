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

        /// <summary>
        /// Membuat SEP dari rujukan baru lewat <c>POST /api/Rujukan/CreateSepFromRujukanBaru</c>.
        /// Seluruh parameter dikirim sebagai JSON body (tidak ada query string). Nilai
        /// <c>data</c> berisi nomor SEP, sementara <c>succeeded</c>, <c>errors</c>, dan
        /// <c>message</c> tetap dibawa apa adanya supaya pemanggil bisa membedakan HTTP 200
        /// yang benar-benar berhasil dari HTTP 200 yang gagal diproses API Service.
        /// </summary>
        public Root<string> CreateSepFromRujukanBaru(Filters.Rujukan.CreateSepFromRujukanBaruRequest request)
        {
            var result = new Root<string>();
            var header = headers(Session.Username);

            var response = RestResponse(url, "Rujukan/CreateSepFromRujukanBaru", timeout, header, 1, 2, request);
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
