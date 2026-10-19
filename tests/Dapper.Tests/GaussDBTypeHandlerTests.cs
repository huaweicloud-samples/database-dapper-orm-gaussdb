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
        public void HexHandlerSetValueWritesBytesOrNull()
        {
            var handler = new GaussDBHexByteArrayHandler();
            using var cmd = new System.Data.SqlClient.SqlCommand();
            var p = cmd.CreateParameter();
            handler.SetValue(p, new byte[] { 9 });
            Assert.Equal(new byte[] { 9 }, p.Value);
            handler.SetValue(p, null);
            Assert.Equal(DBNull.Value, p.Value);
        }
    }
}
