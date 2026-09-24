# Display and preset performance

The expensive work is driver, COM, WMI and DDC/CI I/O. The optimization keeps protocol delays intact and reduces redundant requests.

## Reading policies

- Preset drift comparisons reuse hardware snapshots for up to five seconds. Settings-owned values are composed from a new settings snapshot on every comparison.
- Explicit preset capture/update and post-apply verification bypass the hardware snapshot cache and read live values. Independent monitors can be read concurrently, with a maximum of four workers. The existing per-monitor, cross-process DDC mutex still serializes each physical channel.
- UI monitor controls reuse successful individual readings for five seconds. The UI queries writable controls and the four information codes it displays; full diagnostic reports still request the complete monitor data. Brightness remains on its dedicated read path.
- Hardware writes invalidate snapshots before and after the operation, including partial failures. Completion also invalidates monitor-control readings and queues one debounced drift refresh on the UI dispatcher. Rescan and layout changes clear relevant caches.
- Preset-file caching uses full path, modification time, creation time and length, plus a two-second expiry. Save/delete invalidate it immediately. Returned objects are deep copies so editing a preset cannot mutate cached data.
- New caches have bounded capacity and LRU eviction. Concurrent requests for one key share a factory; failed factories are removed. Invalidating an in-flight read prevents it from being stored over a newer entry.

## UI work

Display view models survive activation when layout and settings-file metadata have not changed. Concurrent activation refreshes share their existing task. This avoids rebuilding the whole display page while a slow device is answering.

Resolution and refresh-rate menus share one driver mode enumeration and an indexed `(width, height) → rates` lookup. Selection changes no longer enumerate modes on the UI thread. Preset selection uses a name dictionary, and unchanged difference rows are retained instead of clearing and repopulating the observable collection.

## Measurements

Read-only measurements on the development machine's internal panel and external Dell, September 18, 2026:

| Operation | Before | First optimized run |
|---|---:|---:|
| Initial UI monitor controls | 6,085 ms | 3,694 ms |
| Repeated UI monitor controls | 3,991 ms | 0.9 ms |
| Fresh whole-desk capture | 15,315 ms | 1,382 ms |
| Repeated drift capture | 15,115 ms | 0.9 ms |
| 1,000 preset-file reads | 134 ms | 46 ms |

These are individual runs, not statistical guarantees. Hardware timings vary with driver state and contention. Warm-cache timings apply only within the stated lifetimes. A fresh capture still performs live reads; a warm comparison intentionally reuses recent hardware data.

Run `dotnet run --project tools/perfcheck -c Release` to measure the current paths; `-- --details` breaks down individual read APIs. This reads hardware and uses a temporary preset file, but does not apply settings or overwrite user presets.

Run `dotnet run --project tools/presetverify -c Release` for regression checks, including concurrent cache misses, LRU eviction, expiry, transient failures, in-flight invalidation, mutable-object isolation, external file edits and deleted files. Add `-- --capture-live` for a read-only capture/JSON round trip on attached displays.
