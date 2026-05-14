# Observability

## Three Pillars

| Pillar | What | Tool examples |
|---|---|---|
| Logs | What happened | Winston, Pino, Datadog |
| Metrics | How much / how fast | Prometheus, Datadog |
| Traces | Where time was spent | OpenTelemetry, Jaeger |

## Structured Logging

```typescript
// ✅ Structured — queryable, filterable
log.info("user.login", { userId, ip, duration_ms: 45, success: true });

// ❌ String interpolation — unsearchable
console.log(`User ${userId} logged in from ${ip} in 45ms`);
```

**Log Levels:**

| Level | When |
|---|---|
| `error` | Something broke — requires action |
| `warn` | Unexpected but handled |
| `info` | Key business events (login, purchase, deploy) |
| `debug` | Detailed flow for troubleshooting (not in prod) |

**Never log:** passwords, tokens, PII, full request bodies with secrets.

## Metrics to Track

**RED Method** (for services):
- **R**ate — requests per second
- **E**rrors — error rate (%)
- **D**uration — latency (p50, p95, p99)

**USE Method** (for infrastructure):
- **U**tilization — CPU/memory/disk %
- **S**aturation — queue depth, wait time
- **E**rrors — hardware/kernel errors

## OpenTelemetry (Node.js)

```typescript
import { trace } from "@opentelemetry/api";

const tracer = trace.getTracer("my-service");

async function processOrder(orderId: string) {
  const span = tracer.startSpan("processOrder");
  span.setAttribute("order.id", orderId);

  try {
    await doWork(orderId);
    span.setStatus({ code: SpanStatusCode.OK });
  } catch (err) {
    span.recordException(err as Error);
    span.setStatus({ code: SpanStatusCode.ERROR });
    throw err;
  } finally {
    span.end();
  }
}
```

## Alerting

- Alert on symptoms (high error rate, slow responses), not causes
- Every alert must have a runbook
- PagerDuty for P1/P2 (revenue impact, data loss risk)
- Slack for P3/P4 (degraded, not down)
- Alert fatigue = ignored alerts = missed incidents

## Health Endpoints

```typescript
// Liveness: is the process alive?
app.get("/health/live", (req, res) => res.json({ status: "ok" }));

// Readiness: can it serve traffic?
app.get("/health/ready", async (req, res) => {
  const dbOk = await db.ping().then(() => true).catch(() => false);
  if (!dbOk) return res.status(503).json({ status: "not ready", db: false });
  res.json({ status: "ok", db: true });
});
```

## Anti-Patterns

| Avoid | Why |
|---|---|
| `console.log` in production | No structure, no log level, no context |
| Logging every function entry/exit | Noise drowns signal |
| Metrics without dashboards | Data no one looks at |
| Alerting on causes (`cpu > 80%`) | Noisy, doesn't map to user impact |
| No correlation IDs | Can't trace a request across services |
