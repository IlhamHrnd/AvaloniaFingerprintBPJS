using AvaloniaFingerprint.Converters;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;

namespace AvaloniaFingerprint.Services
{
    public class PrintoutServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<Models.Printout.Sep> Sep(string kdBooking, bool isFromAppointment, DateTime dateTime)
        {
            var result = new Root<Models.Printout.Sep>();
            var header = headers(Session.Username);

            var response = RestResponse(url, $"PrintOut/Sep?kdBooking={kdBooking}&isFromAppointment={isFromAppointment.ToString().ToLower()}&dateTime={Converter.DateFormat(dateTime, DateFormat.Yearmonthdate)}", timeout, header, 1, 1);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.Printout.Sep>>(response.Content ?? string.Empty) ?? new Root<Models.Printout.Sep>();
                else result = JsonConvert.DeserializeObject<Root<Models.Printout.Sep>>(response.Content ?? string.Empty) ?? new Root<Models.Printout.Sep>();
            }
            catch (Exception e)
            {
                return result = new Root<Models.Printout.Sep>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }

        public Root<Models.Printout.Label> Label(string kdBooking, DateTime dateTime)
        {
            var result = new Root<Models.Printout.Label>();
            var header = headers(Session.Username);

            var response = RestResponse(url, $"PrintOut/Label?kdBooking={kdBooking}&dateTime={Converter.DateFormat(dateTime, DateFormat.Yearmonthdate)}&type=2", timeout, header, 1, 1);
            try
            {
                if (response.IsSuccessStatusCode) result = JsonConvert.DeserializeObject<Root<Models.Printout.Label>>(response.Content ?? string.Empty) ?? new Root<Models.Printout.Label>();
                else result = JsonConvert.DeserializeObject<Root<Models.Printout.Label>>(response.Content ?? string.Empty) ?? new Root<Models.Printout.Label>();
            }
            catch (Exception e)
            {
                return result = new Root<Models.Printout.Label>
                {
                    errors = [e.Message, e.InnerException?.Message ?? string.Empty]
                };
            }
            return result;
        }
    }
}
