# AI Tools Monitor — Feature Research (Verified from Real Tools)

> Research date: 2026-07-30
> All features below are verified as implemented by real, shipping tools.
> Sources are cited per feature.

---

## Category 1: Cost/Spend Tracking

### Feature 1: Per-Model Cost Breakdown with Auto-Pricing

**(a) Inspired by:** CodeBurn (9k stars, github.com/getagentseal/codeburn), TokenTracker (1128 stars), ClaudeUsageTracker (116 stars)
**(b) Verified data source:** Claude Code session JSONL files at `~/.claude/projects/` contain `message.usage` objects with `input_tokens`, `output_tokens`, `cache_creation_input_tokens`, `cache_read_input_tokens`, and `message.model` (e.g., "claude-sonnet-5"). Codex stores similar data in `~/.codex/sessions/`. Pricing tables can be pulled from LiteLLM's `model_prices_and_context_window.json` (2200+ models) or hardcoded for known models.
**(c) Difficulty: MEDIUM.** Parse JSONL files (System.Text.Json), maintain a local pricing dictionary keyed by model name, multiply token counts by rates. SQLite DB for historical aggregation. ~2-3 days.

### Feature 2: Monthly/Weekly/Daily Spend Summaries with Date Range Selection

**(a) Inspired by:** CodeBurn (`codeburn overview`, `codeburn report -p 30days`), TokenTracker (daily/weekly/monthly views), ClaudeUsageTracker (monthly breakdown)
**(b) Verified data source:** Same JSONL session files. Each message has a `timestamp` field (ISO 8601). Aggregate by day/week/month using SQLite `GROUP BY date()`.
**(c) Difficulty: LOW.** SQLite queries with date grouping. Display in WinForms ListView or DataGridView. ~1-2 days.

### Feature 3: Budget Guard with Hard/Soft Caps

**(a) Inspired by:** CodeBurn Guard (`codeburn guard install` — soft cap $5, hard cap $15, checkpoint $3), OpenRouter Workspace Budgets (openrouter.ai/docs/features/workspace-budgets)
**(b) Verified data source:** Real-time cost accumulation in local DB. Hard cap stops session (via Claude Code hook integration); soft cap shows warning. Config in JSON.
**(c) Difficulty: MEDIUM.** Requires hooking into Claude Code's settings.json for guard installation. For the tray app, a simpler approach: monitor accumulated daily cost and show tray notification + color change when thresholds are crossed. ~2-3 days.

### Feature 4: Cost Forecasting from Trend Data

**(a) Inspired by:** CodeBurn (forecast based on 7-day trends predicting month-end spend), ai-token-monitor ("根据近 7 天趋势预测本月底花费" — predict month-end from 7-day trend)
**(b) Verified data source:** Historical daily cost from local DB. Linear regression or moving average over recent N days, extrapolated to month end.
**(c) Difficulty: LOW.** Simple linear extrapolation. Display as a forecasted month-end total. ~1 day.

### Feature 5: Multi-Currency Support with Exchange Rates

**(a) Inspired by:** CodeBurn (`codeburn currency GBP` — 162 ISO 4217 currencies, rates from Frankfurter/European Central Bank), ClaudeUsageTracker (EUR conversion when Spanish locale)
**(b) Verified data source:** Frankfurter API (frankfurter.app) — free, no API key, European Central Bank data. Cache rates for 24h.
**(c) Difficulty: LOW.** HTTP GET to Frankfurter API, cache result, multiply USD costs. ~0.5 days.

---

## Category 2: Session History/Logging

### Feature 6: Session Timeline with Duration and Token Counts

**(a) Inspired by:** CodeBurn (session-level tracking with timestamps), claude-dashboard (session duration, session ID), toktrack (persistent cache preserving history beyond 30-day deletion)
**(b) Verified data source:** Claude Code JSONL files have `timestamp` per message, `sessionId` field. First message timestamp to last = duration. Sum usage fields for total tokens.
**(c) Difficulty: LOW.** Parse JSONL, compute session boundaries, store in SQLite. ~1-2 days.

