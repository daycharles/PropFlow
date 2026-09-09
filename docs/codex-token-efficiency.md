# Codex token-efficiency playbook

This repository uses the included Codex allowance efficiently. The configured default is
`gpt-5.6-terra` with low reasoning. Context7 is the sole third-party MCP server: it supplies
targeted, current library and framework documentation. Prompt compressors and external model
routers are not part of this workflow.

## Task brief

Use this brief before substantial work:

```text
Outcome:
Relevant paths/components:
Constraints:
Acceptance check:
```

Start from targeted searches and bounded reads. For third-party libraries and frameworks, use
Context7 with a specific library and documentation question rather than requesting an entire
guide. Expand the search or context only when the previous result identifies what is missing.

## Model and verification policy

| Work | Model / effort | Verification |
| --- | --- | --- |
| Locate code, summarize, triage output, mechanical edit, focused documentation lookup | Luna | Inspect the requested result |
| Implement, debug, design, or review | Terra / low | Smallest relevant check |
| Hard ambiguity or failure after focused work | Higher effort or Astra, with written reason | Targeted reproduction plus relevant check |

Do not rerun passing checks or broaden the test suite without a new change, failure, or
release-critical reason.

## Five-hour allowance policy

| Current usage | Action |
| --- | --- |
| Below 70% | Normal policy |
| 70–89% | Luna for routine work; Terra only for implementation and final verification |
| 90% or above | Bounded fixes, evidence, or a handoff only; defer exploration, broad refactors, and repeated tests |

Check usage before and after each substantial task. Record two complete five-hour windows in
`docs/codex-usage-log.csv`, then compare burn rate and rework with the baseline. Retain this
policy only if it reduces allowance consumption without material rework.

## Handoff template

```text
Decision(s):
Changed paths:
Remaining work:
Verification status:
```
