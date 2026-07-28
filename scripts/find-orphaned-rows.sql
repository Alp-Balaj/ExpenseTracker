-- Suggestion-9 pre-flight: run this BEFORE applying the
-- UserIndexesAndForeignKeys migration.
--
-- That migration turns "UserId" into a real foreign key to "AspNetUsers" on
-- every user-owned table. Postgres validates an added FK against the rows
-- already in the table, so a single row whose "UserId" no longer matches a user
-- fails the whole migration with
--
--     ERROR: insert or update on table "<table>" violates foreign key
--            constraint "FK_<table>_AspNetUsers_UserId"
--
-- Orphans are possible because, until this migration, nothing tied the two
-- together: a user deleted straight from the database left every row they ever
-- created behind. Step 1 finds them, step 2 removes them.
--
-- Step 1 (read-only): how many orphaned rows each table holds. All zeros means
-- the migration will apply cleanly.

SELECT 'Accounts'             AS table_name, COUNT(*) AS orphaned_rows FROM "Accounts"             t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'Budgets',             COUNT(*) FROM "Budgets"             t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'Categories',          COUNT(*) FROM "Categories"          t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'Currencies',          COUNT(*) FROM "Currencies"          t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'Expenses',            COUNT(*) FROM "Expenses"            t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'FutureExpenses',      COUNT(*) FROM "FutureExpenses"      t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'Incomes',             COUNT(*) FROM "Incomes"             t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'RefreshTokens',       COUNT(*) FROM "RefreshTokens"       t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'SavingContributions', COUNT(*) FROM "SavingContributions" t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'SavingGoals',         COUNT(*) FROM "SavingGoals"         t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
UNION ALL SELECT 'Transfers',           COUNT(*) FROM "Transfers"           t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId")
ORDER BY table_name;

-- Step 2 (mutating — only if step 1 reported non-zero counts). These rows
-- belong to nobody: no login can reach them and no report counts them. Deleting
-- them is what the migration's cascade would have done had it existed.
--
-- Order matters: children before the rows they reference, because the
-- table-to-table foreign keys are RESTRICT.
--
-- BEGIN;
--
-- DELETE FROM "Transfers"           t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "SavingContributions" t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "Expenses"            t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "Incomes"             t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "FutureExpenses"      t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "SavingGoals"         t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "Budgets"             t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "Accounts"            t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "Currencies"          t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "Categories"          t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
-- DELETE FROM "RefreshTokens"       t WHERE NOT EXISTS (SELECT 1 FROM "AspNetUsers" u WHERE u."Id" = t."UserId");
--
-- -- Re-run step 1: every count must be 0, then:
-- COMMIT;
