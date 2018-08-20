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
                    s.SetTag("ConstraintName", postgres.ConstraintName);
                    s.SetTag("TableName", postgres.TableName);
                    s.SetTag("ColumnName", postgres.ColumnName);
                    s.SetTag("ColumnName", postgres.DataTypeName);
                    s.SetTag("Hint", postgres.Hint);
                    s.SetTag("Routine", postgres.Routine);
                    s.SetTag("SchemaName", postgres.SchemaName);
                    s.SetTag("SqlState", postgres.SqlState);

                    s.SetExtra("InternalQuery", postgres.InternalQuery);
                    s.SetExtra("Detail", postgres.Detail);
                });
            }
        }
    }
}
