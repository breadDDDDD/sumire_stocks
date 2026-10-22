# Health

## GET /api/health

Process and database status. Always returns 200; check `db`.

Response:

```json
{
  "status": "ok",
  "db": "ok",
  "dbError": null,
  "time": "2026-10-22T14:32:10+07:00",
  "version": "0.1.0"
}
```

| Field | Notes |
|---|---|
| `status` | `ok`, or `degraded` when the database is unreachable (the UI and non-DB features keep working) |
| `db` | `ok` / `error` |
| `dbError` | Short reason when `db` is `error`, e.g. `SUPABASE_CONNECTION_STRING is not set` or `NpgsqlException: …`. Never contains the connection string, host or user names. |
| `time` | Server time in WIB (ISO-8601 with `+07:00`) |
| `version` | App version |

The DB check opens one connection with a 5 s timeout.

## Conventions for all endpoints

- Base URL `http://localhost:5080`, routes under `/api`, JSON camelCase, enums as camelCase strings.
- Dates are ISO-8601 (`2026-10-22`), timestamps ISO-8601 with offset, shown in WIB.
- Prices are JSON numbers (decimal).
- Errors are RFC 7807 ProblemDetails (`application/problem+json`); unknown `/api/*` routes return 404 ProblemDetails.
