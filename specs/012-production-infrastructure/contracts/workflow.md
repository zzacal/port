# Contract: CI/CD workflow (012, FR-024)

File: `.github/workflows/ci.yml` (one file, owned by this feature).

```
name: ci
on:
  push:
    branches: ['**']
  workflow_dispatch:
permissions:
  contents: read
concurrency:
  group: ci-${{ github.ref }}
  cancel-in-progress: false
jobs:
  test:
    runs-on: ubuntu-24.04   # AMENDED at implement (R-03 fallback, decisions #46-47): was macos-latest + Colima
    steps:
      - actions/checkout@v5
      - actions/setup-dotnet@v5 with global-json-file: global.json
      - env GALLERY_FFMPEG=/usr/bin/ffmpeg, GALLERY_CWEBP=/usr/bin/cwebp
      - sudo apt-get install -y ffmpeg webp
      - dotnet build Gallery.slnx -c Release -warnaserror
      - dotnet test Gallery.slnx -c Release --no-build --blame-hang-timeout 180s
      - dotnet publish src/Gallery.Web -c Release -o publish --no-build
      - actions/upload-artifact@v4 name: web, path: publish, retention-days: 7
  deploy:
    if: github.ref == 'refs/heads/main'
    needs: test
    runs-on: ubuntu-latest
    permissions:
      id-token: write
      contents: read
    steps:
      - actions/download-artifact@v4 name: web, path: publish
      - azure/login@v2 with client-id: ${{ vars.AZURE_CLIENT_ID }}, tenant-id: ${{ vars.AZURE_TENANT_ID }},
        subscription-id: ${{ vars.AZURE_SUBSCRIPTION_ID }}
      - azure/webapps-deploy@v3 with app-name: portfolio-web, package: publish
      - smoke: curl -fsS -o /dev/null -w '%{http_code}' https://port.zacal.me/ must be 200 (retry 10x, 15 s apart)
```

Rules:
- The deployed package is the artifact the `test` job built and tested (no second build).
- No `secrets.*` reference anywhere in the file. A source-scan test asserts it
  (`tests/Gallery.Web.Tests/WorkflowGuardTests.cs`): the file has no `secrets.`, the deploy job
  has `needs: test` and `if: github.ref == 'refs/heads/main'`, `id-token: write` appears only in the
  deploy job, and the app name is `portfolio-web`.
- The deploy job declares NO `environment:`. The federated credential's subject is branch-based
  (`repo:zzacal/port:ref:refs/heads/main`); a job with `environment:` makes GitHub send an
  `environment:<name>` subject instead and the Azure login fails. The guard test asserts
  `environment:` is absent.
- Escalation (research R-03): if Colima cannot start on `macos-latest`, switch the test job to
  `ubuntu-latest` and add the encoder-path environment variables to the two fixtures. Do not skip,
  filter or delete any test.
- Action major versions are pinned by tag as above. Exact latest majors are confirmed when the
  workflow is written (task), and recorded in decisions.md.
