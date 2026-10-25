/*
  DollarShark demo data. Run after applying the Phase 2.5 migrations and
  registering the user who should own these rows. Safe to rerun: it adds only
  missing sample rows and never deletes or changes existing financial data.
*/
SET XACT_ABORT ON;
DECLARE @Email nvarchar(256) = N'REPLACE-WITH-YOUR-LOGIN-EMAIL';
DECLARE @UserId nvarchar(450);
DECLARE @AccountId uniqueidentifier;
DECLARE @CategoryId uniqueidentifier;
DECLARE @Today date = CAST(SYSUTCDATETIME() AS date);
DECLARE @AddedTransactions int = 0;

SELECT @UserId = Id
FROM dbo.AspNetUsers
WHERE NormalizedEmail = UPPER(LTRIM(RTRIM(@Email)));

IF @UserId IS NULL
    THROW 51000, 'No DollarShark user matches that email. Register or correct the email first.', 1;

BEGIN TRY
    BEGIN TRANSACTION;

    SELECT TOP (1) @AccountId = Id
    FROM dbo.FinancialAccounts
    WHERE UserId = @UserId
      AND Name = N'Sample Checking'
      AND Institution = N'DollarShark Demo Bank';

    IF @AccountId IS NULL
    BEGIN
        SET @AccountId = NEWID();
        INSERT INTO dbo.FinancialAccounts
            (Id, UserId, Name, Institution, AccountType, Currency, LastFourDigits, IsActive, CreatedAtUtc)
        VALUES
            (@AccountId, @UserId, N'Sample Checking', N'DollarShark Demo Bank', N'Chequing', N'CAD', N'4242', 1, SYSUTCDATETIME());
    END;

    SELECT TOP (1) @CategoryId = Id
    FROM dbo.Categories
    WHERE UserId = @UserId
      AND IsSystem = 0
      AND Name = N'Sample Category'
      AND ParentCategoryId IS NULL;

    IF @CategoryId IS NULL
    BEGIN
        SET @CategoryId = NEWID();
        INSERT INTO dbo.Categories (Id, UserId, IsSystem, Name, ParentCategoryId)
        VALUES (@CategoryId, @UserId, 0, N'Sample Category', NULL);
    END;

    DECLARE @Samples TABLE
    (
        TransactionDate date NOT NULL,
        Description nvarchar(300) NOT NULL,
        Amount decimal(19,4) NOT NULL,
        TransactionType nvarchar(16) NOT NULL
    );

    INSERT INTO @Samples (TransactionDate, Description, Amount, TransactionType)
    VALUES
        (DATEADD(day, -3, @Today), N'Sample groceries', -64.25, N'Expense'),
        (DATEADD(day, -2, @Today), N'Sample coffee', -5.50, N'Expense'),
        (DATEADD(day, -1, @Today), N'Sample paycheque', 1250.00, N'Income');

    INSERT INTO dbo.Transactions
        (Id, UserId, FinancialAccountId, CategoryId, TransactionDate, Description, Amount, TransactionType, Currency, CreatedAtUtc)
    SELECT NEWID(), @UserId, @AccountId, @CategoryId, s.TransactionDate, s.Description, s.Amount, s.TransactionType, N'CAD', SYSUTCDATETIME()
    FROM @Samples AS s
    WHERE NOT EXISTS
    (
        SELECT 1
        FROM dbo.Transactions AS existing
        WHERE existing.UserId = @UserId
          AND existing.FinancialAccountId = @AccountId
          AND existing.Description = s.Description
    );

    SET @AddedTransactions = @@ROWCOUNT;
    COMMIT TRANSACTION;

    SELECT @AccountId AS AccountId, @CategoryId AS CategoryId,
           @AddedTransactions AS TransactionsAdded, @Email AS DataOwnedBy;
END TRY
BEGIN CATCH
    IF @@TRANCOUNT > 0 ROLLBACK TRANSACTION;
    THROW;
END CATCH;
