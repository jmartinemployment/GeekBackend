using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;
using Npgsql.EntityFrameworkCore.PostgreSQL.Metadata;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Stage 1 of plans/project-is-the-whole.md: a project becomes a row.
    /// </summary>
    /// <remarks>
    /// The invariants that matter are in the database, not in C#. A project with logged work is
    /// never deleted (RESTRICT on every child, and every project has a log row from its first
    /// insert); the log cannot be rewritten (a trigger refuses UPDATE and DELETE); and neither
    /// table can be truncated, including by a TRUNCATE ... CASCADE that arrives from somewhere
    /// else. Application code can be redeployed around; a constraint cannot.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921130000_AddGccProjects")]
    public partial class AddGccProjects : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.CreateTable(
                name: "gcc_projects",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<Guid>(type: "uuid", nullable: false),
                    client_id = table.Column<Guid>(type: "uuid", nullable: false),
                    idempotency_key = table.Column<Guid>(type: "uuid", nullable: false),
                    name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    code = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    description = table.Column<string>(type: "text", nullable: true),
                    status = table.Column<string>(type: "character varying(32)", maxLength: 32, nullable: false),
                    site_url = table.Column<string>(type: "character varying(2048)", maxLength: 2048, nullable: true),
                    project_site_run_id = table.Column<Guid>(type: "uuid", nullable: true),
                    department = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: true),
                    partner_urls = table.Column<List<string>>(type: "text[]", nullable: false),
                    competitor_urls = table.Column<List<string>>(type: "text[]", nullable: false),
                    start_date = table.Column<DateOnly>(type: "date", nullable: false),
                    due_date = table.Column<DateOnly>(type: "date", nullable: true),
                    finished_date = table.Column<DateOnly>(type: "date", nullable: true),
                    estimated_hours = table.Column<decimal>(type: "numeric(8,2)", nullable: true),
                    budget = table.Column<decimal>(type: "numeric(12,2)", nullable: true),
                    budget_currency = table.Column<string>(type: "char(3)", nullable: true),
                    created_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_projects", x => x.id);

                    // RESTRICT: a client with projects is not deleted out from under them.
                    table.ForeignKey(
                        name: "FK_gcc_projects_gcc_clients_client_id",
                        column: x => x.client_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_clients",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);

                    table.CheckConstraint(
                        "ck_gcc_projects_status",
                        "status IN ('planned', 'active', 'on_hold', 'finished', 'cancelled')");

                    // Finished and finish date are one fact. Either both or neither — a finished
                    // project with no finish date cannot be reported on, and a finish date on a
                    // running project is a claim nothing backs.
                    table.CheckConstraint(
                        "ck_gcc_projects_finished_date_matches_status",
                        "(status = 'finished') = (finished_date IS NOT NULL)");

                    table.CheckConstraint(
                        "ck_gcc_projects_due_date_after_start",
                        "due_date IS NULL OR due_date >= start_date");

                    table.CheckConstraint(
                        "ck_gcc_projects_finished_date_after_start",
                        "finished_date IS NULL OR finished_date >= start_date");

                    // An amount with no currency is not money.
                    table.CheckConstraint(
                        "ck_gcc_projects_budget_currency_pair",
                        "(budget IS NULL) = (budget_currency IS NULL)");
                });

            migrationBuilder.CreateTable(
                name: "gcc_project_log",
                schema: "content_creator",
                columns: table => new
                {
                    id = table.Column<long>(type: "bigint", nullable: false)
                        .Annotation("Npgsql:ValueGenerationStrategy", NpgsqlValueGenerationStrategy.IdentityByDefaultColumn),
                    project_id = table.Column<Guid>(type: "uuid", nullable: false),
                    occurred_at_utc = table.Column<DateTime>(type: "timestamp with time zone", nullable: false, defaultValueSql: "now()"),
                    actor_user_id = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    event_type = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    payload = table.Column<string>(type: "jsonb", nullable: false),
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_gcc_project_log", x => x.id);

                    table.ForeignKey(
                        name: "FK_gcc_project_log_gcc_projects_project_id",
                        column: x => x.project_id,
                        principalSchema: "content_creator",
                        principalTable: "gcc_projects",
                        principalColumn: "id",
                        onDelete: ReferentialAction.Restrict);

                    table.CheckConstraint(
                        "ck_gcc_project_log_event_type",
                        "event_type IN ('project_created', 'project_updated', 'project_status_changed')");
                });

            migrationBuilder.CreateIndex(
                name: "ix_gcc_projects_idempotency_key_unique",
                schema: "content_creator",
                table: "gcc_projects",
                column: "idempotency_key",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "ix_gcc_projects_client_id",
                schema: "content_creator",
                table: "gcc_projects",
                column: "client_id");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_projects_client_id_code_unique",
                schema: "content_creator",
                table: "gcc_projects",
                columns: new[] { "client_id", "code" },
                unique: true,
                filter: "code IS NOT NULL");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_project_log_project_id_occurred_at_utc",
                schema: "content_creator",
                table: "gcc_project_log",
                columns: new[] { "project_id", "occurred_at_utc" });

            // No table in this schema is ever truncated — not by application code, a migration, a
            // test or a script. RESTRICT foreign keys stop DELETE; they do not stop TRUNCATE, and
            // a TRUNCATE ... CASCADE arriving at a parent table takes the children with it without
            // consulting a single foreign key.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION content_creator.forbid_truncate()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'TRUNCATE is forbidden on %.%', TG_TABLE_SCHEMA, TG_TABLE_NAME
                        USING ERRCODE = 'raise_exception';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_projects_forbid_truncate
                BEFORE TRUNCATE ON content_creator.gcc_projects
                FOR EACH STATEMENT EXECUTE FUNCTION content_creator.forbid_truncate();
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_project_log_forbid_truncate
                BEFORE TRUNCATE ON content_creator.gcc_project_log
                FOR EACH STATEMENT EXECUTE FUNCTION content_creator.forbid_truncate();
                """);

            // The log is evidence, so it is append-only. Without this, a log entry could be edited
            // into agreement with the row it describes, and the two would stop being independent
            // accounts of what happened.
            migrationBuilder.Sql("""
                CREATE OR REPLACE FUNCTION content_creator.gcc_project_log_append_only()
                RETURNS trigger
                LANGUAGE plpgsql
                AS $$
                BEGIN
                    RAISE EXCEPTION
                        'content_creator.gcc_project_log is append-only; % is not permitted', TG_OP
                        USING ERRCODE = 'raise_exception';
                END;
                $$;
                """);

            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_project_log_append_only
                BEFORE UPDATE OR DELETE ON content_creator.gcc_project_log
                FOR EACH ROW EXECUTE FUNCTION content_creator.gcc_project_log_append_only();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The triggers go with their tables. forbid_truncate() is dropped last and only here,
            // where both of its users are being removed in the same statement batch.
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_project_log_append_only ON content_creator.gcc_project_log;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_project_log_forbid_truncate ON content_creator.gcc_project_log;");
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_projects_forbid_truncate ON content_creator.gcc_projects;");

            migrationBuilder.DropTable(
                name: "gcc_project_log",
                schema: "content_creator");

            migrationBuilder.DropTable(
                name: "gcc_projects",
                schema: "content_creator");

            migrationBuilder.Sql("DROP FUNCTION IF EXISTS content_creator.gcc_project_log_append_only();");
            migrationBuilder.Sql("DROP FUNCTION IF EXISTS content_creator.forbid_truncate();");
        }
    }
}
