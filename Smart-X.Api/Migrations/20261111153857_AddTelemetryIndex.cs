using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smart_X.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddTelemetryIndex : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateIndex(
                name: "IX_TelemetryRecords_DeviceId_Timestamp",
                table: "TelemetryRecords",
                columns: new[] { "DeviceId", "Timestamp" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropIndex(
                name: "IX_TelemetryRecords_DeviceId_Timestamp",
                table: "TelemetryRecords");
        }
    }
}
