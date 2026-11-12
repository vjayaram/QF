-- Transactional outbox (hand-written; shared shape across services).
CREATE TABLE [dbo].[OutboxMessages]
(
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [Subject] NVARCHAR(128) NOT NULL,
    [Payload] NVARCHAR(MAX) NOT NULL,
    [OccurredAt] DATETIMEOFFSET(7) NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NULL,
    [Attempts] INT NOT NULL CONSTRAINT [DF_OutboxMessages_Attempts] DEFAULT (0),
    [LastError] NVARCHAR(2000) NULL,
    CONSTRAINT [PK_OutboxMessages] PRIMARY KEY CLUSTERED ([Id])
);
GO

-- The relay only scans unprocessed rows.
CREATE NONCLUSTERED INDEX [IX_OutboxMessages_Pending] ON [dbo].[OutboxMessages] ([OccurredAt])
    INCLUDE ([Attempts]) WHERE [ProcessedAt] IS NULL;
GO