### Feature 7: Persistent History Cache (Survives 30-Day Deletion)

**(a) Inspired by:** toktrack (179 stars) — explicitly built to solve Claude Code's 30-day data deletion. "Claude Code deletes your session data after 30 days by default. Once deleted, your token usage and cost history are gone forever — unless you preserve them."
**(b) Verified data source:** Claude Code session JSONL files. The monitor should copy/ingest data on each poll cycle into its own SQLite DB before the source files are deleted.
**(c) Difficulty: MEDIUM.** File watcher + incremental ingestion into local DB. Track which sessions have been ingested to avoid duplicates. ~2-3 days.

### Feature 8: Activity Heatmap (GitHub-Style)

**(a) Inspired by:** TokenTracker (GitHub-style activity heatmap widget), WakaTime (daily activity visualization)
**(b) Verified data source:** Session timestamps from JSONL files. Group by date, count sessions/tokens per day. Render as colored grid in WinForms (custom painted panel).
**(c) Difficulty: MEDIUM.** Custom WinForms control with GDI+ drawing. Data is straightforward (daily aggregates). ~2 days.

---

## Category 3: Alerts/Notifications

### Feature 9: Rate Limit Monitoring with Countdown

**(a) Inspired by:** claude-dashboard (`rateLimit5h`, `rateLimit7d` widgets — 5-hour and 7-day rate limit tracking with reset countdown), Usage4AI ("Get notified if you exceed 90% of your usage limit")
**(b) Verified data source:** Claude Code exposes rate limit info via its API. The claude-dashboard plugin reads this from the session state. For a tray app, monitor the `Retry-After` headers or parse Claude's internal rate limit state. Alternatively, track request timestamps and estimate remaining capacity.
**(c) Difficulty: HARD.** Rate limit data isn't directly exposed in session files. Would need to either hook into Claude Code's internal state or estimate from request patterns. ~3-4 days.

### Feature 10: Budget Threshold Tray Notifications

**(a) Inspired by:** CodeBurn Guard (soft/hard caps), Usage4AI (90% threshold alerts), ai-token-monitor ("超过月预算 80% 时顶部显示警告条" — warning bar at 80% of monthly budget)
**(b) Verified data source:** Accumulated cost in local DB. Compare against user-configured budget in config JSON.
**(c) Difficulty: LOW.** System tray balloon tip or toast notification via Windows API. Compare daily/monthly total against threshold. ~1 day.

### Feature 11: Cost Anomaly Detection

**(a) Inspired by:** CodeBurn optimize (detects "possibly low-worth expensive sessions with no edit turns or repeated retries"), claude-dashboard (burn rate widget — token consumption per minute)
**(b) Verified data source:** Per-session cost data. Flag sessions that are statistical outliers (>2 standard deviations from mean cost-per-session) or have abnormally high burn rate.
**(c) Difficulty: MEDIUM.** Rolling statistics in SQLite. Z-score calculation. ~1-2 days.

---

## Category 4: Quick-Launch/Actions

### Feature 12: Recent Project Switcher

**(a) Inspired by:** PowerToys Run (recent apps/projects), Alfred/Raycast (quick switch), CodeBurn (per-project cost breakdown implies project awareness)
**(b) Verified data source:** Claude Code session files contain `cwd` (current working directory) and `project` fields. Track recently-used project paths from session history.
**(c) Difficulty: LOW.** Maintain an MRU (Most Recently Used) list in the tray context menu. Click to open terminal in that directory. ~1 day.

### Feature 13: Quick Command Palette (Launch Tools/Configs)

**(a) Inspired by:** PowerToys Run (command palette), Raycast (quick actions), Alfred (workflow launcher)
**(b) Verified data source:** Tool profiles from config JSON. Launch commands: `claude`, `codex`, `opencode`, `hermes`, `antigravity`.
**(c) Difficulty: LOW.** Context menu items + custom input form. `Process.Start()` to launch tools. ~1 day.

