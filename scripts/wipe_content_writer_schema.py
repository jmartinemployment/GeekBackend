#!/usr/bin/env python3
"""Truncate all Content Writer rows in Postgres (content_writer schema)."""
import os
import sys
from urllib.parse import parse_qsl, urlparse

import psycopg2


def resolve_database_url() -> str:
    raw = (
        os.environ.get("CONTENT_WRITER_DATABASE_URL")
        or os.environ.get("DATABASE_URL")
        or os.environ.get("SITE_ANALYZER2_DATABASE_URL")
    )
    if not raw:
        sys.exit("No database URL in environment")

    parsed = urlparse(raw)
    query = [(key, value) for key, value in parse_qsl(parsed.query, keep_blank_values=True) if key != "search_path"]
    if "pooler.supabase.com" in (parsed.hostname or "") and parsed.port in (None, 5432):
        parsed = parsed._replace(netloc=parsed.netloc.replace(":5432", ":6543") if ":5432" in parsed.netloc else f"{parsed.hostname}:6543")
    cleaned = parsed._replace(query="&".join(f"{k}={v}" for k, v in query))
    return cleaned.geturl()


URL = resolve_database_url()

SQL = """
TRUNCATE TABLE
    content_writer."ContentFigures",
    content_writer."ProjectPublications",
    content_writer."GeneratedContents",
    content_writer."KeywordSources",
    content_writer."CrawledSites",
    content_writer."Projects"
RESTART IDENTITY CASCADE;
"""

with psycopg2.connect(URL) as conn:
    with conn.cursor() as cur:
        cur.execute(
            """
            SELECT table_name
            FROM information_schema.tables
            WHERE table_schema = 'content_writer'
            ORDER BY table_name
            """
        )
        tables = [row[0] for row in cur.fetchall()]
        print("content_writer tables:", ", ".join(tables) if tables else "(none)")

        for table in (
            "ContentFigures",
            "ProjectPublications",
            "GeneratedContents",
            "KeywordSources",
            "CrawledSites",
            "Projects",
        ):
            cur.execute(
                """
                SELECT COUNT(*)::int
                FROM information_schema.tables
                WHERE table_schema = 'content_writer' AND table_name = %s
                """,
                (table,),
            )
            if cur.fetchone()[0] == 0:
                continue
            cur.execute(f'SELECT COUNT(*)::int FROM content_writer."{table}"')
            before = cur.fetchone()[0]
            print(f"{table}: {before} rows before")

        cur.execute(SQL)
        conn.commit()
        print("Truncated content_writer project data.")
