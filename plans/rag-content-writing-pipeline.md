# RAG generate (GeekBackend scope)

Status: **Phase C + F + D consumers shipped**; **citeable generate** proxies to Rag `POST /v1/generate` (fallback one-shot).

Sibling: [Geek-Crawler-v2/plans/citeable-rag-output.md](../../Geek-Crawler-v2/plans/citeable-rag-output.md).

## Soft-disable

- Off when `GEEK_CRAWLER_RAG_URL` unset
- Or `GEEK_RAG_GENERATE_ENABLED=false`
- Graph: `GEEK_RAG_GRAPH_ENABLED=false` to soft-disable (default ON)
- Ad-template index: `GEEK_RAG_AD_TEMPLATES_ENABLED=false` to soft-disable (default ON)
- Citeable Rag generate: `GEEK_RAG_CITEABLE_GENERATE_ENABLED=false` forces GeekAPI one-shot (default ON)

## Endpoints

| Method | Path | Purpose |
|--------|------|---------|
| GET | `/api/rag/status` | Soft-detect + models + feature flags (`citeableGenerateAvailable`) |
| POST | `/api/rag/generate` | Prefer Rag multi-step citeable draft; fallback one-shot |
| POST | `/api/rag/templates` | Upsert ad templates → Rag `/v1/templates/index` |

## Citeable path

1. Pick latest partner + competitor runs
2. `IGeekCrawlerRagClient.GenerateAsync` → Rag `/v1/generate`
3. Return `content` + `citations[{url,quote,pageId,…}]` + sources
4. On Rag failure: warn and use legacy query + OpenAI one-shot

Research merge (`GccV2GeekCrawlerResearchResolver`) passes `preferParent: true` and maps `pageId` on quoteables.

HttpClient timeout for Rag is **6 minutes** (o3 citeable generate).

## Success criteria

- [x] Phase C generate + status + entities
- [x] Phase F: long-form o1/o3 routing
- [x] Phase D consumers: graph themes + ad-template index query/upsert
- [x] Citeable proxy + citations DTO + preferParent research
