# Independent probe result

Time (local): 2026-08-22 12:31:06
Workspace: `c:/D/Work/VibeCoding`

| Probe | Result | Detail |
|-------|--------|--------|
| 1. NocoDB reachable | PASS | version=2026.06.1 baseHasAdmin=True |
| 1. Sign in (JWT) | PASS | got xc-auth JWT |
| 1. Create/list API token | PASS | xc-token ready (NIH7…Ewne) |
| 1. Ensure base PM_Probe | PASS | baseId=pp2qnwztfi21xu7 |
| 2. Ensure tables | PASS | tasks=myv519vh9damj1w completions=mjnfrhne1rsm094 favorites=mjd3yooedkus9de |
| 2. Completions → Tasks relation | PASS | link column Task id=clqbkun2enar673 |
| 1+2. Insert task and two completions, then query | PASS | task=3 completions=2 title=probe-daily-20260822-123104 |
| 4. Upload attachment, save to record, download temp file | PASS | uploaded+downloaded 92 bytes → C:\Users\Art\AppData\Local\Temp\pm-probe-bf55d7a2213b4b67b3fedac5f91e87a7.png |
| 4. Load bitmap in WPF (thumbnail source) | PASS | decoded 32x32 px |
| 4. Clipboard SetImage / GetImage roundtrip | PASS | clipboard image 32x32 (paste into Paint to visually confirm) |
| 4. HoneyView Process.Start | PASS | started pid=8928 alive=True path=D:\tools\Honeyview\Honeyview.exe |
| 3. Immediate Windows toast (API) | PASS | ToastContentBuilder.Show() returned without exception |
| 3. Schedule toast +60s (API + queue) | PASS | AddToSchedule ok delivery=12:32:06 queuedVisible=True scheduledAt=12:32:06 |

Visual follow-up still needed: Windows toast appearance, scheduled toast after exit, clipboard paste into Paint/WeChat, HoneyView window.
