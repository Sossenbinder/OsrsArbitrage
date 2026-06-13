using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace ArbitrageTracker.Data.Migrations
{
    /// <inheritdoc />
    public partial class InitialCreate : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "AppSettings",
                columns: table => new
                {
                    Key = table.Column<string>(type: "TEXT", nullable: false),
                    Value = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_AppSettings", x => x.Key);
                });

            migrationBuilder.CreateTable(
                name: "BucketSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    Interval = table.Column<string>(type: "TEXT", nullable: false),
                    Timestamp = table.Column<long>(type: "INTEGER", nullable: false),
                    AvgHighPrice = table.Column<long>(type: "INTEGER", nullable: true),
                    AvgLowPrice = table.Column<long>(type: "INTEGER", nullable: true),
                    HighPriceVolume = table.Column<long>(type: "INTEGER", nullable: false),
                    LowPriceVolume = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_BucketSnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "ItemMappings",
                columns: table => new
                {
                    Id = table.Column<int>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    Name = table.Column<string>(type: "TEXT", nullable: false),
                    Examine = table.Column<string>(type: "TEXT", nullable: false),
                    Members = table.Column<bool>(type: "INTEGER", nullable: false),
                    LowAlch = table.Column<int>(type: "INTEGER", nullable: false),
                    HighAlch = table.Column<int>(type: "INTEGER", nullable: false),
                    BuyLimit = table.Column<int>(type: "INTEGER", nullable: false),
                    Value = table.Column<long>(type: "INTEGER", nullable: false),
                    Icon = table.Column<string>(type: "TEXT", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_ItemMappings", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "OpportunitySnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    DetectedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    BuyPrice = table.Column<long>(type: "INTEGER", nullable: false),
                    SellPrice = table.Column<long>(type: "INTEGER", nullable: false),
                    NetMargin = table.Column<long>(type: "INTEGER", nullable: false),
                    ExpectedCycleProfit = table.Column<long>(type: "INTEGER", nullable: false),
                    SafetyScore = table.Column<double>(type: "REAL", nullable: false),
                    ProxyValidated = table.Column<bool>(type: "INTEGER", nullable: true),
                    ProxyEvaluatedAt = table.Column<long>(type: "INTEGER", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_OpportunitySnapshots", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "PriceSnapshots",
                columns: table => new
                {
                    Id = table.Column<long>(type: "INTEGER", nullable: false)
                        .Annotation("Sqlite:Autoincrement", true),
                    ItemId = table.Column<int>(type: "INTEGER", nullable: false),
                    CapturedAt = table.Column<long>(type: "INTEGER", nullable: false),
                    High = table.Column<long>(type: "INTEGER", nullable: true),
                    HighTime = table.Column<long>(type: "INTEGER", nullable: false),
                    Low = table.Column<long>(type: "INTEGER", nullable: true),
                    LowTime = table.Column<long>(type: "INTEGER", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_PriceSnapshots", x => x.Id);
                });

            migrationBuilder.CreateIndex(
                name: "IX_BucketSnapshots_ItemId_Interval_Timestamp",
                table: "BucketSnapshots",
                columns: new[] { "ItemId", "Interval", "Timestamp" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_OpportunitySnapshots_ItemId_DetectedAt",
                table: "OpportunitySnapshots",
                columns: new[] { "ItemId", "DetectedAt" });

            migrationBuilder.CreateIndex(
                name: "IX_PriceSnapshots_ItemId_CapturedAt",
                table: "PriceSnapshots",
                columns: new[] { "ItemId", "CapturedAt" });
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "AppSettings");

            migrationBuilder.DropTable(
                name: "BucketSnapshots");

            migrationBuilder.DropTable(
                name: "ItemMappings");

            migrationBuilder.DropTable(
                name: "OpportunitySnapshots");

            migrationBuilder.DropTable(
                name: "PriceSnapshots");
        }
    }
}
