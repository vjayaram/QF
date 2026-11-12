/*
  Post-deployment (idempotent). Grants the service's user-assigned managed identity least-privilege
  access. No SQL logins or passwords exist in Azure: the server is Entra-only authentication.
  $(AppIdentityName) is supplied by the pipeline from Terraform outputs.
  Skipped on local SQL Server (EngineEdition 5 = Azure SQL Database).
*/
IF CAST(SERVERPROPERTY('EngineEdition') AS INT) = 5
BEGIN
    IF NOT EXISTS (SELECT 1 FROM sys.database_principals WHERE [name] = N'$(AppIdentityName)')
        EXEC (N'CREATE USER [$(AppIdentityName)] FROM EXTERNAL PROVIDER;');

    IF IS_ROLEMEMBER(N'db_datareader', N'$(AppIdentityName)') = 0
        EXEC (N'ALTER ROLE [db_datareader] ADD MEMBER [$(AppIdentityName)];');

    IF IS_ROLEMEMBER(N'db_datawriter', N'$(AppIdentityName)') = 0
        EXEC (N'ALTER ROLE [db_datawriter] ADD MEMBER [$(AppIdentityName)];');

    -- sp_getapplock (scheduling lock) and sp_set_session_context (RLS) need no extra grants.
END;
