using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// A re-run replaces the page; nothing older is kept (Jeff, 2026-10-06: "no history is required
    /// ... the old created content should be deleted"). Deletes the content earlier runs left behind
    /// and puts both rules into the database: one page per type and name on a project, one version
    /// per page.
    /// </summary>
    /// <remarks>
    /// <para>
    /// What goes, in order. Every version of a page except its newest, with the evidence and approval
    /// events of those versions. Every page that is an older duplicate of another on the same project
    /// -- same type and name, case and surrounding spaces aside, neither derived from another page --
    /// with its versions and theirs; a page derived from a deleted duplicate is re-pointed at the page
    /// that stays. The one version each page keeps is numbered 1.
    /// </para>
    /// <para>
    /// Then the constraints. The unique index creation is the check: if any duplicate survived the
    /// deletes, it fails, the transaction rolls back, and GeekRepository refuses to start rather than
    /// run with a rule the data breaks. The one-page index is an expression index over
    /// lower(btrim(...)), which EF cannot model, so it lives here and nowhere in the snapshot.
    /// </para>
    /// <para>
    /// Pages under a create that was never assigned a project (project_id null) are outside the
    /// one-page rule, as nulls are to Postgres; their superseded versions are still deleted.
    /// </para>
    /// </remarks>
    public partial class ARerunReplacesThePage : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            // 1. Superseded versions: every version of a page except its newest, and what hung off them.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE superseded_versions ON COMMIT DROP AS
                SELECT v."Id"
                FROM content_creator.gcc_artifact_versions AS v
                WHERE v."Id" <> (
                    SELECT n."Id"
                    FROM content_creator.gcc_artifact_versions AS n
                    WHERE n."ArtifactId" = v."ArtifactId"
                    ORDER BY n."VersionNumber" DESC, n."CreatedAtUtc" DESC, n."Id" DESC
                    LIMIT 1);

                DELETE FROM content_creator.gcc_approval_events
                WHERE "ArtifactVersionId" IN (SELECT "Id" FROM superseded_versions);

                DELETE FROM content_creator.gcc_version_evidence
                WHERE version_id IN (SELECT "Id" FROM superseded_versions);

                DELETE FROM content_creator.gcc_artifact_versions
                WHERE "Id" IN (SELECT "Id" FROM superseded_versions);
                """);

            // 2. Duplicate pages: for one project, type and name, the newest page stays and the others go
            //    with their content. A page derived from one that goes now derives from the one that stays.
            migrationBuilder.Sql("""
                CREATE TEMP TABLE superseded_pages ON COMMIT DROP AS
                SELECT a."Id",
                       (SELECT k."Id"
                        FROM content_creator.gcc_artifacts AS k
                        WHERE k.project_id = a.project_id
                          AND k."ParentArtifactId" IS NULL
                          AND lower(btrim(k."Type")) = lower(btrim(a."Type"))
                          AND lower(btrim(k."Name")) = lower(btrim(a."Name"))
                        ORDER BY k."CreatedAtUtc" DESC, k."Id" DESC
                        LIMIT 1) AS kept_id
                FROM content_creator.gcc_artifacts AS a
                WHERE a.project_id IS NOT NULL
                  AND a."ParentArtifactId" IS NULL
                  AND a."Id" <> (
                    SELECT k."Id"
                    FROM content_creator.gcc_artifacts AS k
                    WHERE k.project_id = a.project_id
                      AND k."ParentArtifactId" IS NULL
                      AND lower(btrim(k."Type")) = lower(btrim(a."Type"))
                      AND lower(btrim(k."Name")) = lower(btrim(a."Name"))
                    ORDER BY k."CreatedAtUtc" DESC, k."Id" DESC
                    LIMIT 1);

                UPDATE content_creator.gcc_artifacts AS c
                SET "ParentArtifactId" = s.kept_id
                FROM superseded_pages AS s
                WHERE c."ParentArtifactId" = s."Id";

                DELETE FROM content_creator.gcc_approval_events
                WHERE "ArtifactVersionId" IN (
                    SELECT v."Id" FROM content_creator.gcc_artifact_versions AS v
                    WHERE v."ArtifactId" IN (SELECT "Id" FROM superseded_pages));

                DELETE FROM content_creator.gcc_version_evidence
                WHERE version_id IN (
                    SELECT v."Id" FROM content_creator.gcc_artifact_versions AS v
                    WHERE v."ArtifactId" IN (SELECT "Id" FROM superseded_pages));

                DELETE FROM content_creator.gcc_artifact_versions
                WHERE "ArtifactId" IN (SELECT "Id" FROM superseded_pages);

                DELETE FROM content_creator.gcc_artifacts
                WHERE "Id" IN (SELECT "Id" FROM superseded_pages);
                """);

            // 3. The one version a page has is its first.
            migrationBuilder.Sql("""
                UPDATE content_creator.gcc_artifact_versions
                SET "VersionNumber" = 1
                WHERE "VersionNumber" <> 1;
                """);

            // 4. One version per page.
            migrationBuilder.DropIndex(
                name: "ix_gcc_artifact_versions_artifact_id",
                schema: "content_creator",
                table: "gcc_artifact_versions");

            migrationBuilder.CreateIndex(
                name: "ux_gcc_artifact_versions_artifact_id",
                schema: "content_creator",
                table: "gcc_artifact_versions",
                column: "ArtifactId",
                unique: true);

            // 5. One page per type and name on a project, among the pages no other page derives from.
            migrationBuilder.Sql("""
                CREATE UNIQUE INDEX ux_gcc_artifacts_one_page_per_type_and_name
                ON content_creator.gcc_artifacts (project_id, lower(btrim("Type")), lower(btrim("Name")))
                WHERE "ParentArtifactId" IS NULL;
                """);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            // The deleted content is gone; only the constraints can be undone.
            migrationBuilder.Sql("""
                DROP INDEX IF EXISTS content_creator.ux_gcc_artifacts_one_page_per_type_and_name;
                """);

            migrationBuilder.DropIndex(
                name: "ux_gcc_artifact_versions_artifact_id",
                schema: "content_creator",
                table: "gcc_artifact_versions");

            migrationBuilder.CreateIndex(
                name: "ix_gcc_artifact_versions_artifact_id",
                schema: "content_creator",
                table: "gcc_artifact_versions",
                column: "ArtifactId");
        }
    }
}
