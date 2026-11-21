using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace Chubb.Claims.Api.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialClaims : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "Users",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    DisplayName = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Role = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Users", x => x.Id);
                    table.CheckConstraint("CK_Users_DisplayName", "length(trim(\"DisplayName\")) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_Users_Role", "\"Role\" IN ('Claimant', 'ClaimsOfficer')");
                });

            migrationBuilder.CreateTable(
                name: "Claims",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClaimNumber = table.Column<string>(type: "TEXT", maxLength: 40, nullable: false),
                    ClaimantId = table.Column<int>(type: "INTEGER", nullable: false),
                    AssignedOfficerId = table.Column<int>(type: "INTEGER", nullable: true),
                    ClaimType = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    IncidentDescription = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    IncidentDate = table.Column<DateOnly>(type: "TEXT", nullable: false),
                    MarketCode = table.Column<string>(type: "TEXT", maxLength: 2, nullable: false),
                    CurrencyCode = table.Column<string>(type: "TEXT", maxLength: 3, nullable: false),
                    ClaimedAmountSen = table.Column<long>(type: "INTEGER", nullable: false),
                    EstimatedLiabilitySen = table.Column<long>(type: "INTEGER", nullable: false),
                    Status = table.Column<string>(type: "TEXT", maxLength: 20, nullable: false),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    UpdatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_Claims", x => x.Id);
                    table.CheckConstraint("CK_Claims_Amounts", "typeof(\"ClaimedAmountSen\") = 'integer' AND \"ClaimedAmountSen\" > 0 AND typeof(\"EstimatedLiabilitySen\") = 'integer' AND \"EstimatedLiabilitySen\" >= 0");
                    table.CheckConstraint("CK_Claims_ClaimNumber", "length(trim(\"ClaimNumber\")) BETWEEN 1 AND 40");
                    table.CheckConstraint("CK_Claims_Description", "length(trim(\"IncidentDescription\")) BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_Claims_MarketCurrency", "\"MarketCode\" = 'MY' AND \"CurrencyCode\" = 'MYR'");
                    table.CheckConstraint("CK_Claims_Status", "\"Status\" IN ('Submitted', 'UnderReview', 'InfoRequested', 'Settled', 'Rejected')");
                    table.CheckConstraint("CK_Claims_Type", "\"ClaimType\" IN ('Motor', 'Property')");
                    table.ForeignKey(
                        name: "FK_Claims_Users_AssignedOfficerId",
                        column: x => x.AssignedOfficerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_Claims_Users_ClaimantId",
                        column: x => x.ClaimantId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "ClaimHistory",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClaimId = table.Column<int>(type: "INTEGER", nullable: false),
                    ActorId = table.Column<int>(type: "INTEGER", nullable: false),
                    Action = table.Column<string>(type: "TEXT", maxLength: 100, nullable: false),
                    Notes = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    CreatedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ClaimHistory", x => x.Id);
                    table.CheckConstraint("CK_History_Action", "length(trim(\"Action\")) BETWEEN 1 AND 100");
                    table.CheckConstraint("CK_History_Notes", "\"Notes\" IS NULL OR length(\"Notes\") <= 2000");
                    table.ForeignKey(
                        name: "FK_ClaimHistory_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_ClaimHistory_Users_ActorId",
                        column: x => x.ActorId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "InformationRequests",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ClaimId = table.Column<int>(type: "INTEGER", nullable: false),
                    RequestedByOfficerId = table.Column<int>(type: "INTEGER", nullable: false),
                    Message = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: false),
                    Response = table.Column<string>(type: "TEXT", maxLength: 2000, nullable: true),
                    RequestedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: false),
                    RespondedAt = table.Column<DateTimeOffset>(type: "TEXT", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_InformationRequests", x => x.Id);
                    table.CheckConstraint("CK_Requests_Message", "length(trim(\"Message\")) BETWEEN 1 AND 2000");
                    table.CheckConstraint("CK_Requests_Response", "\"Response\" IS NULL OR length(\"Response\") <= 2000");
                    table.ForeignKey(
                        name: "FK_InformationRequests_Claims_ClaimId",
                        column: x => x.ClaimId,
                        principalTable: "Claims",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                    table.ForeignKey(
                        name: "FK_InformationRequests_Users_RequestedByOfficerId",
                        column: x => x.RequestedByOfficerId,
                        principalTable: "Users",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_ClaimHistory_ActorId",
                table: "ClaimHistory",
                column: "ActorId");

            migrationBuilder.CreateIndex(
                name: "IX_ClaimHistory_ClaimId",
                table: "ClaimHistory",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_AssignedOfficerId",
                table: "Claims",
                column: "AssignedOfficerId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimantId",
                table: "Claims",
                column: "ClaimantId");

            migrationBuilder.CreateIndex(
                name: "IX_Claims_ClaimNumber",
                table: "Claims",
                column: "ClaimNumber",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_InformationRequests_ClaimId",
                table: "InformationRequests",
                column: "ClaimId");

            migrationBuilder.CreateIndex(
                name: "IX_InformationRequests_RequestedByOfficerId",
                table: "InformationRequests",
                column: "RequestedByOfficerId");
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "ClaimHistory");

            migrationBuilder.DropTable(
                name: "InformationRequests");

            migrationBuilder.DropTable(
                name: "Claims");

            migrationBuilder.DropTable(
                name: "Users");
        }
    }
}
