using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// The bank of partner extractions, keyed by partner host and a digest of the pages extracted.
    /// </summary>
    /// <remarks>
    /// Its own table rather than a field on gcc_creates.research_json, which is written by whole-column
    /// replacement from two callers (the brief save and generation) with no concurrency token: last
    /// writer wins, and a generate racing a brief save would silently drop entries. A row per
    /// (host, digest) collides with nothing, and the second create on the same partners reads the
    /// first's row instead of paying for the extraction again.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20261004090000_AddGccPartnerExtractions")]
    public partial class AddGccPartnerExtractions : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_partner_extractions",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    partner_host = table.Column<string>(type: "character varying(253)", maxLength: 253, nullable: false),
                    pages_digest = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    create_id = table.Column<Guid>(type: "uuid", nullable: true),
                    product_name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    extraction_json = table.Column<string>(type: "text", nullable: false),
                    pages_attempted = table.Column<int>(type: "integer", nullable: false),
                    extracted_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_partner_extractions", x => x.id);
                });

            migrationBuilder.CreateIndex(
                name: "ux_gcc_partner_extractions_host_digest",
                schema: "content_creator",
                table: "gcc_partner_extractions",
                columns: new[] { "partner_host", "pages_digest" },
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "gcc_partner_extractions",
                schema: "content_creator");
        }
    }
}
