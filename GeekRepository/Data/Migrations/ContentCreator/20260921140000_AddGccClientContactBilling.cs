using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.EntityFrameworkCore.Migrations;
using GeekRepository.Data;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// Stage 2 of plans/project-is-the-whole.md: the client becomes a client record.
    /// </summary>
    /// <remarks>
    /// Brought forward ahead of the rest of Stage 2 because Stage 1 shipped a foreign key that
    /// depends on it. gcc_projects.client_id references gcc_clients, but the client list the UI
    /// showed came from a second, blob-backed store whose ids are unrelated — so every attempt to
    /// create a project was refused by that key. One client table is what makes it satisfiable.
    ///
    /// Existing rows are deleted rather than backfilled. The required columns below have no
    /// sensible default: inventing a contact email or a currency to satisfy NOT NULL would be
    /// exactly the auto-repair this codebase forbids, and the plan records that current client
    /// rows are development data that is not carried forward. A client with projects already
    /// attached cannot be deleted — gcc_projects.client_id is RESTRICT — so if any exist this
    /// migration fails loudly instead of destroying a record of work.
    /// </remarks>
    [DbContext(typeof(ContentCreatorDbContext))]
    [Migration("20260921140000_AddGccClientContactBilling")]
    public partial class AddGccClientContactBilling : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // DELETE, not TRUNCATE: the no-truncate trigger added at the end of this migration is
            // about to make TRUNCATE impossible, and a DELETE is what respects the RESTRICT keys.
            migrationBuilder.Sql("DELETE FROM content_creator.gcc_clients;");

            migrationBuilder.AddColumn<string>(
                name: "contact_name", schema: "content_creator", table: "gcc_clients",
                type: "character varying(256)", maxLength: 256, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "contact_email", schema: "content_creator", table: "gcc_clients",
                type: "character varying(320)", maxLength: 320, nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "contact_phone", schema: "content_creator", table: "gcc_clients",
                type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_contact_name", schema: "content_creator", table: "gcc_clients",
                type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_email", schema: "content_creator", table: "gcc_clients",
                type: "character varying(320)", maxLength: 320, nullable: false, defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "contact_address_line1", schema: "content_creator", table: "gcc_clients",
                type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "contact_address_line2", schema: "content_creator", table: "gcc_clients",
                type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "contact_city", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "contact_region", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "contact_postal_code", schema: "content_creator", table: "gcc_clients",
                type: "character varying(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "contact_country", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);

            migrationBuilder.AddColumn<string>(
                name: "billing_address_line1", schema: "content_creator", table: "gcc_clients",
                type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_address_line2", schema: "content_creator", table: "gcc_clients",
                type: "character varying(256)", maxLength: 256, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_city", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_region", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_postal_code", schema: "content_creator", table: "gcc_clients",
                type: "character varying(32)", maxLength: 32, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "billing_country", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);

            migrationBuilder.AddColumn<int>(
                name: "payment_terms_days", schema: "content_creator", table: "gcc_clients",
                type: "integer", nullable: false, defaultValue: 0);
            migrationBuilder.AddColumn<decimal>(
                name: "rate", schema: "content_creator", table: "gcc_clients",
                type: "numeric(12,2)", nullable: true);
            // No database default. A currency every client silently shares is how the wrong one
            // ends up on an invoice.
            migrationBuilder.AddColumn<string>(
                name: "currency", schema: "content_creator", table: "gcc_clients",
                type: "char(3)", nullable: false, defaultValue: "");
            migrationBuilder.AddColumn<string>(
                name: "tax_id", schema: "content_creator", table: "gcc_clients",
                type: "character varying(64)", maxLength: 64, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "po_reference", schema: "content_creator", table: "gcc_clients",
                type: "character varying(64)", maxLength: 64, nullable: true);

            // The publish target, flattened off the old Workflow Client. Env-var names only — the
            // secrets they name are read at call time and never stored.
            migrationBuilder.AddColumn<string>(
                name: "publish_api_base_url", schema: "content_creator", table: "gcc_clients",
                type: "character varying(2048)", maxLength: 2048, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "publish_oauth_token_endpoint", schema: "content_creator", table: "gcc_clients",
                type: "character varying(512)", maxLength: 512, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "publish_client_id_env_var", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "publish_client_secret_env_var", schema: "content_creator", table: "gcc_clients",
                type: "character varying(128)", maxLength: 128, nullable: true);
            migrationBuilder.AddColumn<int>(
                name: "publish_default_author_id", schema: "content_creator", table: "gcc_clients",
                type: "integer", nullable: true);
            migrationBuilder.AddColumn<string>(
                name: "publish_category_strategy", schema: "content_creator", table: "gcc_clients",
                type: "character varying(64)", maxLength: 64, nullable: true);

            // The defaults above exist only so AddColumn can make the columns NOT NULL. Dropping
            // them immediately means no row can be written later without saying what it is: an
            // empty contact email or currency has to come from a caller, and the checks below
            // refuse it.
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients ALTER COLUMN contact_name DROP DEFAULT;");
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients ALTER COLUMN contact_email DROP DEFAULT;");
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients ALTER COLUMN billing_email DROP DEFAULT;");
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients ALTER COLUMN currency DROP DEFAULT;");
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients ALTER COLUMN payment_terms_days DROP DEFAULT;");

            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_clients
                ADD CONSTRAINT ck_gcc_clients_payment_terms_days CHECK (payment_terms_days >= 0);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_clients
                ADD CONSTRAINT ck_gcc_clients_rate_positive CHECK (rate IS NULL OR rate > 0);
                """);
            migrationBuilder.Sql("""
                ALTER TABLE content_creator.gcc_clients
                ADD CONSTRAINT ck_gcc_clients_currency_iso4217 CHECK (currency ~ '^[A-Z]{3}$');
                """);

            // This table holds billing data now, so it joins the no-truncate rule with the rest.
            migrationBuilder.Sql("""
                CREATE TRIGGER trg_gcc_clients_forbid_truncate
                BEFORE TRUNCATE ON content_creator.gcc_clients
                FOR EACH STATEMENT EXECUTE FUNCTION content_creator.forbid_truncate();
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql(
                "DROP TRIGGER IF EXISTS trg_gcc_clients_forbid_truncate ON content_creator.gcc_clients;");

            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients DROP CONSTRAINT IF EXISTS ck_gcc_clients_currency_iso4217;");
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients DROP CONSTRAINT IF EXISTS ck_gcc_clients_rate_positive;");
            migrationBuilder.Sql(
                "ALTER TABLE content_creator.gcc_clients DROP CONSTRAINT IF EXISTS ck_gcc_clients_payment_terms_days;");

            foreach (var column in new[]
                     {
                         "contact_name", "contact_email", "contact_phone",
                         "billing_contact_name", "billing_email",
                         "contact_address_line1", "contact_address_line2", "contact_city",
                         "contact_region", "contact_postal_code", "contact_country",
                         "billing_address_line1", "billing_address_line2", "billing_city",
                         "billing_region", "billing_postal_code", "billing_country",
                         "payment_terms_days", "rate", "currency", "tax_id", "po_reference",
                         "publish_api_base_url", "publish_oauth_token_endpoint",
                         "publish_client_id_env_var", "publish_client_secret_env_var",
                         "publish_default_author_id", "publish_category_strategy",
                     })
            {
                migrationBuilder.DropColumn(
                    name: column, schema: "content_creator", table: "gcc_clients");
            }
        }
    }
}
