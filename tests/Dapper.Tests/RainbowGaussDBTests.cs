#if NET8_0_OR_GREATER
using System;
using System.Threading.Tasks;
using Xunit;

namespace Dapper.Tests
{
    /// <summary>
    /// End-to-end coverage for the Dapper.Rainbow GaussDB branch: the "returning Id" / "limit 1"
    /// CRUD spelling, and both table-name resolution paths <c>DetermineTableName</c> can take there.
    /// </summary>
    /// <remarks>
    /// Each test deliberately uses its own entity type. <c>tableNameMap</c> is a process-wide
    /// static keyed by entity type, so resolving one entity against two differently named tables
    /// would let the first test's resolved name leak into the second.
    /// </remarks>
    public class RainbowGaussDBTests
    {
        private sealed class Widget
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        private sealed class Gizmo
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        private sealed class BigSerialRow
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        private sealed class HugeKeyRow
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        /// <summary>
        /// The fallback branch of <c>DetermineTableName</c> resolves to the bare type name, so this type's
        /// name is also the physical table its test creates (the server folds the unquoted name to lower
        /// case, hence the "gdbt6gadget" DDL below).
        /// </summary>
        private sealed class GdbT6Gadget
        {
            public int Id { get; set; }
            public string? Name { get; set; }
        }

        private sealed class RainbowDb : Database<RainbowDb>
        {
            public Table<Widget> Widgets { get; set; } = null!;
            public Table<Gizmo> Gizmos { get; set; } = null!;
            public Table<BigSerialRow> GdbT2Bigserials { get; set; } = null!;

            // A long key type, so the test can name a primary key beyond int32; the entity's Id property is
            // still int, which is where the documented overflow happens.
            public Table<HugeKeyRow, long> GdbT3Bigkeys { get; set; } = null!;

            public Table<GdbT6Gadget> GdbT6Gadgets { get; set; } = null!;
        }

        [GaussDBTests.FactGaussDB]
        public async Task InsertGetFirstDeleteRoundTrip()
        {
            using var conn = DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection();
            // The property is "Widgets" while the physical table is "widget", so the probe misses and
            // the name falls back to the bare type name: the GaussDB fallback branch under test, which
            // must stay unquoted (quotes would defeat the server's folding of unquoted identifiers).
            // dappertest is shared, so a leftover "widgets" table (from an interrupted run) would make
            // the probe hit and resolve the name to "Widgets" instead of exercising that fallback.
            conn.Execute("drop table if exists widgets");
            conn.Execute("drop table if exists widget");
            conn.Execute("create table widget (Id serial primary key, Name varchar2(60))");
            try
            {
                var db = RainbowDb.Init(conn, 30);
                Assert.Equal("Widget", db.Widgets.TableName);

                int? id = db.Widgets.Insert(new { Name = "alpha" });
                Assert.True(id > 0);

                var fetched = db.Widgets.Get(id!.Value);
                Assert.NotNull(fetched);
                Assert.Equal("alpha", fetched.Name);

                var first = db.Widgets.First();
                Assert.NotNull(first);
                Assert.Equal("alpha", first.Name);

                Assert.True(db.Widgets.Delete(id.Value));
                Assert.Null(db.Widgets.Get(id.Value));

                // The async branches spell their GaussDB SQL themselves ("returning Id" for the insert,
                // "limit 1" for the first). The delete above emptied the table, so the limit-1 row is
                // necessarily the one just inserted.
                int? asyncId = await db.Widgets.InsertAsync(new { Name = "gamma" });
                Assert.True(asyncId > 0);

                var asyncFirst = await db.Widgets.FirstAsync();
                Assert.NotNull(asyncFirst);
                Assert.Equal("gamma", asyncFirst.Name);

                // A POCO carrying Id: Insert drops "Id" from the column list, so the serial column
                // supplies the key rather than the property's 12345.
                int? pocoId = db.Widgets.Insert(new Widget { Id = 12345, Name = "beta" });
                Assert.True(pocoId > 0);
                Assert.NotEqual(12345, pocoId!.Value);

                var poco = db.Widgets.Get(pocoId.Value);
                Assert.NotNull(poco);
                Assert.Equal("beta", poco.Name);

                Assert.True(db.Widgets.Delete(pocoId.Value));
                Assert.Null(db.Widgets.Get(pocoId.Value));
            }
            finally
            {
                conn.Execute("drop table if exists widgets");
                conn.Execute("drop table if exists widget");
            }
        }

