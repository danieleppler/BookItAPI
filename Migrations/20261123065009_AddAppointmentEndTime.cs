using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace BookIt.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddAppointmentEndTime : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AlterDatabase()
                .Annotation("Npgsql:PostgresExtension:btree_gist", ",,");

            // Added as nullable (no default), backfilled, then made NOT NULL.
            // A default like -infinity would leave pre-UPDATE row versions in this transaction with
            // EndTime < StartTime, and building the exclusion index below fails on them
            // ("range lower bound must be less than or equal to range upper bound").
            migrationBuilder.AddColumn<DateTime>(
                name: "EndTime",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: true);

            // Backfill existing rows from their service's duration
            migrationBuilder.Sql("""
                UPDATE "Appointments" AS a
                SET "EndTime" = a."StartTime" + make_interval(mins => s."DurationMinutes")
                FROM "Services" AS s
                WHERE s."Id" = a."ServiceId";
                """);

            migrationBuilder.AlterColumn<DateTime>(
                name: "EndTime",
                table: "Appointments",
                type: "timestamp with time zone",
                nullable: false,
                oldClrType: typeof(DateTime),
                oldType: "timestamp with time zone",
                oldNullable: true);

            migrationBuilder.AddCheckConstraint(
                name: "CK_Appointments_EndAfterStart",
                table: "Appointments",
                sql: "\"EndTime\" > \"StartTime\"");

            // No two non-cancelled appointments of the same service may overlap.
            // '[)' = start inclusive, end exclusive, so back-to-back slots are allowed.
            migrationBuilder.Sql("""
                ALTER TABLE "Appointments"
                ADD CONSTRAINT "EX_Appointments_NoOverlap"
                EXCLUDE USING gist (
                    "ServiceId" WITH =,
                    tstzrange("StartTime", "EndTime", '[)') WITH &&
                ) WHERE ("Status" <> 'Cancelled');
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""ALTER TABLE "Appointments" DROP CONSTRAINT "EX_Appointments_NoOverlap";""");

            migrationBuilder.DropCheckConstraint(
                name: "CK_Appointments_EndAfterStart",
                table: "Appointments");

            migrationBuilder.DropColumn(
                name: "EndTime",
                table: "Appointments");

            migrationBuilder.AlterDatabase()
                .OldAnnotation("Npgsql:PostgresExtension:btree_gist", ",,");
        }
    }
}
