using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Stage 4 of plans/project-is-the-whole.md: what the client receives.
    /// </summary>
    /// <remarks>
    /// A deliverable is the project-side record of a create, not a second copy of it. The content
    /// pipeline is untouched — creates, artifacts, versions and approvals stay exactly where they
    /// are — and this table says which project promised the thing and when it was handed over.
    ///
    /// The unique create_id is the constraint that matters: one create is one deliverable. The same
    /// piece of content listed under two projects would make both schedules and both invoices
    /// wrong, with nothing to say which was the mistake.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921160000_AddGccDeliverables")]
    public partial class AddGccDeliverables : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_deliverables",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    create_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    delivered_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_deliverables", x => x.id);

                    table.ForeignKey(
                        name: "FK_gcc_deliverables_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);

                    // RESTRICT, so deleting a create that a project has promised is refused rather
                    // than quietly leaving a deliverable pointing at nothing.
                    table.ForeignKey(
                        name: "FK_gcc_deliverables_gcc_creates_create_id",
                        column: x => x.create_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_creates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    table.CheckConstraint(
                        "ck_gcc_deliverables_status",
                        "status IN ('planned', 'in_progress', 'delivered')");

                    table.CheckConstraint(
                        "ck_gcc_deliverables_delivered_at_matches_status",
                        "(status = 'delivered') = (delivered_at_utc IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_deliverables_create_id_unique",
                schema: "content_creator",
                table: "gcc_deliverables",
                column: "create_id",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_deliverables_project_id",
                schema: "content_creator",
                table: "gcc_deliverables",
                column: "project_id");

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_deliverables_forbid_truncate
                BEFORE TRUNCATE ON content_creator.gcc_deliverables
                FOR EACH STATEMENT EXECUTE FUNCTION content_creator.forbid_truncate();
                """);

            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_project_log
                DROP CONSTRAINT IF EXISTS ck_gcc_project_log_event_type;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_project_log
                ADD CONSTRAINT ck_gcc_project_log_event_type CHECK (
                    event_type IN (
                        'project_created', 'project_updated', 'project_status_changed',
                        'task_created', 'task_updated', 'task_completed', 'time_logged',
                        'deliverable_created', 'deliverable_updated', 'deliverable_delivered'
                    )
                );
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_project_log
                DROP CONSTRAINT IF EXISTS ck_gcc_project_log_event_type;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_project_log
                ADD CONSTRAINT ck_gcc_project_log_event_type CHECK (
                    event_type IN (
                        'project_created', 'project_updated', 'project_status_changed',
                        'task_created', 'task_updated', 'task_completed', 'time_logged'
                    )
                );
                """);

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_deliverables_forbid_truncate ON content_creator.gcc_deliverables;");

            migrationBuilder.DropTable(name: "gcc_deliverables", schema: "content_creator");
        }
    }
}
