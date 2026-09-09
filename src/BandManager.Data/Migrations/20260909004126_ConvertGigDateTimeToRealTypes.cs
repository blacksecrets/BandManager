using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BandManager.Data.Migrations
{
    /// <inheritdoc />
    public partial class ConvertGigDateTimeToRealTypes : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // Hand-written, not the naive AlterColumn EF scaffolded (which
            // would fail at runtime - Postgres has no implicit text->date/
            // time cast). Best-effort conversion of existing free-text
            // values, tested against real production data (7 Gigs rows,
            // all "Weekday, Month D, YYYY" format for Date, all empty for
            // the three time columns) before being written here - anything
            // that doesn't match the expected shape falls back to a safe
            // default (today's date / NULL) rather than aborting the
            // migration, per the "give best effort, don't worry about
            // unparseable results" instruction this was built under.
            migrationBuilder.Sql("""
                ALTER TABLE "Gigs" ALTER COLUMN "Date" TYPE date USING (
                    CASE
                        WHEN "Date" ~ '^[A-Za-z]+, [A-Za-z]+ [0-9]{1,2}, [0-9]{4}$'
                        THEN to_date(substring("Date" from position(', ' in "Date") + 2), 'FMMonth FMDD, YYYY')
                        ELSE CURRENT_DATE
                    END
                );
                """);

            const string timeColumnTemplate = """
                ALTER TABLE "Gigs" ALTER COLUMN "{0}" TYPE time USING (
                    CASE WHEN "{0}" IS NULL OR trim("{0}") = '' THEN NULL
                         WHEN "{0}" ~ '^[0-9]{{1,2}}:[0-9]{{2}}\s*(AM|PM|am|pm)?$' THEN "{0}"::time
                         ELSE NULL
                    END
                );
                """;
            foreach (var column in new[] { "DoorsTime", "OpenerTime", "HeadlinerTime" })
                migrationBuilder.Sql(string.Format(timeColumnTemplate, column));
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // Note: the weekday name is recomputed from the actual date
            // here (to_char), not preserved from whatever text was
            // originally stored - if a historical value's weekday name
            // didn't actually match its date, this "corrects" it. Harmless
            // either way since GigDateTimeFormatting.FormatDate does the
            // exact same recompute going forward.
            const string timeColumnTemplate = """
                ALTER TABLE "Gigs" ALTER COLUMN "{0}" TYPE text USING to_char("{0}", 'FMHH12:MI AM');
                """;
            foreach (var column in new[] { "DoorsTime", "OpenerTime", "HeadlinerTime" })
                migrationBuilder.Sql(string.Format(timeColumnTemplate, column));

            migrationBuilder.Sql("""
                ALTER TABLE "Gigs" ALTER COLUMN "Date" TYPE text USING to_char("Date", 'FMDay, FMMonth FMDD, YYYY');
                """);
        }
    }
}
