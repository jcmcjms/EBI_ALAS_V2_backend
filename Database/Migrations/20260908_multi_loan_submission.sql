BEGIN TRANSACTION;
GO

DECLARE @var0 sysname;
SELECT @var0 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutstandingLoans]') AND [c].[name] = N'Balance');
IF @var0 IS NOT NULL EXEC(N'ALTER TABLE [OutstandingLoans] DROP CONSTRAINT [' + @var0 + '];');
ALTER TABLE [OutstandingLoans] DROP COLUMN [Balance];
GO

DECLARE @var1 sysname;
SELECT @var1 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutstandingLoans]') AND [c].[name] = N'CreditorName');
IF @var1 IS NOT NULL EXEC(N'ALTER TABLE [OutstandingLoans] DROP CONSTRAINT [' + @var1 + '];');
ALTER TABLE [OutstandingLoans] DROP COLUMN [CreditorName];
GO

DECLARE @var2 sysname;
SELECT @var2 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[OutstandingLoans]') AND [c].[name] = N'MonthlyPayment');
IF @var2 IS NOT NULL EXEC(N'ALTER TABLE [OutstandingLoans] DROP CONSTRAINT [' + @var2 + '];');
ALTER TABLE [OutstandingLoans] DROP COLUMN [MonthlyPayment];
GO

DECLARE @var3 sysname;
SELECT @var3 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[BuyOuts]') AND [c].[name] = N'MonthlyAmortization');
IF @var3 IS NOT NULL EXEC(N'ALTER TABLE [BuyOuts] DROP CONSTRAINT [' + @var3 + '];');
ALTER TABLE [BuyOuts] DROP COLUMN [MonthlyAmortization];
GO

EXEC sp_rename N'[BuyOuts].[CreditorName]', N'Name', N'COLUMN';
GO

EXEC sp_rename N'[BuyOuts].[Amount]', N'OutstandingBalance', N'COLUMN';
GO

ALTER TABLE [OutstandingLoans] ADD [Amortization] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [OutstandingLoans] ADD [DateGranted] date NULL;
GO

ALTER TABLE [OutstandingLoans] ADD [DateMaturity] date NULL;
GO

ALTER TABLE [OutstandingLoans] ADD [OutstandingBalance] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [OutstandingLoans] ADD [Pn] nvarchar(50) NOT NULL DEFAULT N'';
GO

ALTER TABLE [OutstandingLoans] ADD [PrincipalBalance] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [OutstandingLoans] ADD [ProductWithDescription] nvarchar(200) NULL;
GO

ALTER TABLE [OutstandingLoans] ADD [Status] nvarchar(100) NOT NULL DEFAULT N'';
GO

DECLARE @var4 sysname;
SELECT @var4 = [d].[name]
FROM [sys].[default_constraints] [d]
INNER JOIN [sys].[columns] [c] ON [d].[parent_column_id] = [c].[column_id] AND [d].[parent_object_id] = [c].[object_id]
WHERE ([d].[parent_object_id] = OBJECT_ID(N'[LoanApplications]') AND [c].[name] = N'InterestRate');
IF @var4 IS NOT NULL EXEC(N'ALTER TABLE [LoanApplications] DROP CONSTRAINT [' + @var4 + '];');
ALTER TABLE [LoanApplications] ALTER COLUMN [InterestRate] decimal(9,6) NOT NULL;
GO

ALTER TABLE [LoanApplications] ADD [Address] nvarchar(500) NULL;
GO

ALTER TABLE [LoanApplications] ADD [AoRecommendation] nvarchar(1000) NULL;
GO

ALTER TABLE [LoanApplications] ADD [ApplicationGroupNo] nvarchar(30) NOT NULL DEFAULT N'';
GO

ALTER TABLE [LoanApplications] ADD [Birthdate] date NULL;
GO

ALTER TABLE [LoanApplications] ADD [CreationTypeCode] int NULL;
GO

ALTER TABLE [LoanApplications] ADD [CreationTypeLabel] nvarchar(50) NULL;
GO

ALTER TABLE [LoanApplications] ADD [DeviationDetails] nvarchar(max) NOT NULL DEFAULT N'';
GO

ALTER TABLE [LoanApplications] ADD [DeviationJustifications] nvarchar(max) NOT NULL DEFAULT N'';
GO

ALTER TABLE [LoanApplications] ADD [DivisionCode] nvarchar(10) NULL;
GO

ALTER TABLE [LoanApplications] ADD [DocStamps] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [LoanApplications] ADD [FeeDeviationJustification] nvarchar(1000) NULL;
GO

ALTER TABLE [LoanApplications] ADD [HasDeviations] bit NOT NULL DEFAULT CAST(0 AS bit);
GO

ALTER TABLE [LoanApplications] ADD [Insurance] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [LoanApplications] ADD [Lai] nvarchar(30) NULL;
GO

