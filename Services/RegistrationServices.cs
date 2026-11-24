using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;

namespace AvaloniaFingerprint.Services
{
    public class RegistrationServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        /// <summary>
        /// Mendaftarkan pasien rujukan baru lewat
        /// <c>POST /api/Registration/CreateRegistrationFromNewRujukan</c>. Seluruh parameter
        /// dikirim sebagai JSON body (tidak ada query string). Nilai <c>data</c> berisi nomor
        /// registrasi, sementara <c>succeeded</c>, <c>errors</c>, dan <c>message</c> tetap
        /// dibawa apa adanya supaya pemanggil bisa membedakan HTTP 200 yang benar-benar
        /// berhasil dari HTTP 200 yang gagal diproses API Service.
        /// </summary>
        public Root<string> CreateRegistrationFromNewRujukan(Filters.Registration.CreateRegistrationFromNewRujukanRequest request)
        {
            var result = new Root<string>();
            var header = headers(Session.Username);

            var response = RestResponse(url, "Registration/CreateRegistrationFromNewRujukan", timeout, header, 1, 2, request);
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
