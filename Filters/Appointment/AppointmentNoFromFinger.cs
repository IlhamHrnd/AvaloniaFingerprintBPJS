using System;

namespace AvaloniaFingerprint.Filters.Appointment
{
    public class AppointmentNoFromFinger
    {
        public string noKartu { get; set; } = string.Empty;
        public string nik { get; set; } = string.Empty;
        public DateTime dateTime { get; set; } = DateTime.Now;
        public string patientName { get; set; } = string.Empty;
    }
}
