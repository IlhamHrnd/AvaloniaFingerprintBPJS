using Microsoft.AspNetCore.Http;
using Newtonsoft.Json.Linq;
using RestSharp;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using Avalonia.Data.Converters;
using Avalonia.Media;
using AvaloniaFingerprint.Services;

namespace AvaloniaFingerprint.Converters
{
    public class Converter
    {
        public static string GetMonthInRomawi(string month)
        {
            return month switch
            {
                "01" => "I",
                "02" => "II",
                "03" => "III",
                "04" => "IV",
                "05" => "V",
                "06" => "VI",
                "07" => "VII",
                "08" => "VIII",
                "09" => "IX",
                "10" => "X",
                "11" => "XI",
                "12" => "XII",
                _ => string.Empty,
            };
        }

        public static int StringToInt(string value)
        {
            try
            {
                if (int.TryParse(value, out int result))
                    return result;
                else
                    return 0;
            }
            catch
            {
                return 0;
            }
        }

        public static bool StringToBool(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            if (value.Equals("true", StringComparison.CurrentCultureIgnoreCase) || value.Equals("yes", StringComparison.CurrentCultureIgnoreCase) || value == "1")
                return true;

            return false;
        }

        public static decimal StringToDecimal(string input)
        {
            if (decimal.TryParse(input, NumberStyles.Any, CultureInfo.InvariantCulture, out decimal result))
                return result;

            return 0;
        }

        public static DateTime StringToDateTime(string value)
        {
            try
            {
                return DateTime.Parse(value);
            }
            catch
            {
                return new DateTime();
            }
        }

        public static TimeSpan StringToTimeSpan(string value)
        {
            try
            {
                return TimeSpan.Parse(value);
            }
            catch
            {
                return new TimeSpan();
            }
        }

        public static TimeSpan IntToTimeSpan(int value, string unit = "seconds")
        {
            if (value <= 0) return TimeSpan.Zero;

            return unit.ToLower() switch
            {
                "minutes" => TimeSpan.FromMinutes(value),
                "hours" => TimeSpan.FromHours(value),
                "days" => TimeSpan.FromDays(value),
                _ => TimeSpan.FromSeconds(value)
            };
        }

        public static string IntToString(int value)
        {
            return value.ToString();
        }

        public static long IntToLong(int data)
        {
            long result = data * 1024 * 1024;
            return result;
        }

        public static double DecimalToDouble(decimal value)
        {
            double result = 0;
            try
            {
                result = Convert.ToDouble(value);
            }
            catch
            {
                result = 0.0;
            }

            return result;
        }

        public static int DecimalToInt(decimal value)
        {
            try
            {
                return (int)Math.Round(value, MidpointRounding.AwayFromZero);
            }
            catch
            {
                return 1;
            }
        }

        public static double IntToDouble(int value)
        {
            double result = 0;
            try
            {
                result = Convert.ToDouble(value);
            }
            catch
            {
                result = 0.0;
            }

            return result;
        }

        public static byte SbyteToByte(sbyte? value, byte min = 1, byte max = byte.MaxValue)
        {
            try
            {
                if (value == null)
                    return 1;
                else if (value < min)
                    return min;
                else if (value > max)
                    return max;
                else
                    return (byte)value;
            }
            catch
            {
                return 1;
            }
        }

        public static string LongToString(long value)
        {
            try
            {
                return value.ToString();
            }
            catch
            {
                return string.Empty;
            }
        }

        public static int ShortToInt(short? value, int defaultValue = 0)
        {
            return value ?? defaultValue;
        }

        public static string DateTimeToString(DateTime value)
        {
            return value.ToString();
        }

        public static string DateFormat(DateTime? dateTime, string format)
        {
            DateTime _dateTime = dateTime ?? DateTime.Now;
            string _format = !string.IsNullOrEmpty(format) ? _dateTime.ToString(format) : _dateTime.ToString();
            return _format;
        }

        public static string TimeFormat(TimeOnly? time, string format = "HH:mm:ss")
        {
            TimeOnly _time = time ?? TimeOnly.MinValue;
            string _format = !string.IsNullOrEmpty(format) ? _time.ToString(format) : _time.ToString();
            return _format;
        }

        public static TimeOnly StringToTimeOnly(string timeString)
        {
            if (string.IsNullOrWhiteSpace(timeString)) return TimeOnly.MinValue;
            if (TimeOnly.TryParse(timeString, CultureInfo.InvariantCulture, out TimeOnly result)) return result;
            return TimeOnly.MinValue;
        }

        public static string Currency(decimal amount, string culture)
        {
            CultureInfo info = new CultureInfo(culture);
            return amount.ToString("C", info);
        }

        public static byte IntToByte(int value)
        {
            if (value < byte.MinValue || value > byte.MaxValue)
                throw new ArgumentOutOfRangeException(nameof(value), "Value must be between 0 and 255");

            return (byte)value;
        }

        public static int GetAgeInYear(DateTime dateTime)
        {
            return GetAge(dateTime, DateTime.Today, 0);
        }

