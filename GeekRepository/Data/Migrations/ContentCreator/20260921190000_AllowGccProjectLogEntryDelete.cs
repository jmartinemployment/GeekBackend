using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A single project_log entry can be deleted. Confirmed by Jeff 2026-09-21, choosing a true,
    /// permanent delete over a hide-only alternative after the tradeoff was raised: this narrows
    /// the log's append-only guarantee rather than removing it.
    /// </summary>
    /// <remarks>
    /// trg_gcc_project_log_append_only narrows from "BEFORE UPDATE OR DELETE" to "BEFORE UPDATE" —
    /// a row can now be removed outright, but still never rewritten in place. Deleting one is itself
    /// logged, as log_entry_deleted, so the log still shows that a deletion happened and by whom,
    /// even though the deleted entry's own content is genuinely gone.
    /// forbid_truncate() on this table is untouched — bulk removal was never asked for.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921190000_AllowGccProjectLogEntryDelete")]
    public partial class AllowGccProjectLogEntryDelete : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_gcc_project_log_append_only
                ON content_creator.gcc_project_log;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_project_log_append_only
                BEFORE UPDATE ON content_creator.gcc_project_log
                FOR EACH ROW EXECUTE FUNCTION content_creator.gcc_project_log_append_only();
                """);

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
                        'deliverable_created', 'deliverable_updated', 'deliverable_delivered',
                        'log_entry_deleted'
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
                        'project_created', 'project_updated', 'project_status_changed', 'project_deleted',
                        'task_created', 'task_updated', 'task_completed', 'time_logged',
                        'deliverable_created', 'deliverable_updated', 'deliverable_delivered'
                    )
                );
                """);

            migrationBuilder.Sql("""
                DROP TRIGGER IF EXISTS trg_gcc_project_log_append_only
                ON content_creator.gcc_project_log;
                """);
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_project_log_append_only
                BEFORE UPDATE OR DELETE ON content_creator.gcc_project_log
                FOR EACH ROW EXECUTE FUNCTION content_creator.gcc_project_log_append_only();
                """);
        }
    }
}