---

## Category 5: Aggregated Timeline

### Feature 14: Daily/Weekly Usage Summary with Breakdown by Tool

**(a) Inspired by:** CodeBurn (`codeburn overview` — daily cost chart, per-tool breakdown, top models, top projects), WakaTime (daily coding stats, project stats, language breakdown), TokenTracker (dashboard with usage trends)
**(b) Verified data source:** Aggregated data from local SQLite DB. Group by tool, date, model.
**(c) Difficulty: LOW.** SQL queries + DataGridView display. ~1-2 days.

### Feature 15: Task Category Classification

**(a) Inspired by:** CodeBurn (13 task categories: Coding, Debugging, Feature Dev, Refactoring, Testing, Exploration, Planning, Delegation, Git Ops, Build/Deploy, Brainstorming, Conversation, General — classified from tool usage patterns and keywords, no LLM calls)
**(b) Verified data source:** Claude Code session files contain tool usage data (Edit, Write, Bash, Grep, etc.). Classify based on which tools were used and user message keywords.
**(c) Difficulty: MEDIUM.** Keyword matching + tool usage pattern analysis. ~2-3 days.

### Feature 16: One-Shot Success Rate

**(a) Inspired by:** CodeBurn (tracks "file-aware retry cycles" — percentage of edit turns that succeeded without retries. "Coding at 90% means the AI got it right first try 9 out of 10 times.")
**(b) Verified data source:** Claude Code session files. Detect retry patterns: Edit file A, Bash command, Edit file A again = retry. Count successful first-attempt edits vs retries.
**(c) Difficulty: MEDIUM.** Pattern detection in session message sequences. ~2 days.

---

## Category 6: Cross-Tool Comparison

### Feature 17: Per-Tool Cost Efficiency Comparison

**(a) Inspired by:** CodeBurn (`codeburn compare` — side-by-side model comparison with one-shot rate, retry rate, cost per call, cost per edit, cache hit rate), Splitrail (`compare_tools` MCP tool — "Compare usage across different AI coding tools")
**(b) Verified data source:** Same JSONL/session files for each tool. Normalize metrics across tools (tokens per session, cost per session, success indicators).
**(c) Difficulty: MEDIUM.** Unified data model across 5 tool formats. Display in a comparison table. ~2-3 days.

### Feature 18: Model Cost-Effectiveness Rankings

**(a) Inspired by:** CodeBurn (`codeburn models` — per-model token + cost table, `--by-task` breakdown), TokenTracker (top models widget)
**(b) Verified data source:** Per-model usage data from session files. Rank models by cost-per-token, one-shot rate, and overall value.
**(c) Difficulty: LOW.** SQL aggregation + sorting. ~1 day.

---

## Category 7: Other Verified Features

### Feature 19: CSV/JSON Export for External Analysis

**(a) Inspired by:** CodeBurn (`codeburn export` — CSV covering today, 7 days, 30 days; JSON export), ClaudeUsageTracker (CSV export by month/project/model), TokenTracker (JSON status output)
**(b) Verified data source:** Local SQLite DB. Query and format as CSV or JSON.
**(c) Difficulty: LOW.** File.SaveFileDialog + StreamWriter or System.Text.Json. ~0.5 days.

### Feature 20: Setup Health Grade and Optimization Recommendations

**(a) Inspired by:** CodeBurn optimize (A to F setup health grade, finds waste patterns: files re-read across sessions, low Read:Edit ratio, wasted bash output, unused MCP servers, ghost agents/skills, bloated CLAUDE.md, cache creation overhead). Each finding shows estimated token and dollar savings with ready-to-paste fixes.
**(b) Verified data source:** Claude Code config files (`~/.claude/CLAUDE.md`, `~/.claude/settings.json`, `~/.claude/agents/`, `~/.claude/skills/`). Session JSONL for waste pattern detection (repeated file reads, no-edit sessions).
**(c) Difficulty: HARD.** Multiple heuristic checks, config file parsing, session analysis. ~4-5 days.

