using Microsoft.EntityFrameworkCore;
using Npgsql;
using Sentry;
using Sentry.Extensibility;

namespace NuGetTrends.Scheduler
{
    public class DbUpdateExceptionProcessor : SentryEventExceptionProcessor<DbUpdateException>
    {
        private readonly IHub _hub;

        public DbUpdateExceptionProcessor(IHub hub) => _hub = hub;

        protected override void ProcessException(
            DbUpdateException exception,
            SentryEvent sentryEvent)
        {
            if (exception.InnerException is PostgresException postgres)
            {
                _hub.ConfigureScope(s =>
                {
                    if (postgres.ConstraintName is { } constraintName)
                    {
                        s.SetTag(nameof(postgres.ConstraintName), constraintName);
                    }
                    if (postgres.TableName is { } tableName)
                    {
                        s.SetTag(nameof(postgres.TableName), tableName);
                    }
                    if (postgres.ColumnName is { } columnName)
                    {
                        s.SetTag(nameof(postgres.ColumnName), columnName);
                    }
                    if (postgres.DataTypeName is { } dataTypeName)
                    {
                        s.SetTag(nameof(postgres.DataTypeName), dataTypeName);
                    }
                    if (postgres.Hint is { } hint)
                    {
                        s.SetTag(nameof(postgres.Hint), hint);
                    }
                    if (postgres.Routine is { } routine)
                    {
                        s.SetTag(nameof(postgres.Routine), routine);
                    }
                    if (postgres.SchemaName is { } schemaName)
                    {
                        s.SetTag(nameof(postgres.SchemaName), schemaName);
                    }
                    if (postgres.SqlState is { } sqlState)
                    {
                        s.SetTag(nameof(postgres.SqlState), sqlState);
                    }

                    if (postgres.InternalQuery is { } internalQuery)
                    {
                        s.SetTag(nameof(postgres.InternalQuery), internalQuery);
                    }
                    if (postgres.Detail is { } detail)
                    {
                            // Detail redacted as it may contain sensitive data. Specify 'Include Error Detail' in the connection string to include this information.
                        if (detail.StartsWith("Detail redacted"))
                        {
                            s.SetTag(nameof(postgres.Detail), "redacted");
                        }
                        else
                        {
                            s.SetTag(nameof(postgres.Detail), detail);
                        }
                    }
                });
            }
        }
    }
}
