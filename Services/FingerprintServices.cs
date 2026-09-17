using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;

namespace AvaloniaFingerprint.Services
{
    public class FingerprintServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<Models.Fingerprint.Verify.VerifyRoot> Verify(Filters.Fingerprint.Verify verify)
        {
            var result = new Root<Models.Fingerprint.Verify.VerifyRoot>();
            var header = headers(Session.Username);

            var response = RestResponse(url, "Finger/Verify", timeout, header, 1, 2, verify);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.Fingerprint.Verify.VerifyRoot>>(response.Content ?? string.Empty) ?? new Root<Models.Fingerprint.Verify.VerifyRoot>();
                else result = JsonConvert.DeserializeObject<Root<Models.Fingerprint.Verify.VerifyRoot>>(response.Content ?? string.Empty) ?? new Root<Models.Fingerprint.Verify.VerifyRoot>();
            }
            catch (Exception e)
            {
                return result = new Root<Models.Fingerprint.Verify.VerifyRoot>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
