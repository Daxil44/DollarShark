-- DollarShark demo data (PostgreSQL).
-- Fills ONE existing user with ~3 months of realistic Canadian test data: 3 accounts, payroll, rent,
-- groceries, subscriptions, linked transfers. Safe to rerun: does nothing if the demo accounts exist.
--
-- 1. Sign up on the site with the email below first (and confirm it).
-- 2. Change the email on the next line if needed.
-- 3. Run:  docker compose exec -T db psql -U dollarshark -d dollarshark -v ON_ERROR_STOP=1 < docs/demo-data.sql

\set demo_email 'daxilpatel127+test@gmail.com'

CREATE FUNCTION pg_temp.tx(p_user text, p_account uuid, p_category uuid, p_date date, p_description text,
                           p_amount double precision, p_type text, p_link uuid DEFAULT NULL, p_id uuid DEFAULT gen_random_uuid())
RETURNS uuid LANGUAGE sql AS $f$
    INSERT INTO "Transactions" ("Id", "UserId", "FinancialAccountId", "CategoryId", "TransactionDate", "Description",
                                "Amount", "TransactionType", "Currency", "CreatedAtUtc", "DuplicateWarningOverridden", "LinkedTransferTransactionId")
    VALUES (p_id, p_user, p_account, p_category, p_date, p_description, round(p_amount::numeric, 2), p_type, 'CAD', now(), false, p_link)
    RETURNING "Id";
$f$;

SELECT set_config('demo.email', :'demo_email', false);

DO $$
DECLARE
    uid text;
    chq uuid := gen_random_uuid();
    sav uuid := gen_random_uuid();
    visa uuid := gen_random_uuid();
    start_date date := (date_trunc('month', current_date) - interval '3 months')::date;
    base date;
    m int;
    w int;
    a uuid;
    b uuid;
    groceries uuid := '10000000-0000-0000-0000-000000000001';
    restaurants uuid := '10000000-0000-0000-0000-000000000002';
    transport uuid := '10000000-0000-0000-0000-000000000003';
    housing uuid := '10000000-0000-0000-0000-000000000004';
    utilities uuid := '10000000-0000-0000-0000-000000000005';
    shopping uuid := '10000000-0000-0000-0000-000000000006';
    subscriptions uuid := '10000000-0000-0000-0000-000000000007';
    health uuid := '10000000-0000-0000-0000-000000000008';
    salary uuid := '10000000-0000-0000-0000-000000000010';
    transfers uuid := '10000000-0000-0000-0000-000000000012';
    card_spend numeric;
