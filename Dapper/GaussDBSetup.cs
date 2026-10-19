using System.Threading;

namespace Dapper
{
    /// <summary>
    /// Registers the GaussDB type handlers with Dapper.
    /// </summary>
    /// <remarks>
    /// Call <see cref="Initialize"/> once at application startup when targeting GaussDB in
    /// A-compatibility mode. Registration is process-wide and idempotent.
    /// <para>
    /// Registration must happen before any query is executed: <see cref="SqlMapper.AddTypeHandler{T}"/>
    /// does not purge Dapper's query cache, so queries whose SQL text was already cached keep using the
    /// previously resolved deserializer.
    /// </para>
    /// </remarks>
    public static class GaussDBSetup
    {
        private static int initialized;

        /// <summary>
        /// Registers the GaussDB type handlers. Safe to call multiple times and from multiple threads.
        /// </summary>
        public static void Initialize()
        {
            if (Interlocked.Exchange(ref initialized, 1) == 1)
            {
                return;
            }

            SqlMapper.AddTypeHandler(new GaussDBHexByteArrayHandler());
            SqlMapper.AddTypeHandler(new GaussDBDateHandler());
        }
    }
}
