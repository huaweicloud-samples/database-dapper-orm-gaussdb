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
    /// <para>
    /// If <see cref="SqlMapper.ResetTypeHandlers()"/> has been called, calling <see cref="Initialize"/>
    /// again re-registers the handlers.
    /// </para>
    /// </remarks>
    public static class GaussDBSetup
    {
        /// <summary>
        /// Registers the GaussDB type handlers. Safe to call multiple times and from multiple threads.
        /// </summary>
        public static void Initialize()
        {
            // Checks actual registration state rather than a latched flag: SqlMapper.ResetTypeHandlers()
            // rebuilds the handler table and would otherwise leave the handlers permanently unregistered,
            // because a latched flag cannot be re-entered.
            if (SqlMapper.HasTypeHandler(typeof(byte[])))
            {
                return;
            }

            SqlMapper.AddTypeHandler(new GaussDBHexByteArrayHandler());
            SqlMapper.AddTypeHandler(new GaussDBDateHandler());
        }
    }
}