BEGIN
    SELECT "Id" INTO uid FROM "AspNetUsers" WHERE "NormalizedEmail" = upper(current_setting('demo.email'));
    IF uid IS NULL THEN
        RAISE EXCEPTION 'No user with email %. Sign up on the site first.', current_setting('demo.email');
    END IF;
    IF EXISTS (SELECT 1 FROM "FinancialAccounts" WHERE "UserId" = uid AND "Name" = 'Demo Chequing') THEN
        RAISE NOTICE 'Demo data already exists for % - nothing added.', current_setting('demo.email');
        RETURN;
    END IF;

    INSERT INTO "FinancialAccounts" ("Id", "UserId", "Name", "Institution", "AccountType", "Currency", "OpeningBalance", "OpeningBalanceDate", "LastFourDigits", "IsActive", "CreatedAtUtc") VALUES
        (chq,  uid, 'Demo Chequing', 'Scotiabank', 'Chequing',    'CAD', 2850.00, start_date - 1, '4821', true, now()),
        (sav,  uid, 'Demo Savings',  'Scotiabank', 'Savings',     'CAD', 6200.00, start_date - 1, '7713', true, now()),
        (visa, uid, 'Demo Visa',     'Scotiabank', 'Credit Card', 'CAD',  640.00, start_date - 1, '3095', true, now());

    FOR m IN 0..3 LOOP
        base := (start_date + make_interval(months => m))::date;
        card_spend := 0;

        -- Income and fixed bills (chequing)
        IF base + 0  <= current_date THEN PERFORM pg_temp.tx(uid, chq, salary,    base + 0,  'PAYROLL ACME CORP', 2450, 'Income'); END IF;
        IF base + 14 <= current_date THEN PERFORM pg_temp.tx(uid, chq, salary,    base + 14, 'PAYROLL ACME CORP', 2450, 'Income'); END IF;
        IF base + 1  <= current_date THEN PERFORM pg_temp.tx(uid, chq, housing,   base + 1,  'RENT - MAPLE APARTMENTS', -1650, 'Expense'); END IF;
        IF base + 2  <= current_date THEN PERFORM pg_temp.tx(uid, chq, transport, base + 2,  'PRESTO AUTOLOAD', -50, 'Expense'); END IF;
        IF base + 9  <= current_date THEN PERFORM pg_temp.tx(uid, chq, utilities, base + 9,  'TORONTO HYDRO', -(70 + random() * 45), 'Expense'); END IF;

        -- Card spending
        FOR w IN 0..3 LOOP
            IF base + 3 + w * 7 <= current_date THEN
                PERFORM pg_temp.tx(uid, visa, groceries, base + 3 + w * 7, CASE WHEN w % 2 = 0 THEN 'REAL CANADIAN SUPERSTORE #1577' ELSE 'FRESHCO #312' END, -(55 + random() * 110), 'Expense');
            END IF;
            IF base + 4 + w * 7 <= current_date THEN
                PERFORM pg_temp.tx(uid, visa, restaurants, base + 4 + w * 7, 'TIM HORTONS #4410', -(3 + random() * 9), 'Expense');
            END IF;
        END LOOP;
        IF base + 5  <= current_date THEN PERFORM pg_temp.tx(uid, visa, subscriptions, base + 5,  'NETFLIX.COM', -18.99, 'Expense'); END IF;
        IF base + 7  <= current_date THEN PERFORM pg_temp.tx(uid, visa, subscriptions, base + 7,  'SPOTIFY P2B3C4', -11.99, 'Expense'); END IF;
        IF base + 11 <= current_date THEN PERFORM pg_temp.tx(uid, visa, utilities,     base + 11, 'ROGERS ******4821', -(72 + random() * 10), 'Expense'); END IF;
        IF base + 16 <= current_date THEN PERFORM pg_temp.tx(uid, visa, transport,     base + 16, 'PETRO-CANADA 88231', -(45 + random() * 30), 'Expense'); END IF;
        IF base + 18 <= current_date THEN PERFORM pg_temp.tx(uid, visa, restaurants,   base + 18, 'UBER EATS TORONTO', -(24 + random() * 25), 'Expense'); END IF;
        IF base + 21 <= current_date THEN PERFORM pg_temp.tx(uid, visa, health,        base + 21, 'SHOPPERS DRUG MART #0812', -(12 + random() * 40), 'Expense'); END IF;
        -- Left uncategorized on purpose: try "Apply to uncategorized" on the Categories page.
        IF base + 19 <= current_date THEN PERFORM pg_temp.tx(uid, visa, NULL,          base + 19, 'AMAZON.CA*2K4LM8', -(20 + random() * 100), 'Expense'); END IF;
        IF base + 23 <= current_date THEN PERFORM pg_temp.tx(uid, visa, NULL,          base + 23, 'STARBUCKS #2231', -(5 + random() * 6), 'Expense'); END IF;

        -- Linked transfer: chequing -> savings
        IF base + 15 <= current_date THEN
            a := gen_random_uuid(); b := gen_random_uuid();
            PERFORM pg_temp.tx(uid, chq, transfers, base + 15, 'TRANSFER TO SAVINGS', -500, 'Transfer', NULL, a);
            PERFORM pg_temp.tx(uid, sav, transfers, base + 15, 'TRANSFER FROM CHEQUING', 500, 'Transfer', NULL, b);
            UPDATE "Transactions" SET "LinkedTransferTransactionId" = b WHERE "Id" = a;
            UPDATE "Transactions" SET "LinkedTransferTransactionId" = a WHERE "Id" = b;
        END IF;

        -- Linked transfer: pay the card from chequing (pays off this month's card spending)
        IF base + 25 <= current_date THEN
            SELECT COALESCE(-sum("Amount"), 0) INTO card_spend FROM "Transactions"
            WHERE "FinancialAccountId" = visa AND "TransactionType" = 'Expense' AND "TransactionDate" BETWEEN base AND base + 25;
            a := gen_random_uuid(); b := gen_random_uuid();
            PERFORM pg_temp.tx(uid, chq,  transfers, base + 25, 'PAYMENT TO SCOTIABANK VISA', -card_spend::double precision, 'Transfer', NULL, a);
            PERFORM pg_temp.tx(uid, visa, transfers, base + 25, 'PAYMENT RECEIVED - THANK YOU', card_spend::double precision, 'Transfer', NULL, b);
            UPDATE "Transactions" SET "LinkedTransferTransactionId" = b WHERE "Id" = a;
            UPDATE "Transactions" SET "LinkedTransferTransactionId" = a WHERE "Id" = b;
        END IF;
    END LOOP;

    RAISE NOTICE 'Demo data added for %: 3 accounts, % transactions.', current_setting('demo.email'),
        (SELECT count(*) FROM "Transactions" WHERE "UserId" = uid);
END $$;