ALTER TABLE [LoanApplications] ADD [LengthOfService] nvarchar(50) NULL;
GO

ALTER TABLE [LoanApplications] ADD [LoanNo] nvarchar(50) NOT NULL DEFAULT N'';
GO

ALTER TABLE [LoanApplications] ADD [MisAgency] nvarchar(200) NULL;
GO

ALTER TABLE [LoanApplications] ADD [NotarialFee] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [LoanApplications] ADD [NthpDate] date NULL;
GO

ALTER TABLE [LoanApplications] ADD [OtherRemarks] nvarchar(1000) NULL;
GO

ALTER TABLE [LoanApplications] ADD [PreLoanFormNumber] nvarchar(50) NULL;
GO

ALTER TABLE [LoanApplications] ADD [PreLoanId] int NULL;
GO

ALTER TABLE [LoanApplications] ADD [ProductCode] nvarchar(20) NOT NULL DEFAULT N'';
GO

ALTER TABLE [LoanApplications] ADD [Region] nvarchar(10) NULL;
GO

ALTER TABLE [LoanApplications] ADD [Remarks] nvarchar(1000) NULL;
GO

ALTER TABLE [LoanApplications] ADD [RequestingOfficer] nvarchar(150) NULL;
GO

ALTER TABLE [LoanApplications] ADD [StandardDocStamps] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [LoanApplications] ADD [StandardInsurance] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [LoanApplications] ADD [StandardNotarialFee] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [LoanApplications] ADD [StationCode] nvarchar(10) NULL;
GO

ALTER TABLE [LoanApplications] ADD [Suffix] nvarchar(10) NULL;
GO

ALTER TABLE [LoanApplications] ADD [VerificationFindings] nvarchar(2000) NULL;
GO

ALTER TABLE [BuyOuts] ADD [Amortization] decimal(18,2) NOT NULL DEFAULT 0.0;
GO

ALTER TABLE [BuyOuts] ADD [Pn] nvarchar(50) NOT NULL DEFAULT N'';
GO

CREATE TABLE [EbiReloans] (
    [Id] int NOT NULL IDENTITY,
    [LoanApplicationId] int NOT NULL,
    [Pn] nvarchar(50) NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [ExistingDeduction] decimal(18,2) NOT NULL,
    [OutstandingBalance] decimal(18,2) NOT NULL,
    [PayToClose] decimal(18,2) NOT NULL,
    CONSTRAINT [PK_EbiReloans] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_EbiReloans_LoanApplications_LoanApplicationId] FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplications] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [IncomingLoans] (
    [Id] int NOT NULL IDENTITY,
    [LoanApplicationId] int NOT NULL,
    [Name] nvarchar(200) NOT NULL,
    [Deductions] decimal(18,2) NOT NULL,
    [Remarks] nvarchar(500) NOT NULL,
    CONSTRAINT [PK_IncomingLoans] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_IncomingLoans_LoanApplications_LoanApplicationId] FOREIGN KEY ([LoanApplicationId]) REFERENCES [LoanApplications] ([Id]) ON DELETE CASCADE
);
GO

CREATE TABLE [LoanSubmissionIdempotencies] (
    [Id] int NOT NULL IDENTITY,
    [IdempotencyKey] uniqueidentifier NOT NULL,
    [UserId] int NOT NULL,
    [ResponseJson] nvarchar(max) NOT NULL,
    [CreatedAt] datetime2 NOT NULL,
    CONSTRAINT [PK_LoanSubmissionIdempotencies] PRIMARY KEY ([Id]),
    CONSTRAINT [FK_LoanSubmissionIdempotencies_Users_UserId] FOREIGN KEY ([UserId]) REFERENCES [Users] ([Id]) ON DELETE NO ACTION
);
GO

CREATE INDEX [IX_LoanApplications_ApplicationGroupNo] ON [LoanApplications] ([ApplicationGroupNo]);
GO

CREATE INDEX [IX_LoanApplications_LoanNo] ON [LoanApplications] ([LoanNo]);
GO

CREATE INDEX [IX_EbiReloans_LoanApplicationId] ON [EbiReloans] ([LoanApplicationId]);
GO

CREATE INDEX [IX_IncomingLoans_LoanApplicationId] ON [IncomingLoans] ([LoanApplicationId]);
GO

CREATE INDEX [IX_LoanSubmissionIdempotencies_UserId] ON [LoanSubmissionIdempotencies] ([UserId]);
GO

CREATE UNIQUE INDEX [IX_LoanSubmissionIdempotency_Key_User] ON [LoanSubmissionIdempotencies] ([IdempotencyKey], [UserId]);
GO

INSERT INTO [__EFMigrationsHistory] ([MigrationId], [ProductVersion])
VALUES (N'20260908092815_MultiLoanSubmission', N'8.0.0');
GO

COMMIT;
GO

