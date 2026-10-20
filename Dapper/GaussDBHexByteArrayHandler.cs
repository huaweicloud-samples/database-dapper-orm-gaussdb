using System;
using System.Data;
using System.Globalization;

namespace Dapper
{
    /// <summary>
    /// Maps GaussDB <c>raw</c> and <c>blob</c> columns to <see cref="byte"/> arrays.
    /// </summary>
    /// <remarks>
    /// In GaussDB A-compatibility mode, raw/blob are driver-unmapped types: reading them without a cast
    /// fails with <see cref="InvalidCastException"/> ("Reading as 'System.Object' is not supported for
    /// fields having DataTypeName 'raw'"). Query them with an explicit <c>::text</c> cast, which
    /// yields a hex string (for example <c>'0102ABCD'</c>); this handler converts that text back to bytes.
    /// Values that already arrive as a byte array (for example from a <c>bytea</c> column) pass through.
    /// </remarks>
    public sealed class GaussDBHexByteArrayHandler : SqlMapper.TypeHandler<byte[]?>
    {
        // Dapper's SqlMapper.LookupDbType resolves byte[] from its built-in type map (DbType.Binary)
        // before it consults registered type handlers, so Dapper's write path never reaches this method.
        // It exists to satisfy the TypeHandler<T> contract, and for direct callers.
        /// <inheritdoc/>
        public override void SetValue(IDbDataParameter parameter, byte[]? value)
        {
            if (parameter is null) throw new ArgumentNullException(nameof(parameter));
            parameter.Value = value is null ? DBNull.Value : (object)value;
        }

        /// <inheritdoc/>
        public override byte[]? Parse(object? value)
        {
            switch (value)
            {
                case null:
                case DBNull:
                    return null;
                case byte[] bytes:
                    return bytes;
                case string hex:
                    return ParseHex(hex);
                default:
                    throw new NotSupportedException(
                        $"GaussDBHexByteArrayHandler cannot convert {value.GetType().Name}.");
            }
        }

        // hand-rolled because Convert.FromHexString is unavailable on net461/netstandard2.0
        private static byte[] ParseHex(string hex)
        {
            hex = hex.Trim();
            if (hex.Length % 2 != 0)
            {
                throw new FormatException($"Hex string must have an even number of characters; got {hex.Length}.");
            }

            if (hex.Length == 0) return Array.Empty<byte>();

            var result = new byte[hex.Length / 2];
            for (var i = 0; i < result.Length; i++)
            {
                // AllowHexSpecifier (not HexNumber, which also allows surrounding whitespace) so that a
                // separator-laden value such as "01 02 03" is rejected instead of silently mis-decoded.
                result[i] = byte.Parse(
                    hex.Substring(i * 2, 2),
                    NumberStyles.AllowHexSpecifier,
                    CultureInfo.InvariantCulture);
            }

            return result;
        }
    }
}
