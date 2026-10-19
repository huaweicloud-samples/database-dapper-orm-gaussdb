using System;
using Xunit;

namespace Dapper.Tests
{
    public class GaussDBTypeHandlerTests
    {
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
    }
}
