using System;
using Xunit;

namespace Dapper.Tests
{
    // These tests register handlers on the process-wide SqlMapper state and restore it in Dispose.
    // Test parallelization is disabled assembly-wide (see ParameterTests.cs), so the collection marker
    // is defensive only; the state cleanup in Dispose is what actually keeps unrelated tests safe.
    [Collection(NonParallelDefinition.Name)]
    public class GaussDBTypeHandlerTests : IDisposable
    {
        // Dapper exposes no per-handler removal, so restoring the built-in table is the only way back.
        public void Dispose() => SqlMapper.ResetTypeHandlers();

        [Fact]
        public void HexHandlerParsesHexString()
        {
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Equal(new byte[] { 0x01, 0x02, 0xAB, 0xCD }, handler.Parse("0102ABCD"));
        }

        [Fact]
        public void HexHandlerParsesLowercaseAndWhitespace()
        {
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Equal(new byte[] { 0x00, 0xFF, 0xAB }, handler.Parse("  00ffab  "));
        }

        [Fact]
        public void HexHandlerPassesThroughByteArray()
        {
            var handler = new GaussDBHexByteArrayHandler();
            var bytes = new byte[] { 1, 2, 3 };
            Assert.Same(bytes, handler.Parse(bytes));
        }

        [Fact]
        public void HexHandlerParsesNull()
        {
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Null(handler.Parse(null));
            Assert.Null(handler.Parse(DBNull.Value));
        }

        [Fact]
        public void HexHandlerRejectsOddLength()
        {
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Throws<FormatException>(() => handler.Parse("010"));
        }

        [Fact]
        public void HexHandlerRejectsEmbeddedWhitespace()
        {
            // the ::text cast never emits separators; silently decoding them would corrupt data
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Throws<FormatException>(() => handler.Parse("01 02 03"));
            Assert.Throws<FormatException>(() => handler.Parse("0 10 2"));
        }

        [Fact]
        public void HexHandlerRejectsNonHexCharacters()
        {
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Throws<FormatException>(() => handler.Parse("ZZZZ"));
        }

        [Fact]
        public void HexHandlerRejectsUnsupportedType()
        {
            var handler = new GaussDBHexByteArrayHandler();
            Assert.Throws<NotSupportedException>(() => handler.Parse(5));
        }

        [Fact]
        public void HexHandlerTreatsEmptyHexAsEmptyArray()
        {
            var handler = new GaussDBHexByteArrayHandler();
            // empty hex means empty bytes, and it must not allocate a fresh array each call
            Assert.Same(Array.Empty<byte>(), handler.Parse(string.Empty));
            Assert.Same(Array.Empty<byte>(), handler.Parse("   "));
        }

        [Fact]
        public void HexHandlerSetValueContractWritesBytesOrNull()
        {
            // This covers the handler's own SetValue contract only. Dapper's write path never calls it:
            // SqlMapper.LookupDbType resolves byte[] from the built-in type map (DbType.Binary) before it
            // consults registered type handlers, so parameter writes bypass this handler entirely.
            var handler = new GaussDBHexByteArrayHandler();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand();
            var p = cmd.CreateParameter();
            handler.SetValue(p, new byte[] { 9 });
            Assert.Equal(new byte[] { 9 }, p.Value);
            handler.SetValue(p, null);
            Assert.Equal(DBNull.Value, p.Value);
        }

        [Fact]
        public void DateHandlerParsesOracleStyleText()
        {
            var handler = new GaussDBDateHandler();
            Assert.Equal(new DateTime(2026, 10, 17), handler.Parse("17-OCT-26"));
        }

        [Fact]
        public void DateHandlerParsesTextWithAdEraSuffix()
        {
            var handler = new GaussDBDateHandler();
            Assert.Equal(new DateTime(2026, 10, 17), handler.Parse("2026-10-17 00:00:00 AD"));
        }

        [Fact]
        public void DateHandlerParsesIsoText()
        {
            var handler = new GaussDBDateHandler();
            Assert.Equal(new DateTime(2026, 10, 17, 12, 34, 56), handler.Parse("2026-10-17 12:34:56"));
        }

        [Fact]
        public void DateHandlerPassesThroughDateTime()
        {
            var handler = new GaussDBDateHandler();
            var dt = new DateTime(2026, 1, 2, 3, 4, 5);
            Assert.Equal(dt, handler.Parse(dt));
        }