        [GaussDBTests.FactGaussDB]
        public void TableNameDetectionFindsExistingTable()
        {
            using var conn = DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection();
            conn.Execute("drop table if exists gizmos");
            conn.Execute("create table gizmos (Id serial primary key, Name varchar2(60))");
            try
            {
                var db = RainbowDb.Init(conn, 30);
                // The probe for "Gizmos" has to match the server's lower-case "gizmos" case-insensitively;
                // a case-sensitive probe would miss and fall back to the bare type name "Gizmo". The
                // resolved name is the unquoted "Gizmos", which the server folds back to "gizmos".
                Assert.Equal("Gizmos", db.Gizmos.TableName);
                Assert.Empty(db.Gizmos.All());
            }
            finally
            {
                conn.Execute("drop table if exists gizmos");
            }
        }

        [GaussDBTests.FactGaussDB]
        public void BigSerialPrimaryKeyRoundTrip()
        {
            // verification-results.md §十二: the driver boxes an int8 primary key as Int64 and Rainbow's
            // Query<int?> converts it (Convert.ChangeType, not an IL unbox), so a bigserial key inside the
            // int32 range round-trips through Insert/Get just like a serial one.
            using var conn = DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection();
            conn.Execute("drop table if exists gdbt2bigserials");
            conn.Execute("create table gdbt2bigserials (Id bigserial primary key, Name varchar2(60))");
            try
            {
                var db = RainbowDb.Init(conn, 30);
                Assert.Equal("GdbT2Bigserials", db.GdbT2Bigserials.TableName);

                int? id = db.GdbT2Bigserials.Insert(new { Name = "big" });
                Assert.True(id > 0);

                var fetched = db.GdbT2Bigserials.Get(id!.Value);
                Assert.NotNull(fetched);
                Assert.Equal(id.Value, fetched.Id);
                Assert.Equal("big", fetched.Name);
            }
            finally
            {
                conn.Execute("drop table if exists gdbt2bigserials");
            }
        }

        [GaussDBTests.FactGaussDB]
        public void BigSerialPrimaryKeyBeyondInt32ThrowsDataException()
        {
            // GAUSSDB.md §11 「Rainbow 的边界」: an int8 primary key is usable only while it fits in int32.
            // Past that the failure is loud - DataException wrapping OverflowException - never a silently
            // truncated key. The key is named through Table<,long>.Get so the value itself can be expressed;
            // the entity's Id is still int, which is where the conversion overflows.
            using var conn = DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection();
            conn.Execute("drop table if exists gdbt3bigkeys");
            conn.Execute("create table gdbt3bigkeys (Id bigserial primary key, Name varchar2(60))");
            try
            {
                conn.Execute("insert into gdbt3bigkeys (Id, Name) values (3000000000, 'huge')");
                var db = RainbowDb.Init(conn, 30);

                var ex = Assert.Throws<System.Data.DataException>(() => db.GdbT3Bigkeys.Get(3000000000L));
                Assert.IsType<OverflowException>(ex.InnerException);
            }
            finally
            {
                conn.Execute("drop table if exists gdbt3bigkeys");
            }
        }

        [GaussDBTests.FactGaussDB]
        public void TableNameResolvedBeforeTableExistsStaysUsable()
        {
            // Task 7 scenario C / Task 8 M3: the resolved name is cached process-wide, so a resolution that
            // happens before the table exists must still hand back a name the table can be created under.
            // The probe misses (no such table yet) and the fallback is the bare type name - unquoted, so the
            // server folds it to lower case and "GdbT6Gadget" addresses the "gdbt6gadget" table created
            // afterwards. A cached name that did not survive that order would break every later CRUD call.
            using var conn = DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection();
            conn.Execute("drop table if exists gdbt6gadget");
            try
            {
                var db = RainbowDb.Init(conn, 30);
                Assert.Equal("GdbT6Gadget", db.GdbT6Gadgets.TableName);

                conn.Execute("create table gdbt6gadget (Id serial primary key, Name varchar2(60))");

                int? id = db.GdbT6Gadgets.Insert(new { Name = "late" });
                Assert.True(id > 0);

                var fetched = db.GdbT6Gadgets.Get(id!.Value);
                Assert.NotNull(fetched);
                Assert.Equal("late", fetched.Name);
            }
            finally
            {
                conn.Execute("drop table if exists gdbt6gadget");
            }
        }
    }
}
#endif
