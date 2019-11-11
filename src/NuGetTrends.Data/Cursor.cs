using System;

namespace NuGetTrends.Data
{
    public class Cursor
    {
        public string? Id { get; set; }
        // https://github.com/npgsql/Npgsql.EntityFrameworkCore.PostgreSQL/issues/303
        public DateTimeOffset Value { get; set; }
    }
}
