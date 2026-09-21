using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A project can be deleted from every view without touching the append-only log.
    /// </summary>
    /// <remarks>
    /// A hard DELETE on gcc_projects was never reachable: every project gets a project_created log
    /// row in its own creation transaction, that row can never be removed
    /// (trg_gcc_project_log_append_only), and FK_gcc_project_log_gcc_projects_project_id is
    /// RESTRICT. deleted_at_utc is the whole mechanism — a project is "deleted" by being marked,
    /// then filtered out of every read, while its rows and its log stay exactly as they were.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921180000_AddGccProjectSoftDelete")]
    public partial class AddGccProjectSoftDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.AddColumn<DateTime>(
                name: "deleted_at_utc",
                schema: "content_creator",
                table: "gcc_projects",
                nullable: true);

            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_project_log
                DROP CONSTRAINT IF EXISTS ck_gcc_project_log_event_type;
                """);
            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_project_log
                ADD CONSTRAINT ck_gcc_project_log_event_type CHECK (
                    event_type IN (
                        'project_created', 'project_updated', 'project_status_changed', 'project_deleted',
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
                        'task_created', 'task_updated', 'task_completed', 'time_logged',
                        'deliverable_created', 'deliverable_updated', 'deliverable_delivered'
                    )
                );
                """);

            migrationBuilder.DropColumn(
                name: "deleted_at_utc",
                schema: "content_creator",
                table: "gcc_projects");
        }
    }
}
