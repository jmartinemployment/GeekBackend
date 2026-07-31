using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentWriterV4
{
    /// <inheritdoc />
    public partial class InitialContentWriterV4 : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.EnsureSchema(
                name: "content_writer_v4");

            migrationBuilder.CreateTable(
                name: "brand_voices",
                schema: "content_writer_v4",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Tone = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    sample_text = table.Column<string>(type: "text", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_brand_voices", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "provider_models",
                schema: "content_writer_v4",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    input_cost_per_1k = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    output_cost_per_1k = table.Column<decimal>(type: "numeric(10,6)", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false, defaultValue: true),
                    effective_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_provider_models", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "templates",
                schema: "content_writer_v4",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    Slug = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Name = table.Column<string>(type: "character varying(256)", maxLength: 256, nullable: false),
                    Description = table.Column<string>(type: "text", nullable: false),
                    Category = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    Icon = table.Column<string>(type: "character varying(16)", maxLength: 16, nullable: false),
                    input_schema = table.Column<string>(type: "jsonb", nullable: false),
                    system_prompt = table.Column<string>(type: "text", nullable: false),
                    user_prompt_template = table.Column<string>(type: "text", nullable: false),
                    is_active = table.Column<bool>(type: "boolean", nullable: false),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_templates", x => x.Id);
                });

            migrationBuilder.CreateTable(
                name: "documents",
                schema: "content_writer_v4",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    owner_id = table.Column<Guid>(type: "uuid", nullable: false),
                    template_id = table.Column<Guid>(type: "uuid", nullable: true),
                    brand_voice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    Title = table.Column<string>(type: "character varying(512)", maxLength: 512, nullable: false),
                    inputs = table.Column<string>(type: "jsonb", nullable: false, defaultValue: "{}"),
                    content = table.Column<string>(type: "text", nullable: false, defaultValue: ""),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false),
                    updated_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_documents", x => x.Id);
                    table.ForeignKey(
                        name: "FK_documents_brand_voices_brand_voice_id",
                        column: x => x.brand_voice_id,
                        principalSchema: "content_writer_v4",
                        principalTable: "brand_voices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                    table.ForeignKey(
                        name: "FK_documents_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "content_writer_v4",
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateTable(
                name: "generations",
                schema: "content_writer_v4",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    document_id = table.Column<Guid>(type: "uuid", nullable: true),
                    template_id = table.Column<Guid>(type: "uuid", nullable: false),
                    brand_voice_id = table.Column<Guid>(type: "uuid", nullable: true),
                    Provider = table.Column<string>(type: "character varying(64)", maxLength: 64, nullable: false),
                    Model = table.Column<string>(type: "character varying(128)", maxLength: 128, nullable: false),
                    inputs = table.Column<string>(type: "jsonb", nullable: false),
                    Output = table.Column<string>(type: "text", nullable: false),
                    input_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    output_tokens = table.Column<int>(type: "integer", nullable: false, defaultValue: 0),
                    cost_usd = table.Column<decimal>(type: "numeric(10,6)", nullable: false, defaultValue: 0m),
                    created_at = table.Column<DateTime>(type: "timestamp with time zone", nullable: false)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_generations", x => x.Id);
                    table.ForeignKey(
                        name: "FK_generations_brand_voices_brand_voice_id",
                        column: x => x.brand_voice_id,
                        principalSchema: "content_writer_v4",
                        principalTable: "brand_voices",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_generations_documents_document_id",
                        column: x => x.document_id,
                        principalSchema: "content_writer_v4",
                        principalTable: "documents",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.SetNull);
                    table.ForeignKey(
                        name: "FK_generations_templates_template_id",
                        column: x => x.template_id,
                        principalSchema: "content_writer_v4",
                        principalTable: "templates",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Restrict);
                });

            migrationBuilder.CreateIndex(
                name: "IX_brand_voices_owner_id",
                schema: "content_writer_v4",
                table: "brand_voices",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_brand_voice_id",
                schema: "content_writer_v4",
                table: "documents",
                column: "brand_voice_id");

            migrationBuilder.CreateIndex(
                name: "ix_documents_owner_id",
                schema: "content_writer_v4",
                table: "documents",
                column: "owner_id");

            migrationBuilder.CreateIndex(
                name: "IX_documents_template_id",
                schema: "content_writer_v4",
                table: "documents",
                column: "template_id");

            migrationBuilder.CreateIndex(
                name: "IX_generations_brand_voice_id",
                schema: "content_writer_v4",
                table: "generations",
                column: "brand_voice_id");

            migrationBuilder.CreateIndex(
                name: "ix_generations_document_id",
                schema: "content_writer_v4",
                table: "generations",
                column: "document_id");

            migrationBuilder.CreateIndex(
                name: "ix_generations_tmpl_created",
                schema: "content_writer_v4",
                table: "generations",
                columns: new[] { "template_id", "created_at" });

            migrationBuilder.CreateIndex(
                name: "IX_provider_models_Provider_Model",
                schema: "content_writer_v4",
                table: "provider_models",
                columns: new[] { "Provider", "Model" },
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_templates_Slug",
                schema: "content_writer_v4",
                table: "templates",
                column: "Slug",
                unique: true);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropTable(
                name: "generations",
                schema: "content_writer_v4");

            migrationBuilder.DropTable(
                name: "provider_models",
                schema: "content_writer_v4");

            migrationBuilder.DropTable(
                name: "documents",
                schema: "content_writer_v4");

            migrationBuilder.DropTable(
                name: "brand_voices",
                schema: "content_writer_v4");

            migrationBuilder.DropTable(
                name: "templates",
                schema: "content_writer_v4");
        }
    }
}