        public static int GetAgeInMonth(DateTime dateTime)
        {
            return GetAge(dateTime, DateTime.Today, 1);
        }

        public static int GetAgeInDay(DateTime dateTime)
        {
            return GetAge(dateTime, DateTime.Today, 2);
        }

        public static string GetFullName(string firstName, string middleName, string lastName)
        {
            middleName = string.IsNullOrWhiteSpace(middleName) ? "" : middleName;
            lastName = string.IsNullOrWhiteSpace(lastName) ? "" : lastName;

            return $"{firstName} {middleName} {lastName}".Trim();
        }

        public static bool IsValidJson(string json)
        {
            if (string.IsNullOrWhiteSpace(json))
                return false;

            try
            {
                var obj = JToken.Parse(json);
                return true;
            }
            catch
            {
                return false;
            }
        }

        public static string EscapeQuery(string value)
        {
            return value.Replace("'", "''");
        }

        public static string GetNumber(string value)
        {
            return new string([.. value.Where(char.IsDigit)]);
        }

        public static bool IsNumeric(string value)
        {
            if (string.IsNullOrEmpty(value))
                return false;

            var array = value.Trim().ToCharArray();
            return array.All(char.IsNumber);
        }

        public static bool IsValidBase64(string value)
        {
            if (string.IsNullOrEmpty(value)) return false;

            string base64 = value.Replace('-', '+').Replace('_', '/');

            int remainder = base64.Length % 4;
            if (remainder != 0) base64 = base64.PadRight(base64.Length + (4 - remainder), '=');

            return Convert.TryFromBase64String(base64, new byte[base64.Length], out _);
        }

        public static long TimeStamp(DateTime dateTime)
        {
            var ts = dateTime.ToString();
            var dto = new DateTimeOffset(DateTime.Parse(ts));
            var time = dto.ToUnixTimeMilliseconds();
            return time;
        }

        #region Private
        private static int GetAge(DateTime fromDate, DateTime toDate, int ageType)
        {
            var birthDayThisYear = new DateTime();
            int months, days;

            if (!DateTime.TryParse(fromDate.Month + "/" + fromDate.Day + "/" + toDate.Year, out birthDayThisYear))
            {
                birthDayThisYear = new DateTime(toDate.Year, fromDate.AddMonths(1).Month, 1);
                months = toDate.Month - fromDate.AddMonths(1).Month;
                days = toDate.Day - 1;
            }
            else
            {
                months = toDate.Month - fromDate.Month;
                days = toDate.Day - fromDate.Day;
            }

            var years = toDate.Year - fromDate.Year;
            if (birthDayThisYear > toDate)
            {
                years -= 1;
                months += 12;
            }

            if (birthDayThisYear.Day > toDate.Day)
            {
                months -= 1;
                var day = birthDayThisYear.Day;
                DateTime dt;

                if (DateTime.IsLeapYear(birthDayThisYear.Year))
                {
                    if (toDate.Month - 1 == 2 && birthDayThisYear.Day > 29)
                        day = 29;
                }
                else
                {
                    if (toDate.Month - 1 == 2 && birthDayThisYear.Day > 28)
                        day = 28;
                }

                try
                {
                    dt = new DateTime(toDate.Month - 1 <= 0 ? birthDayThisYear.Year - 1 : birthDayThisYear.Year, toDate.Month - 1 <= 0 ? 12 : toDate.Month - 1, day);
                }
                catch
                {
                    dt = new DateTime(toDate.Month - 1 <= 0 ? birthDayThisYear.Year - 1 : birthDayThisYear.Year, toDate.Month - 1 <= 0 ? 1 : toDate.Month - 1, day - 1);
                }

                var ts = toDate - dt;
                days = ts.Days;
            }

            return ageType switch
            {
                0 => years,
                1 => months,
                _ => days,
            };
        }
        #endregion
    }

    /// <summary>Turns a base64 JPEG string into a bitmap for <c>Image.Source</c>.</summary>
    public sealed class Base64ImageConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => Base64ToBitmap(value as string);

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();

        private Avalonia.Media.Imaging.Bitmap? Base64ToBitmap(string? base64)
        {
            if (string.IsNullOrWhiteSpace(base64))
                return null;

            try
            {
                using var stream = new MemoryStream(System.Convert.FromBase64String(base64));
                return new Avalonia.Media.Imaging.Bitmap(stream);
            }
            catch (Exception)
            {
                return null;
            }
        }
    }

    /// <summary>Green when true, red when false — used for the device / login indicators.</summary>
    public sealed class BoolToBrushConverter : IValueConverter
    {
        public IBrush TrueBrush { get; set; } = new SolidColorBrush(Color.Parse("#107C10"));
        public IBrush FalseBrush { get; set; } = new SolidColorBrush(Color.Parse("#C42B1C"));

        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is true ? TrueBrush : FalseBrush;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => throw new NotSupportedException();
    }

    public sealed class InverseBoolConverter : IValueConverter
    {
        public object? Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is not true;

        public object? ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
            => value is not true;
    }
}
