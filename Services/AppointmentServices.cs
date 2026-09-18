using AvaloniaFingerprint.Converters;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json;
using System;

namespace AvaloniaFingerprint.Services
{
    public class AppointmentServices(IConfiguration config, SessionService session) : BaseService(config, session)
    {
        public Root<string> AppointmentNoFromFinger(Filters.Appointment.AppointmentNoFromFinger filter)
        {
            var result = new Root<string>();
            var header = headers(Session.Username);

            var response = RestResponse(url, $"Appointment/AppointmentNoFromFinger?noKartu={filter.noKartu}&nik={filter.nik}&patientName={filter.patientName}&dateTime={Converter.DateFormat(DateTime.Now, DateFormat.Yearmonthdate)}", timeout, header, 1, 1);
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

        public Root<string> GenerateRegistration(string kdBooking)
        {
            var result = new Root<string>();
            var header = headers(Session.Username);

            var response = RestResponse(url, $"Appointment/GenerateRegistration?kdBooking={kdBooking}", timeout, header, 1, 2);
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
