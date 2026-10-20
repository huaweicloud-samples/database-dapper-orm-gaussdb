#if NET8_0_OR_GREATER
using System;
using System.Data.Common;
using Xunit;

namespace Dapper.Tests
{
    /// <summary>
    /// GaussDB 507 集中式（Oracle 兼容 A 模式）测试提供者。连接串优先取环境变量
    /// <c>GaussDBConnectionString</c>，否则由 <c>GAUSSDB_PASSWORD</c> 拼装本地默认串。
    /// 密码刻意不硬编码：未提供 <c>GAUSSDB_PASSWORD</c> 时密码为空，连接探针失败，
    /// <see cref="FactGaussDBAttribute"/> 会把测试标记为跳过而不是误连到其它实例。
    /// </summary>
    public class GaussDBProvider : DatabaseProvider
    {
        public override DbProviderFactory Factory => HuaweiCloud.GaussDB.GaussDBFactory.Instance;

        public override string GetConnectionString()
        {
            var fromEnv = Environment.GetEnvironmentVariable("GaussDBConnectionString");
            if (!string.IsNullOrEmpty(fromEnv))
            {
                return fromEnv;
            }

            var password = Environment.GetEnvironmentVariable("GAUSSDB_PASSWORD") ?? string.Empty;
            return $"Host=localhost;Port=5432;Database=dappertest;Username=appuser;Password={password}";
        }
    }

    public class GaussDBTests : TestBase<GaussDBProvider>
    {
        [FactGaussDB]
        public void ConnectivityAndVersion()
        {
            using var conn = Provider.GetOpenConnection();
            var version = conn.ExecuteScalar<string>("select version()");
            Assert.NotNull(version);
            Assert.Contains("GaussDB", version);
        }

        [AttributeUsage(AttributeTargets.Method, AllowMultiple = false)]
        public class FactGaussDBAttribute : FactAttribute
        {
            public override string? Skip
            {
                get { return unavailable ?? base.Skip; }
                set { base.Skip = value; }
            }

            private static readonly string? unavailable;

            static FactGaussDBAttribute()
            {
                try
                {
                    using (DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection()) { /* just trying to see if it works */ }
                }
                catch (Exception ex)
                {
                    unavailable = $"GaussDB is unavailable: {ex.Message}";
                }
            }
        }
    }
}
#endif
