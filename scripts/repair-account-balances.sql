-- Fix-2 data repair: account balances drifted because expense/income edits and
-- deletes never compensated the balance (fixed in code as of Fix-2).
-- PostgreSQL. DO NOT run unattended — the repair step needs a human decision.
--
-- There is no "initial balance" column: an account's stored Balance mixes the
-- balance entered at creation with every (possibly drifted) adjustment since.
-- So this script cannot know the correct balance on its own.
--
-- Step 1 (read-only): report, per account, the stored balance, the net of all
-- transactions, and the initial balance the stored value implies. Review the
-- implied_initial_balance column: for accounts that were never edited/deleted
-- against, it equals the balance entered at creation; anything else has drift
-- baked in.

SELECT
    a."Id",
    a."Name",
    a."Balance"                                        AS stored_balance,
    COALESCE(i.total, 0) - COALESCE(e.total, 0)        AS transaction_net,
    a."Balance" - (COALESCE(i.total, 0) - COALESCE(e.total, 0))
                                                       AS implied_initial_balance
FROM "Accounts" a
LEFT JOIN (
    SELECT "AccountId", SUM("Amount") AS total
    FROM "Expenses"
    GROUP BY "AccountId"
) e ON e."AccountId" = a."Id"
LEFT JOIN (
    SELECT "AccountId", SUM("Amount") AS total
    FROM "Incomes"
    GROUP BY "AccountId"
) i ON i."AccountId" = a."Id"
ORDER BY a."Name";

-- Step 2 (mutating — run only after confirming each account's true initial
-- balance with the owner). Recomputes Balance = <initial> + incomes - expenses.
-- Replace the initial_balances VALUES list with the confirmed figures.
--
-- BEGIN;
--
-- WITH initial_balances ("AccountId", initial) AS (
--     VALUES
--         ('<account-guid-1>'::uuid, 0.00)
--       -- , ('<account-guid-2>'::uuid, 123.45)
-- ),
-- net AS (
--     SELECT a."Id",
--            COALESCE((SELECT SUM("Amount") FROM "Incomes"  WHERE "AccountId" = a."Id"), 0)
--          - COALESCE((SELECT SUM("Amount") FROM "Expenses" WHERE "AccountId" = a."Id"), 0) AS tx_net
--     FROM "Accounts" a
-- )
-- UPDATE "Accounts" a
-- SET "Balance" = ib.initial + n.tx_net
-- FROM initial_balances ib, net n
-- WHERE a."Id" = ib."AccountId" AND n."Id" = a."Id";
--
-- -- Verify with the Step 1 SELECT, then:
-- COMMIT;
