#if NET8_0_OR_GREATER
using Xunit;

namespace Dapper.Tests
{
    /// <summary>
    /// End-to-end coverage for the Dapper.Rainbow GaussDB branch: the "returning Id" / "limit 1"
    /// CRUD spelling, and both table-name resolution paths <c>DetermineTableName</c> can take there.
    /// </summary>
    /// <remarks>
    /// The two tests deliberately use different entity types. <c>tableNameMap</c> is a process-wide
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

        private sealed class RainbowDb : Database<RainbowDb>
        {
            public Table<Widget> Widgets { get; set; } = null!;
            public Table<Gizmo> Gizmos { get; set; } = null!;
        }

        [GaussDBTests.FactGaussDB]
        public void InsertGetFirstDeleteRoundTrip()
        {
            using var conn = DatabaseProvider<GaussDBProvider>.Instance.GetOpenConnection();
            // The property is "Widgets" while the physical table is "widget", so the probe misses and
            // the name falls back to the bare type name: the GaussDB fallback branch under test, which
            // must stay unquoted (quotes would defeat the server's folding of unquoted identifiers).
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
    }
}
#endif
