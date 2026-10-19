#if NET8_0_OR_GREATER
using Xunit;

namespace Dapper.Tests
{
    public class GaussDBFeatureSupportTests
    {
        [Fact]
        public void GaussDBConnectionIsRecognizedAsArrayCapable()
        {
            using var conn = new HuaweiCloud.GaussDB.GaussDBConnection(
                "Host=localhost;Port=5432;Database=dappertest;Username=appuser;Password=placeholder");
            Assert.True(FeatureSupport.Get(conn).Arrays);
        }
    }
}
#endif