---

## Implementation Priority (Ranked by Value vs. Effort)

| Priority | Feature | Effort | Value |
|----------|---------|--------|-------|
| 1 | Feature 1: Per-Model Cost Breakdown | Medium | ★★★★★ |
| 2 | Feature 2: Daily/Weekly/Monthly Summaries | Low | ★★★★★ |
| 3 | Feature 7: Persistent History Cache | Medium | ★★★★★ |
| 4 | Feature 10: Budget Threshold Notifications | Low | ★★★★☆ |
| 5 | Feature 6: Session Timeline | Low | ★★★★☆ |
| 6 | Feature 12: Recent Project Switcher | Low | ★★★★☆ |
| 7 | Feature 19: CSV/JSON Export | Low | ★★★★☆ |
| 8 | Feature 14: Aggregated Daily Summary | Low | ★★★★☆ |
| 9 | Feature 4: Cost Forecasting | Low | ★★★☆☆ |
| 10 | Feature 18: Model Cost-Effectiveness | Low | ★★★☆☆ |
| 11 | Feature 8: Activity Heatmap | Medium | ★★★☆☆ |
| 12 | Feature 17: Cross-Tool Comparison | Medium | ★★★☆☆ |
| 13 | Feature 15: Task Category Classification | Medium | ★★★☆☆ |
| 14 | Feature 16: One-Shot Success Rate | Medium | ★★★☆☆ |
| 15 | Feature 3: Budget Guard (Hard/Soft Caps) | Medium | ★★☆☆☆ |
| 16 | Feature 5: Multi-Currency | Low | ★★☆☆☆ |
| 17 | Feature 11: Cost Anomaly Detection | Medium | ★★☆☆☆ |
| 18 | Feature 13: Quick Command Palette | Low | ★★☆☆☆ |
| 19 | Feature 20: Setup Health Grade | Hard | ★★☆☆☆ |
| 20 | Feature 9: Rate Limit Monitoring | Hard | ★☆☆☆☆ |

---

## Key Data Sources Verified on This Machine

| Tool | Data Location | Format | Contains Cost Data? |
|------|--------------|--------|-------------------|
| Claude Code | `~/.claude/projects/**/*.jsonl` | JSONL, per-message | Token counts + model name (cost must be calculated) |
| Claude Code | `~/.claude/history.jsonl` | JSONL, per-session | Timestamps, project, session ID |
| Codex | `~/.codex/sessions/` | JSON (needs verification) | Token counts + model |
| OpenCode | `~/.config/opencode/` | Config files | No direct usage data found |
| Hermes Agent | `~/.AppData/Local/hermes/` | SQLite DB | Session history (read-only) |
| Pricing | LiteLLM JSON (bundled or API) | JSON | 2200+ model prices |

---

## Sources

- **CodeBurn**: https://github.com/getagentseal/codeburn (9,026 stars, 36 tools)
- **TokenTracker**: https://github.com/xiufengsun/TokenTracker (1,128 stars, 29 tools)
- **claude-dashboard**: https://github.com/uppinote20/claude-dashboard (536 stars)
- **Splitrail**: https://github.com/Piebald-AI/splitrail (215 stars)
- **toktrack**: https://github.com/mag123c/toktrack (179 stars)
- **ClaudeUsageTracker**: https://github.com/masorange/ClaudeUsageTracker (116 stars)
- **Usage4AI**: https://github.com/Vororna/Usage4AI (2 stars)
- **ai-token-monitor**: https://github.com/Ghost-bp/ai-token-monitor (3 stars)
- **WakaTime**: https://wakatime.com/features
- **OpenRouter**: https://openrouter.ai/docs (Workspace Budgets, Input/Output Logging)
- **LiteLLM pricing**: https://github.com/BerriAI/litellm/blob/main/model_prices_and_context_window.json
- **Frankfurter API**: https://frankfurter.app (ECB exchange rates, free)
