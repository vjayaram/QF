-- Idempotent consumer: ids of integration events already processed.
CREATE TABLE [dbo].[InboxMessages]
(
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [ProcessedAt] DATETIMEOFFSET(7) NOT NULL,
    CONSTRAINT [PK_InboxMessages] PRIMARY KEY CLUSTERED ([Id])
);
GO
