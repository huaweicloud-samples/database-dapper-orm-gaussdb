using System;
using System.Data;
using System.Globalization;

namespace Dapper
{
    /// <summary>
    /// Maps GaussDB A-compatibility <c>date</c> column text to <see cref="DateTime"/>.
    /// </summary>
    /// <remarks>
    /// In GaussDB A-compatibility mode, <c>date</c> is the driver-unmapped type <c>datea</c>: reading it
    /// without a cast throws <see cref="NotSupportedException"/>. Query it with <c>::timestamp</c>
    /// (which Dapper maps natively, no handler required) or <c>::text</c>, which yields either
    /// <c>'17-OCT-26'</c> or <c>'2026-10-17 00:00:00 AD'</c>. This handler parses both spellings.
    /// </remarks>
    public sealed class GaussDBDateHandler : SqlMapper.TypeHandler<DateTime>
    {
        private static readonly string[] TextFormats =
        {
            "yyyy-MM-dd HH:mm:ss",
            "yyyy-MM-dd",
            "dd-MMM-yy",
            "dd-MMM-yyyy",
            "dd-MMM-yy HH:mm:ss",
            "dd-MMM-yyyy HH:mm:ss",
        };

        private static readonly CultureInfo OracleDateCulture = CreateOracleDateCulture();

        // Oracle RR semantics: 00-49 -> 20xx, 50-99 -> 19xx.
        // The window is pinned explicitly rather than relying on the culture's default TwoDigitYearMax,
        // which is globalization data (NLS on .NET Framework, ICU on .NET Core) and not a documented
        // contract; a 2029 window would silently map 30-49 to 19xx.
        private static CultureInfo CreateOracleDateCulture()
        {
            var culture = (CultureInfo)CultureInfo.InvariantCulture.Clone();
            var calendar = new GregorianCalendar();
            calendar.TwoDigitYearMax = 2049;
            culture.DateTimeFormat.Calendar = calendar;
            return culture;
        }

        /// <inheritdoc/>
        public override void SetValue(IDbDataParameter parameter, DateTime value)
        {
            if (parameter is null) throw new ArgumentNullException(nameof(parameter));
            parameter.Value = value;
        }

        /// <inheritdoc/>
        public override DateTime Parse(object? value)
        {
            switch (value)
            {
                case null:
                case DBNull:
                    return default;
                case DateTime dt:
                    return dt;
                case string text:
                    return ParseText(text);
                default:
                    throw new NotSupportedException(
                        $"GaussDBDateHandler cannot convert {value.GetType().Name}.");
            }
        }

        private static DateTime ParseText(string text)
        {
            text = text.Trim();
            if (text.EndsWith(" BC", StringComparison.OrdinalIgnoreCase))
            {
                throw new NotSupportedException($"BC-era dates are not supported: '{text}'.");
            }

            if (text.EndsWith(" AD", StringComparison.OrdinalIgnoreCase))
            {
                text = text.Substring(0, text.Length - 3).Trim();
            }

            if (DateTime.TryParseExact(
                    text,
                    TextFormats,
                    OracleDateCulture,
                    DateTimeStyles.None,
                    out var exact))
            {
                return exact;
            }

            return DateTime.Parse(text, OracleDateCulture);
        }
    }
}
