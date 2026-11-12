-- Row-level security: the second tenant-isolation layer (the first is EF global query filters).
CREATE SCHEMA [security] AUTHORIZATION [dbo];
GO

CREATE FUNCTION [security].[fn_TenantPredicate] (@TenantId UNIQUEIDENTIFIER)
RETURNS TABLE
WITH SCHEMABINDING
AS
RETURN SELECT 1 AS [Allowed]
WHERE @TenantId = CAST(SESSION_CONTEXT(N'TenantId') AS UNIQUEIDENTIFIER);
GO

CREATE SECURITY POLICY [security].[TenantIsolationPolicy]
    ADD FILTER PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [dbo].[Appointments],
    ADD BLOCK PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [dbo].[Appointments] AFTER INSERT,
    ADD BLOCK PREDICATE [security].[fn_TenantPredicate]([TenantId]) ON [dbo].[Appointments] AFTER UPDATE
WITH (STATE = ON, SCHEMABINDING = ON);
GO
