using System;
using Microsoft.EntityFrameworkCore.Migrations;

#nullable disable

namespace SBSaaS.Infrastructure.Migrations
{
    /// <inheritdoc />
    public partial class AddBillingEntitiesAndRelationships : Migration
    {
        /// <inheritdoc />
        protected override void Up(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropPrimaryKey(
                name: "PK_Subscriptions",
                table: "Subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_SubscriptionPlans",
                table: "SubscriptionPlans");

            migrationBuilder.RenameTable(
                name: "Subscriptions",
                newName: "subscriptions",
                newSchema: "billing");

            migrationBuilder.RenameTable(
                name: "SubscriptionPlans",
                newName: "plans",
                newSchema: "billing");

            migrationBuilder.RenameColumn(
                name: "StartUtc",
                schema: "billing",
                table: "subscriptions",
                newName: "BillingPeriodStart");

            migrationBuilder.RenameColumn(
                name: "EndUtc",
                schema: "billing",
                table: "subscriptions",
                newName: "CanceledAt");

            migrationBuilder.AddColumn<DateTime>(
                name: "BillingPeriodEnd",
                schema: "billing",
                table: "subscriptions",
                type: "timestamp with time zone",
                nullable: false,
                defaultValue: new DateTime(1, 1, 1, 0, 0, 0, 0, DateTimeKind.Unspecified));

            migrationBuilder.AddColumn<string>(
                name: "Status",
                schema: "billing",
                table: "subscriptions",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                schema: "billing",
                table: "plans",
                type: "numeric(18,2)",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                schema: "billing",
                table: "plans",
                type: "character varying(200)",
                maxLength: 200,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                schema: "billing",
                table: "plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                oldClrType: typeof(string),
                oldType: "text");

            migrationBuilder.AddColumn<string>(
                name: "BillingCycle",
                schema: "billing",
                table: "plans",
                type: "character varying(50)",
                maxLength: 50,
                nullable: false,
                defaultValue: "");

            migrationBuilder.AddColumn<string>(
                name: "Description",
                schema: "billing",
                table: "plans",
                type: "text",
                nullable: true);

            migrationBuilder.AddPrimaryKey(
                name: "PK_subscriptions",
                schema: "billing",
                table: "subscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_plans",
                schema: "billing",
                table: "plans",
                column: "Id");

            migrationBuilder.CreateTable(
                name: "plan_features",
                schema: "billing",
                columns: table => new
                {
                    Id = table.Column<Guid>(type: "uuid", nullable: false),
                    PlanId = table.Column<Guid>(type: "uuid", nullable: false),
                    FeatureKey = table.Column<string>(type: "character varying(100)", maxLength: 100, nullable: false),
                    LimitValue = table.Column<long>(type: "bigint", nullable: false),
                    OveragePrice = table.Column<decimal>(type: "numeric(18,4)", nullable: true)
                },
                constraints: table =>
                {
                    table.PrimaryKey("PK_plan_features", x => x.Id);
                    table.ForeignKey(
                        name: "FK_plan_features_plans_PlanId",
                        column: x => x.PlanId,
                        principalSchema: "billing",
                        principalTable: "plans",
                        principalColumn: "Id",
                        onDelete: ReferentialAction.Cascade);
                });

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_PlanId",
                schema: "billing",
                table: "subscriptions",
                column: "PlanId");

            migrationBuilder.CreateIndex(
                name: "IX_subscriptions_TenantId",
                schema: "billing",
                table: "subscriptions",
                column: "TenantId");

            migrationBuilder.CreateIndex(
                name: "IX_plans_Code",
                schema: "billing",
                table: "plans",
                column: "Code",
                unique: true);

            migrationBuilder.CreateIndex(
                name: "IX_plan_features_PlanId_FeatureKey",
                schema: "billing",
                table: "plan_features",
                columns: new[] { "PlanId", "FeatureKey" },
                unique: true);

            migrationBuilder.AddForeignKey(
                name: "FK_subscriptions_plans_PlanId",
                schema: "billing",
                table: "subscriptions",
                column: "PlanId",
                principalSchema: "billing",
                principalTable: "plans",
                principalColumn: "Id",
                onDelete: ReferentialAction.Restrict);
        }

        /// <inheritdoc />
        protected override void Down(MigrationBuilder migrationBuilder)
        {
            migrationBuilder.DropForeignKey(
                name: "FK_subscriptions_plans_PlanId",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropTable(
                name: "plan_features",
                schema: "billing");

            migrationBuilder.DropPrimaryKey(
                name: "PK_subscriptions",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_PlanId",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropIndex(
                name: "IX_subscriptions_TenantId",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropPrimaryKey(
                name: "PK_plans",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropIndex(
                name: "IX_plans_Code",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "BillingPeriodEnd",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "Status",
                schema: "billing",
                table: "subscriptions");

            migrationBuilder.DropColumn(
                name: "BillingCycle",
                schema: "billing",
                table: "plans");

            migrationBuilder.DropColumn(
                name: "Description",
                schema: "billing",
                table: "plans");

            migrationBuilder.RenameTable(
                name: "subscriptions",
                schema: "billing",
                newName: "Subscriptions");

            migrationBuilder.RenameTable(
                name: "plans",
                schema: "billing",
                newName: "SubscriptionPlans");

            migrationBuilder.RenameColumn(
                name: "CanceledAt",
                table: "Subscriptions",
                newName: "EndUtc");

            migrationBuilder.RenameColumn(
                name: "BillingPeriodStart",
                table: "Subscriptions",
                newName: "StartUtc");

            migrationBuilder.AlterColumn<decimal>(
                name: "Price",
                table: "SubscriptionPlans",
                type: "numeric",
                nullable: false,
                oldClrType: typeof(decimal),
                oldType: "numeric(18,2)");

            migrationBuilder.AlterColumn<string>(
                name: "Name",
                table: "SubscriptionPlans",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(200)",
                oldMaxLength: 200);

            migrationBuilder.AlterColumn<string>(
                name: "Code",
                table: "SubscriptionPlans",
                type: "text",
                nullable: false,
                oldClrType: typeof(string),
                oldType: "character varying(50)",
                oldMaxLength: 50);

            migrationBuilder.AddPrimaryKey(
                name: "PK_Subscriptions",
                table: "Subscriptions",
                column: "Id");

            migrationBuilder.AddPrimaryKey(
                name: "PK_SubscriptionPlans",
                table: "SubscriptionPlans",
                column: "Id");
        }
    }
}
