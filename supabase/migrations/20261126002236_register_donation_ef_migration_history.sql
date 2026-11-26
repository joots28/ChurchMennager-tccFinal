-- Mantém o histórico do EF Core alinhado com a alteração aplicada pelo Supabase MCP.
INSERT INTO public."__EFMigrationsHistory" ("MigrationId", "ProductVersion")
VALUES ('20261126002130_AddDonationPaymentMethodAndTransactionGuard', '8.0.32')
ON CONFLICT ("MigrationId") DO NOTHING;
