using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace PropFlow.Infrastructure.Persistence.Migrations.Operations
{
    /// <inheritdoc />
    public partial class M3Operations : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<Guid>(
                name: "BuildingId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "CategoryId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CompletedAt",
                schema: "operations",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "CreatedAt",
                schema: "operations",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTimeOffset(new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified), new TimeSpan(0, 0, 0, 0, 0)));

            migrationBuilder.AddColumn<Guid>(
                name: "CreatorId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "operations",
                table: "WorkItems",
                type: "character varying(4000)",
                maxLength: 4000,
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "EmployeeId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Priority",
                schema: "operations",
                table: "WorkItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<Guid>(
                name: "PropertyId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"));

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledEnd",
                schema: "operations",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<DateTimeOffset>(
                name: "ScheduledStart",
                schema: "operations",
                table: "WorkItems",
                type: "timestamp with time zone",
                nullable: true);

            migrationBuilder.AddColumn<Guid>(
                name: "SpaceId",
                schema: "operations",
                table: "WorkItems",
                type: "uuid",
                nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "Status",
                schema: "operations",
                table: "WorkItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<int>(
                name: "WorkType",
                schema: "operations",
                table: "WorkItems",
                type: "integer",
                nullable: false,
                defaultValue: 0);

            migrationBuilder.AddColumn<string>(
                name: "Email",
                schema: "operations",
                table: "Vendors",
                type: "character varying(254)",
                maxLength: 254,
                nullable: true);

            migrationBuilder.AddColumn<bool>(
                name: "IsActive",
                schema: "operations",
                table: "Vendors",
                type: "boolean",
                nullable: false,
                defaultValue: false);

            migrationBuilder.AddColumn<string>(
                name: "Phone",
                schema: "operations",
                table: "Vendors",
                type: "character varying(40)",
                maxLength: 40,
                nullable: true);

            migrationBuilder.AlterColumn<Guid>(
                name: "VendorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: true,
                oldClrType: typeof(Guid),
                oldType: "uuid");

            migrationBuilder.AddColumn<string>(
                name: "Changes",
                schema: "operations",
                table: "Timeline",
                type: "jsonb",
                nullable: false,
                defaultValue: "");

            migrationBuilder.CreateTable(
                name: "Employees",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    DisplayName = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    Email = table.Column<string>(type: "character varying(254)", maxLength: 254, nullable: true),
                    Phone = table.Column<string>(type: "character varying(40)", maxLength: 40, nullable: true),
                    IsActive = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Employees", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Portfolios",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Portfolios", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "WorkCategories",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    SortOrder = table.Column<int>(type: "integer", nullable: false),
                    IsArchived = table.Column<bool>(type: "boolean", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_WorkCategories", x => new { x.OrganizationId, x.Id });
                });

            migrationBuilder.CreateTable(
                name: "Properties",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PortfolioId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(200)", maxLength: 200, nullable: false),
                    TimeZoneId = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Properties", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Properties_Portfolios_OrganizationId_PortfolioId",
                        columns: x => new { x.OrganizationId, x.PortfolioId },
                        principalSchema: "operations",
                        principalTable: "Portfolios",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Buildings",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Buildings", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Buildings_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "Spaces",
                schema: "operations",
                columns: table => new
                {
                    OrganizationId = table.Column<Guid>(type: "uuid", nullable: false),
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PropertyId = table.Column<Guid>(type: "uuid", nullable: false),
                    BuildingId = table.Column<Guid>(type: "uuid", nullable: true),
                    Code = table.Column<string>(type: "character varying(50)", maxLength: 50, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Spaces", x => new { x.OrganizationId, x.Id });
                    table.ForeignKey(
                        name: "FK_Spaces_Buildings_OrganizationId_BuildingId",
                        columns: x => new { x.OrganizationId, x.BuildingId },
                        principalSchema: "operations",
                        principalTable: "Buildings",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Spaces_Properties_OrganizationId_PropertyId",
                        columns: x => new { x.OrganizationId, x.PropertyId },
                        principalSchema: "operations",
                        principalTable: "Properties",
                        principalColumns: new[] { "OrganizationId", "Id" },
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_BuildingId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "BuildingId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_CategoryId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "CategoryId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_EmployeeId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "EmployeeId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_PropertyId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkItems_OrganizationId_SpaceId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "SpaceId" });

            migrationBuilder.CreateIndex(
                name: "IX_Buildings_OrganizationId_PropertyId",
                schema: "operations",
                table: "Buildings",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_Properties_OrganizationId_PortfolioId",
                schema: "operations",
                table: "Properties",
                columns: new[] { "OrganizationId", "PortfolioId" });

            migrationBuilder.CreateIndex(
                name: "IX_Spaces_OrganizationId_BuildingId",
                schema: "operations",
                table: "Spaces",
                columns: new[] { "OrganizationId", "BuildingId" });

            migrationBuilder.CreateIndex(
                name: "IX_Spaces_OrganizationId_PropertyId",
                schema: "operations",
                table: "Spaces",
                columns: new[] { "OrganizationId", "PropertyId" });

            migrationBuilder.CreateIndex(
                name: "IX_WorkCategories_OrganizationId_Name",
                schema: "operations",
                table: "WorkCategories",
                columns: new[] { "OrganizationId", "Name" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Buildings_OrganizationId_BuildingId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "BuildingId" },
                principalSchema: "operations",
                principalTable: "Buildings",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Employees_OrganizationId_EmployeeId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "EmployeeId" },
                principalSchema: "operations",
                principalTable: "Employees",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Properties_OrganizationId_PropertyId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "PropertyId" },
                principalSchema: "operations",
                principalTable: "Properties",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_Spaces_OrganizationId_SpaceId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "SpaceId" },
                principalSchema: "operations",
                principalTable: "Spaces",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);

            migrationBuilder.AddForeignKey(
                name: "FK_WorkItems_WorkCategories_OrganizationId_CategoryId",
                schema: "operations",
                table: "WorkItems",
                columns: new[] { "OrganizationId", "CategoryId" },
                principalSchema: "operations",
                principalTable: "WorkCategories",
                principalColumns: new[] { "OrganizationId", "Id" },
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Buildings_OrganizationId_BuildingId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Employees_OrganizationId_EmployeeId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Properties_OrganizationId_PropertyId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_Spaces_OrganizationId_SpaceId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropForeignKey(
                name: "FK_WorkItems_WorkCategories_OrganizationId_CategoryId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropTable(
                name: "Employees",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Spaces",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "WorkCategories",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Buildings",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Properties",
                schema: "operations");

            migrationBuilder.DropTable(
                name: "Portfolios",
                schema: "operations");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_BuildingId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_CategoryId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_EmployeeId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_PropertyId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropIndex(
                name: "IX_WorkItems_OrganizationId_SpaceId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "BuildingId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "CategoryId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "CompletedAt",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "CreatedAt",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "CreatorId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "EmployeeId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Priority",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "PropertyId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ScheduledEnd",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "ScheduledStart",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "SpaceId",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "WorkType",
                schema: "operations",
                table: "WorkItems");

            migrationBuilder.DropColumn(
                name: "Email",
                schema: "operations",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "IsActive",
                schema: "operations",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Phone",
                schema: "operations",
                table: "Vendors");

            migrationBuilder.DropColumn(
                name: "Changes",
                schema: "operations",
                table: "Timeline");

            migrationBuilder.AlterColumn<Guid>(
                name: "VendorId",
                schema: "operations",
                table: "Timeline",
                type: "uuid",
                nullable: false,
                defaultValue: new Guid("00000000-0000-0000-0000-000000000000"),
                oldClrType: typeof(Guid),
                oldType: "uuid",
                oldNullable: true);
        }
    }
}
