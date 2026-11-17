using System;

namespace AvaloniaFingerprint.Models.Patient
{
    public class PatientList
    {
        public string patientId { get; set; } = string.Empty;

        public string medicalNo { get; set; } = string.Empty;

        public string ssn { get; set; } = string.Empty;

        public string srSalutation { get; set; } = string.Empty;

        public string firstName { get; set; } = string.Empty;

        public string middleName { get; set; } = string.Empty;

        public string lastName { get; set; } = string.Empty;

        public string cityOfBirth { get; set; } = string.Empty;

        public DateTime? dateOfBirth { get; set; }

        public string sex { get; set; } = string.Empty;

        public string fullName { get; set; } = string.Empty;
    }
}
