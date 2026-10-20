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

        private const string CreateTypesTable = @"
create table gaussdb_types_probe (
    id number(10) primary key,
    c_number number(12,4),
    c_varchar2 varchar2(60),
    c_nvarchar2 nvarchar2(60),
    c_date date,
    c_ts timestamp,
    c_clob clob,
    c_blob blob,
    c_raw raw(16)
)";

        [FactGaussDB]
        public void OracleTypesRoundTrip()
        {
            // Registration is process-wide and does not purge Dapper's query cache, so it has to happen
            // before the first statement whose SQL text will be cached.
            GaussDBSetup.Initialize();

            using var conn = Provider.GetOpenConnection();
            conn.Execute("drop table if exists gaussdb_types_probe");
            conn.Execute(CreateTypesTable);
            try
            {
                var id = conn.ExecuteScalar<int>(@"
insert into gaussdb_types_probe
    (id, c_number, c_varchar2, c_nvarchar2, c_date, c_ts, c_clob, c_blob, c_raw)
values
    (@id, @num, @v2, @nv2, @d, @ts, @clob, @blob, hextoraw(@raw))
returning id",
                    new
                    {
                        id = 1,
                        num = 12345.6789m,
                        v2 = "varchar2值",
                        nv2 = "nvarchar2值",
                        d = new DateTime(2026, 10, 17),          // 参数直写 date 列（服务端隐式转换，实测可行）
                        ts = new DateTime(2026, 10, 17, 12, 34, 56),
                        clob = "clob内容",
                        blob = new byte[] { 0x00, 0xFF, 0xAB },
                        raw = "0102ABCD",
                    });

                Assert.Equal(1, id);

                // The c_* column names are aliased because Dapper's DefaultTypeMap matches names
                // case-insensitively but does not strip underscores (MatchNamesWithUnderscores defaults
                // to false), so "c_number" would not land on a CNumber property.
                var row = conn.QuerySingle<TypeRow>(@"
select id,
       c_number   as cnumber,
       c_varchar2 as cvarchar2,
       c_nvarchar2 as cnvarchar2,
       c_date::timestamp as cdate,
       c_ts   as cts,
       c_clob::text as cclob,
       c_blob::text as cblob,
       c_raw::text  as craw
from gaussdb_types_probe where id = 1");

                Assert.Equal(1, row.Id);
                Assert.Equal(12345.6789m, row.CNumber);
                Assert.Equal("varchar2值", row.CVarchar2);
                Assert.Equal("nvarchar2值", row.CNvarchar2);
                Assert.Equal(new DateTime(2026, 10, 17), row.CDate);
                Assert.Equal(new DateTime(2026, 10, 17, 12, 34, 56), row.CTs);
                Assert.Equal("clob内容", row.CClob);
                Assert.Equal(new byte[] { 0x00, 0xFF, 0xAB }, row.CBlob);
                Assert.Equal(new byte[] { 0x01, 0x02, 0xAB, 0xCD }, row.CRaw);
            }
            finally
            {
                conn.Execute("drop table if exists gaussdb_types_probe");
            }
        }

        private sealed class TypeRow
        {
            public int Id { get; set; }
            public decimal CNumber { get; set; }
            public string CVarchar2 { get; set; } = "";
            public string CNvarchar2 { get; set; } = "";
            public DateTime CDate { get; set; }
            public DateTime CTs { get; set; }
            public string CClob { get; set; } = "";
            public byte[] CBlob { get; set; } = Array.Empty<byte>();
            public byte[] CRaw { get; set; } = Array.Empty<byte>();
        }

        [FactGaussDB]
        public void EmptyStringBecomesNull()
        {
            using var conn = Provider.GetOpenConnection();
            conn.Execute("drop table if exists gaussdb_empty_probe");
            conn.Execute("create table gaussdb_empty_probe (id int, v varchar2(10))");
            try
            {
                conn.Execute("insert into gaussdb_empty_probe (id, v) values (1, @v)", new { v = "" });
                var isNull = conn.ExecuteScalar<bool>("select v is null from gaussdb_empty_probe where id = 1");
                Assert.True(isNull, "GaussDB A 模式将空串转为 NULL");
            }
            finally
            {
                conn.Execute("drop table if exists gaussdb_empty_probe");
            }
        }

        [FactGaussDB]
        public void InListArrayParameterWorks()
        {
            using var conn = Provider.GetOpenConnection();
            var ids = new[] { 1, 2, 3 };
            // GaussDB is array-capable (see GaussDBFeatureSupportTests), so Dapper takes its array path:
            // the array is bound as a single parameter and the SQL text is left untouched, which makes
            // "in @ids" a server-side syntax error. The array-capable spelling is "= any(@ids)".
            var count = conn.ExecuteScalar<int>(
                "select count(*)::int from (values (1),(2),(3),(4)) t(id) where id = any(@ids)",
                new { ids });
            Assert.Equal(3, count);
        }

        [FactGaussDB]
        public void BcDateDirectTimestampRead()
        {
            // .NET's DateTime cannot represent a BC year (MinValue is 0001-01-01), so reading a BC value
            // with its era intact must fail loudly rather than be silently shifted into the AD era.
            // Measured with HuaweiCloud.GaussDB.Driver 8.0.1 on net8.0:
            //   System.InvalidCastException: Out of range of DateTime (year must be between 1 and 9999).
            // (thrown by the driver from GaussDBDataReader.GetFieldType, before Dapper maps anything).
            GaussDBSetup.Initialize();

            using var conn = Provider.GetOpenConnection();
            conn.Execute("drop table if exists gaussdb_bc_probe");
            conn.Execute("create table gaussdb_bc_probe (id int, d date)");
            try
            {
                conn.Execute("insert into gaussdb_bc_probe (id, d) values (1, to_date('0044-03-15 BC','yyyy-mm-dd BC'))");

                Assert.Throws<InvalidCastException>(
                    () => conn.QuerySingle<DateTime>("select d::timestamp from gaussdb_bc_probe where id = 1"));
            }
            finally
            {
                conn.Execute("drop table if exists gaussdb_bc_probe");
            }
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
