using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace GeekRepository.Data.Migrations.ContentCreator
{
    /// <summary>
    /// gcc_creates gets a concurrency token, and the database needs nothing for it.
    /// </summary>
    /// <remarks>
    /// The token is Postgres's <c>xmin</c> system column, which every row already has and every write
    /// already changes. EF's generator emits <c>AddColumn("xmin")</c> for it, which Postgres refuses
    /// ("column name conflicts with a system column name"), so the body is deliberately empty. The
    /// migration exists to move the model snapshot forward, so the next generated migration does not
    /// emit that AddColumn again.
    /// </remarks>
    public partial class AddGccCreateRowVersion : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
        }
    }
}
