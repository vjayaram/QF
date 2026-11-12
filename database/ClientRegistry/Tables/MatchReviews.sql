-- Probable duplicates from legacy imports, awaiting a data steward.
CREATE TABLE [dbo].[MatchReviews]
(
    [Id] UNIQUEIDENTIFIER NOT NULL,
    [TenantId] UNIQUEIDENTIFIER NOT NULL,
    [SourceSystem] NVARCHAR(32) NOT NULL,
    [ExternalId] NVARCHAR(64) NOT NULL,
    [CandidateClientIds] NVARCHAR(2000) NOT NULL,
    [CreatedAt] DATETIMEOFFSET(7) NOT NULL,
    [ResolvedAt] DATETIMEOFFSET(7) NULL,
    CONSTRAINT [PK_MatchReviews] PRIMARY KEY CLUSTERED ([Id])
);
GO

CREATE NONCLUSTERED INDEX [IX_MatchReviews_TenantId_Open] ON [dbo].[MatchReviews] ([TenantId], [CreatedAt]) WHERE [ResolvedAt] IS NULL;
GO