        [Fact]
        public void DateHandlerRejectsBcEra()
        {
            var handler = new GaussDBDateHandler();
            Assert.Throws<NotSupportedException>(() => handler.Parse("0044-03-15 00:00:00 BC"));
        }

        [Fact]
        public void DateHandlerUsesOracleRrYearSemantics()
        {
            var handler = new GaussDBDateHandler();
            // RR: 00-49 -> 20xx, 50-99 -> 19xx
            Assert.Equal(new DateTime(2035, 10, 17), handler.Parse("17-OCT-35"));
            Assert.Equal(new DateTime(1995, 10, 17), handler.Parse("17-OCT-95"));
            Assert.Equal(new DateTime(2049, 1, 1), handler.Parse("01-JAN-49"));
            Assert.Equal(new DateTime(1950, 1, 1), handler.Parse("01-JAN-50"));
        }

        [Fact]
        public void DateHandlerParsesNullAndDbNull()
        {
            var handler = new GaussDBDateHandler();
            Assert.Equal(default, handler.Parse(null));
            Assert.Equal(default, handler.Parse(DBNull.Value));
        }

        [Fact]
        public void DateHandlerRejectsUnsupportedType()
        {
            var handler = new GaussDBDateHandler();
            Assert.Throws<NotSupportedException>(() => handler.Parse(5));
        }

        [Fact]
        public void DateHandlerSetValueContractWritesDateTime()
        {
            var handler = new GaussDBDateHandler();
            using var cmd = new Microsoft.Data.SqlClient.SqlCommand();
            var p = cmd.CreateParameter();
            var dt = new DateTime(2026, 10, 17);
            handler.SetValue(p, dt);
            Assert.Equal(dt, p.Value);
        }

        [Fact]
        public void SetupRegistersHandlers()
        {
            SqlMapper.ResetTypeHandlers();
            Assert.False(SqlMapper.HasTypeHandler(typeof(byte[])), "前置条件：重置后不应有自定义 handler");

            GaussDBSetup.Initialize();

            Assert.True(SqlMapper.HasTypeHandler(typeof(byte[])), "byte[] handler 应已注册");
            Assert.True(SqlMapper.HasTypeHandler(typeof(DateTime)), "DateTime handler 应已注册");
        }

        [Fact]
        public void SetupIsIdempotent()
        {
            GaussDBSetup.Initialize();
            GaussDBSetup.Initialize(); // 重复调用不得抛异常

            Assert.True(SqlMapper.HasTypeHandler(typeof(byte[])));
        }

        [Fact]
        public void SetupRecoversAfterResetTypeHandlers()
        {
            GaussDBSetup.Initialize();
            SqlMapper.ResetTypeHandlers();
            Assert.False(SqlMapper.HasTypeHandler(typeof(byte[])), "ResetTypeHandlers 应清空自定义 handler");

            GaussDBSetup.Initialize();
            Assert.True(SqlMapper.HasTypeHandler(typeof(byte[])), "再次 Initialize 应能恢复 byte[] 注册");
            Assert.True(SqlMapper.HasTypeHandler(typeof(DateTime)), "再次 Initialize 应能恢复 DateTime 注册");
        }

        [Fact]
        public void SetupRegistersDateHandlerEvenIfByteArrayHandlerPreExists()
        {
            SqlMapper.ResetTypeHandlers();
            SqlMapper.AddTypeHandler(new GaussDBHexByteArrayHandler()); // 模拟用户自有 handler
            GaussDBSetup.Initialize();

            Assert.True(SqlMapper.HasTypeHandler(typeof(DateTime)), "不应因 byte[] 已注册而漏注册 DateTime");
        }

#if NET6_0_OR_GREATER
        [Fact]
        public void DateHandlerAcceptsBoxedDateOnly()
        {
            // Npgsql 10 boxes DateOnly for date columns; the process-wide DateTime handler must not turn
            // a box Dapper can otherwise convert into an exception. Mirrors SqlMapper.TryConvertDateTimeFamily.
            var handler = new GaussDBDateHandler();
            Assert.Equal(new DateTime(2026, 10, 17), handler.Parse(new DateOnly(2026, 10, 17)));
        }
#endif
    }
}
