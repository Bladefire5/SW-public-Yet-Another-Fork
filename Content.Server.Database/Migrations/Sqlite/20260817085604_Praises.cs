using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Content.Server.Database.Migrations.Sqlite
{
    /// <inheritdoc />
    public partial class Praises : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "praise",
                columns: table => new
                {
                    given_to = table.Column<Guid>(type: "TEXT", nullable: false),
                    given_by = table.Column<Guid>(type: "TEXT", nullable: false),
                    date = table.Column<DateTime>(type: "TEXT", nullable: false),
                    given_by_name = table.Column<string>(type: "TEXT", maxLength: 10, nullable: false),
                    reason = table.Column<string>(type: "TEXT", maxLength: 50, nullable: false),
                    weight = table.Column<int>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_praise", x => new { x.given_to, x.given_by, x.date });
                });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "praise");
        }
    }
}
