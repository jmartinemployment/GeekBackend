using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Stage 3 of plans/project-is-the-whole.md: projects track tasks and time.
    /// </summary>
    /// <remarks>
    /// Two invariants here are worth more than the tables. An entry's task must belong to the
    /// entry's own project, enforced by a composite foreign key rather than a check in C#: hours
    /// billed against another client's task is the kind of error that is never noticed and never
    /// forgiven. And an invoiced entry is frozen, by trigger — money that has left the building is
    /// not editable.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921150000_AddGccTasksAndTimeEntries")]
    public partial class AddGccTasksAndTimeEntries : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_tasks",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    assignee_user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: true),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    estimated_hours = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    sort_order = table.Column<int>(type: "integer", nullable: false),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_tasks", x => x.id);

                    // The target of gcc_time_entries' composite foreign key below. Without it, an
                    // entry could point at a task belonging to a different project.
                    table.UniqueConstraint("ak_gcc_tasks_id_project_id", x => new { x.id, x.project_id });

                    table.ForeignKey(
                        name: "FK_gcc_tasks_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);

                    table.CheckConstraint(
                        "ck_gcc_tasks_status",
                        "status IN ('todo', 'in_progress', 'done')");
                });

            migrationBuilder.CreateTable(
                name: "gcc_time_entries",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    task_id = table.Column<Guid>(type: "uuid", nullable: true),
                    user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    work_date = table.Column<DateOnly>(type: "date", nullable: false),
                    minutes = table.Column<int>(type: "integer", nullable: false),
                    description = table.Column<string>(type: "text", nullable: true),
                    billable = table.Column<bool>(type: "boolean", nullable: false),
                    rate_snapshot = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    currency = table.Column<string>(type: "char(3)", nullable: true),
                    invoiced_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_time_entries", x => x.id);

                    table.ForeignKey(
                        name: "FK_gcc_time_entries_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);

                    // (task_id, project_id) together, so a task from another project cannot be
                    // referenced. This is the whole reason gcc_tasks carries that unique pair.
                    table.ForeignKey(
                        name: "FK_gcc_time_entries_gcc_tasks_task_id_project_id",
                        columns: x => new { x.task_id, x.project_id },
                        principalSchema: "content_creator",
                        principalTable: "gcc_tasks",
                        principalColumns: new[] { "id", "project_id" },
                        onDelete: ReferentialAction.Restrict);

                    table.CheckConstraint("ck_gcc_time_entries_minutes_positive", "minutes > 0");

                    table.CheckConstraint(
                        "ck_gcc_time_entries_billable_has_rate",
                        "billable = false OR (rate_snapshot IS NOT NULL AND currency IS NOT NULL)");
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_tasks_project_id_sort_order",
                schema: "content_creator",
                table: "gcc_tasks",
                columns: new[] { "project_id", "sort_order" });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_time_entries_project_id_work_date",
                schema: "content_creator",
                table: "gcc_time_entries",
                columns: new[] { "project_id", "work_date" });

            // Both tables join the no-truncate rule. gcc_time_entries in particular: RESTRICT stops
            // a DELETE of a project that has hours against it, and does nothing whatsoever about a
            // TRUNCATE ... CASCADE arriving from elsewhere.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_tasks_forbid_truncate
                BEFORE TRUNCATE ON content_creator.gcc_tasks
                FOR EACH STATEMENT EXECUTE FUNCTION content_creator.forbid_truncate();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_time_entries_forbid_truncate
                BEFORE TRUNCATE ON content_creator.gcc_time_entries
                FOR EACH STATEMENT EXECUTE FUNCTION content_creator.forbid_truncate();
                """);

            // An invoiced entry is frozen. Not "should not be edited" — cannot be. The row backs an
            // invoice that has already been sent, so changing its minutes or its rate after the
            // fact makes the books disagree with what the client was asked to pay.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION content_creator.gcc_time_entries_freeze_invoiced()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'content_creator.gcc_time_entries row % was invoiced at % and cannot be %s',
                        OLD.id, OLD.invoiced_at_utc, lower(TG_OP)
                        USING ERRCODE = 'raise_exception';
                END;
                $$;
                """);

            // WHEN on the OLD row: an entry that has not been invoiced is still editable, and an
            // entry that has is not. The condition lives in the trigger rather than in a function
            // body so Postgres does not even call it for the ordinary case.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_time_entries_freeze_invoiced
                BEFORE UPDATE OR DELETE ON content_creator.gcc_time_entries
                FOR EACH ROW
                WHEN (OLD.invoiced_at_utc IS NOT NULL)
                EXECUTE FUNCTION content_creator.gcc_time_entries_freeze_invoiced();
                """);

            // The log's event types grow with the stages that produce them. Widened here rather
            // than listed up front, so an event nothing can yet write is an event nothing can
            // record by mistake.
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
                    event_type IN ('project_created', 'project_updated', 'project_status_changed')
                );
                """);

            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_time_entries_freeze_invoiced ON content_creator.gcc_time_entries;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_time_entries_forbid_truncate ON content_creator.gcc_time_entries;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_tasks_forbid_truncate ON content_creator.gcc_tasks;");

            migrationBuilder.DropTable(name: "gcc_time_entries", schema: "content_creator");
            migrationBuilder.DropTable(name: "gcc_tasks", schema: "content_creator");

            migrationBuilder.Sql(
                "DROP FUNCTION IF EXISTS content_creator.gcc_time_entries_freeze_invoiced();");
        }
    }
}
