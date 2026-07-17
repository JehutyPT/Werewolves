# Terminal lobby cache generator

Generation is deliberately opt-in. Ordinary restore, build, test, publish, and
MAUI packaging commands may compile this project but never execute it.

From the repository root, generate the packaged current-profile cache and a
separate uncommitted diagnostic report with:

```sh
dotnet run --project Werewolves.Core/Werewolves.Core.CacheGenerator --configuration Release -- \
  --output Werewolves.Client/Resources/Raw/terminal-lobby-cache.json \
  --diagnostics /tmp/terminal-lobby-cache-generation.json \
  --degree-of-parallelism 8
```

The command publishes the diagnostics first and the app artifact last, after
both canonical payloads have been fully generated and validated. Cancellation
or failure preserves the previous app artifact.
