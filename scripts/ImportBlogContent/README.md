# Blog content import

CLI to discover live content on www.geekatyourspot.com and import into `geek_blog` via GeekAPI.

## Discover URLs

```bash
cd GeekBackend/scripts/ImportBlogContent
dotnet run -- discover --base https://www.geekatyourspot.com --out manifest.json
```

Discovers `/blog/*` (BlogPosting), `/use-cases/*` and `/tools/*` (TechnicalArticle).

## Import

```bash
export GEEK_BACKEND_API_KEY=...
dotnet run -- import --manifest manifest.json --api https://api.geekatyourspot.com --dry-run
dotnet run -- import --manifest manifest.json --api https://api.geekatyourspot.com
# To update posts that already exist:
dotnet run -- import --manifest manifest.json --api https://api.geekatyourspot.com --replace
```

Slug format: `blog/accounting/my-post`, `use-cases/accounting/my-use-case`, `tools/marketing/hubspot-ai`.

## Idempotency

- **Default (no flags):** creates new posts; **skips** any slug+lang that already exists in `geek_blog`.
- **`--dry-run`:** parses live HTML and prints what would happen (`create`, `update`, or `skip`) without writing.
- **`--replace`:** updates existing posts in place with freshly scraped HTML. A preflight summary and warning are printed before any writes.

Always run `--dry-run` first. The preflight line reports how many entries are new vs already imported:

```
Preflight: 42 new, 16 already in geek_blog (58 total).
```
