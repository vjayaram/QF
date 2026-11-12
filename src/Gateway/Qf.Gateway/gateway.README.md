`gateway.fgp` is a build artifact. CI composes it from the subgraphs (see `scripts/compose-gateway.ps1`)
and copies it into the gateway image. To compose locally:

    pwsh ./scripts/compose-gateway.ps1
