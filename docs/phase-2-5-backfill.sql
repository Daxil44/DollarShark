/*
  Run only after applying the Phase 2.5 migration and registering the intended
  initial DollarShark user. Replace the email below with that user's exact email.
  This assigns only currently unowned legacy records; it never overwrites an owner.
*/
SET XACT_ABORT ON;
DECLARE @InitialUserEmail nvarchar(256) = N'REPLACE-WITH-INITIAL-USER-EMAIL';
DECLARE @OwnerId nvarchar(450);

SELECT @OwnerId = Id
FROM dbo.AspNetUsers
WHERE NormalizedEmail = UPPER(@InitialUserEmail);

IF @OwnerId IS NULL
    THROW 51000, 'No DollarShark user matches the selected email. No data was changed.', 1;

BEGIN TRANSACTION;

UPDATE dbo.FinancialAccounts SET UserId = @OwnerId WHERE UserId IS NULL;
DECLARE @AccountsAssigned int = @@ROWCOUNT;

UPDATE dbo.Categories SET UserId = @OwnerId WHERE UserId IS NULL AND IsSystem = 0;
DECLARE @CategoriesAssigned int = @@ROWCOUNT;

UPDATE dbo.Transactions SET UserId = @OwnerId WHERE UserId IS NULL;
DECLARE @TransactionsAssigned int = @@ROWCOUNT;

COMMIT TRANSACTION;

SELECT @AccountsAssigned AS AccountsAssigned,
       @CategoriesAssigned AS PrivateCategoriesAssigned,
       @TransactionsAssigned AS TransactionsAssigned,
       @InitialUserEmail AS AssignedTo;
