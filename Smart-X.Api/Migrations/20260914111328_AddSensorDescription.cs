using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Smart_X.Api.Migrations
{
    /// <inheritdoc />
    public partial class AddSensorDescription : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<string>(
                name: "Description",
                table: "Sensors",
                type: "TEXT",
                nullable: false,
                defaultValue: "");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropColumn(
                name: "Description",
                table: "Sensors");
        }
    }
}
