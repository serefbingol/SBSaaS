using Microsoft.EntityFrameworkCore.Migrations;

namespace SBSaaS.Infrastructure.Migrations
{
    public partial class Initial : Migration
    {
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("CREATE SCHEMA IF NOT EXISTS audit;");

            migrationBuilder.Sql(@"
                CREATE TABLE IF NOT EXISTS audit.change_log (
                    id bigserial PRIMARY KEY,
                    tenant_id uuid NOT NULL,
                    table_name text NOT NULL,
                    key_values jsonb NOT NULL,
                    old_values jsonb,
                    new_values jsonb,
                    operation text NOT NULL,
                    user_id text,
                    utc_date timestamp without time zone NOT NULL DEFAULT (now() at time zone 'utc')
                );
            ");
        }

        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.Sql("DROP TABLE IF EXISTS audit.change_log;");
            migrationBuilder.Sql("DROP SCHEMA IF EXISTS audit;");
        }
    }
}