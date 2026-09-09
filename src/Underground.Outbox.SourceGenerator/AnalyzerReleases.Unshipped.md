; Unshipped analyzer release
; https://github.com/dotnet/roslyn-analyzers/blob/main/src/Microsoft.CodeAnalysis.Analyzers/ReleaseTrackingAnalyzers.Help.md

### New Rules

Rule ID | Category | Severity | Notes
--------|----------|----------|------
OUTBOX001 | Underground.Outbox | Error | Message type has competing handlers
OUTBOX002 | Underground.Outbox | Error | Handler is not bound to a DbContext
