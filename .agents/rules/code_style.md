# Coding Style & Conventions

- **Language Policy**: Always write code comments, variables, methods, classes, and all text/strings within code files in English. When communicating with the user in the chat interface, always use Vietnamese.
- **No Tooltips**: Do NOT add `[Tooltip(...)]` attributes to fields or serialized variables.
- **No Defensive Null Checking**: Do NOT write defensive `if (x != null)` / `if (x == null)` checks or verbose `if-else` null branching unless explicitly required by core logic or requested by the user. Keep code direct, clean, and concise.
- **No UnityEvents**: Do NOT use `UnityEvent` or `UnityEngine.Events`. Always use standard C# events (`public event Action ...` / `public event Action<...> ...`) instead.
