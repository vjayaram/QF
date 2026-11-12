 
AWSTemplateFormatVersion: "2010-09-09"CREATE OR REPLACE SEMANTIC VIEW vbp.semantic.claims_analysis
  TABLES (
    claims AS vbp.core.claim_lines            -- certified data product (denied/voided already excluded)
      PRIMARY KEY (claim_line_id)
      WITH SYNONYMS ('claims', 'claim lines')
      COMMENT = 'Adjudicated claim lines. Owner: Finance',
    providers AS vbp.core.providers
      PRIMARY KEY (npi)
      COMMENT = 'Provider directory'
  )
  RELATIONSHIPS (
    claims_to_providers AS claims (provider_npi) REFERENCES providers
  )
  FACTS (
    claims.paid_amount AS plan_paid
      COMMENT = 'Amount paid by the plan'
  )
  DIMENSIONS (
    claims.service_month AS DATE_TRUNC('month', date_of_service)
      COMMENT = 'Month of service (not paid date)',
    claims.benefit_type AS benefit_kind
      WITH SYNONYMS = ('service type'),
    providers.network_status AS IFF(in_network, 'In network', 'Out of network')
  )
  METRICS (
    claims.total_paid AS SUM(claims.paid_amount)
      COMMENT = 'Certified v3: total plan-paid amount',
    claims.claim_count AS COUNT(claim_line_id)
  )
  COMMENT = 'Certified claims metrics for BI and Cortex Analyst';


  SELECT * FROM SEMANTIC_VIEW(
  vbp.semantic.claims_analysis
  METRICS (claims.total_paid, claims.claim_count)
  DIMENSIONS (claims.service_month, providers.network_status)
);

Why it matters for AI-enabled BI

One definition across tools. Cortex Analyst generates SQL against these definitions instead of guessing joins over raw tables. Snowflake says this reduces hallucinated and conflicting answers.
Verified queries live in the view. The approved answers to common questions sit right next to the metric definitions.
Governed like any other object. It’s controlled with standard Snowflake access grants, so the same permissions apply to SQL, BI and AI.
Replaces YAML semantic models. It supersedes the older approach of Cortex Analyst YAML files kept in a stage. The model now lives in the database.
BI tool support. Sigma, Hex and Omni can read semantic views directly.

Caveats

Certification isn’t built in. Snowflake stores the definition, but you still need the process around it: metric owners, review, tests and versioning. In practice, keep the DDL in Git and deploy it through CI/CD with reconciliation tests. The owner and version can go in comments or tags.
Some features are in preview. As of the Snowflake engineering blog post, semantic SQL was in public preview, and routing mode and the generation assistant were in private preview. Check what’s generally available now.