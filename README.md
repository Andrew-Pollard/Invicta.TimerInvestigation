# Invicta.TimerInvestigation

Measurements of when Windows actually wakes a thread that asks to wait 1 ms, with a sample program for each
finding and the output it recorded.

The investigation started with the benchmarks for `Invicta.Time`, a high-resolution `TimeProvider`. A 1 ms
`Task.Delay` on `TimeProvider.System` should take one 15.625 ms clock tick, but took 11.6–12.5 ms with the screens
on and 16.1 ms with the displays asleep, while the high-resolution provider took 1.54 ms rather than 1 ms.

## In short

- **Waits are stretched:** a process that has not asked for a finer timer resolution has each short wait extended
  to about one 15.625 ms tick after it starts, which comes to about 16 ms in practice.
- **Resolution changes release them:** whenever any process changes the global timer resolution, even for a few
  microseconds, every overdue timer in every process fires.
- **Chromium changes it constantly:** while a Chromium-based window animates, such as Edge or an Electron app,
  the resolution changes in bursts once a frame, so every process's timers follow the display's refresh rate.
- **Nothing else did:** device interrupts and precise timers in other processes did not wake timers early, and
  holding a finer resolution steady barely changed them.

## Machine

Every figure below comes from this one machine; treat them as observations of it, not of Windows in general.

| | |
|---|---|
| **OS** | Windows 11 Pro for Workstations 25H2, build 26200.9445, Balanced power plan |
| **Processor** | AMD Ryzen 7 9800X3D, 8 cores, 16 logical processors |
| **Virtualisation** | Hyper-V running, with virtualisation-based security and memory integrity enabled |
| **Graphics** | NVIDIA RTX 4000 Ada, two 2560×1440 displays supporting 60–180 Hz |
| **Peripherals** | Wireless mouse receiver, Bluetooth keyboard and headphones, USB audio |
| **Runtime** | .NET 10.0.12 |
| **Clock** | 15.625 ms clock increment and timer resolution; 0.5 ms finest resolution |

> [!NOTE]
> Several findings use a concentration, R, to say how closely a series of wake-ups lies on a grid of some period:
> 1 means every wake-up lands at the same point in the period, and a value near 0 means no relationship.
> `PeriodScan` in the library also finds the fundamental period of whatever grid the wake-ups do lie on.

## Findings

Unless stated otherwise, results were recorded with the displays asleep, because animating windows change them
(finding 9).

### 1. Idle waits wake every 16.1 ms, not every 15.625 ms

[`WakeGrid`][wake-grid] records every wake-up of four ways to wait 1 ms in a loop, from Win32 up to
`Task.Delay` ([results][displays-asleep]).

| Mechanism | Mean interval | R on the 15.625 ms tick | Fundamental grid |
|---|---:|---:|---:|
| `Thread.Sleep(1)` | 16.127 ms | 0.01 | 16.130 ms, R = 0.89 |
| Win32 waitable timer, default resolution | 16.137 ms | 0.03 | 16.142 ms, R = 0.96 |
| `System.Threading.Timer`, 1 ms period | 16.151 ms | 0.02 | 16.157 ms, R = 0.85 |
| `await Task.Delay(1)` | 16.162 ms | 0.03 | 16.167 ms, R = 0.96 |

- **Not the tick:** the wake-ups bear no relation to a 15.625 ms grid, and there are no 31 ms intervals, so this is
  a steady 16.1 ms period rather than 15.625 ms with ticks missed.
- **Not .NET:** a waitable timer with no .NET timer machinery behaves the same as `Task.Delay`.

### 2. Each wait is extended from when it starts

[`Trace-TimerWakeUps`][trace-tool] records a kernel trace while `WakeGrid` runs, and
[`TimerTraceAnalysis`][trace-analysis] matches each timer `WakeGrid` sets with its expiration
([results][trace-results]).

| Scenario | Due time after the wait starts, median | Due time within the 15.625 ms cycle | Expiry after due time, median |
|---|---:|---|---:|
| Desktop | 0.982 ms | spread from 0.8 to 14.9 ms | 15.058 ms |
| Mouse moving | 1.018 ms | spread from 0.5 to 14.7 ms | 14.955 ms |
| Silent audio | 1.008 ms | spread from 0.9 to 15.1 ms | 14.907 ms |
| Displays asleep | 0.980 ms | spread from 0.9 to 15.1 ms | 15.067 ms |

- **Due time untouched:** Windows records each 1 ms wait as due 1 ms after it starts, at every point in the
  15.625 ms cycle, so it is not rounded to a tick when set.
- **Expiry deferred:** the timer then expires about 15 ms after that due time, tightly clustered (5th to 95th
  percentile within 1.3 ms), so the wait ends about 16 ms after it began, whenever it began.
- **Not released early:** a precise timer waking every 3 ms in another process did not end these waits sooner
  (finding 8), and the most common sequences before a wake-up were the kernel's own timer processing rather than
  device interrupts (finding 10).

This fits the per-process timer resolution Windows 10 version 2004 introduced, described in
[The Great Rule Change][great-rule-change].

### 3. The periodic tick services timers sooner on a busy processor

[`IdleVersusBusyCore`][idle-busy] waits on one logical processor while it is idle, then while a low-priority
thread keeps it busy ([results][displays-asleep]).

| Median | Idle processor | Busy processor |
|---|---:|---:|
| `Thread.Sleep(1)` interval | 16.117 ms | 15.694 ms |
| 1 ms default-resolution timer, total wait | 16.105 ms | 15.693 ms |
| 1 ms high-resolution timer, lateness | 0.530 ms | 0.529 ms |

- **Busy:** waits end on the periodic 15.625 ms tick, plus about 70 µs.
- **Idle:** waits take about half a millisecond longer, the same amount a high-resolution timer is late on either,
  consistent with the half-millisecond steps in finding 5.

### 4. Interrupt time comes from the precise clock

[`InterruptTime`][interrupt-time] compares the unbiased interrupt time against the performance counter
([results][displays-asleep]).

- **Precise:** interrupt time advanced exactly as far as the performance counter, a ratio of 1.00000, both when
  sampled after each wake-up and when polled continuously; Windows is not counting it up in 15.625 ms steps.
- **Busy polling:** a thread polling on a busy core saw interrupt time change every 1.5–2 ms for most of the run,
  although the global timer resolution stayed at 15.625 ms. What generates those interrupts is not known.

### 5. Timers expire on roughly 0.5 ms steps

[`DurationSweep`][duration-sweep] asks waitable timers for a range of due times and measures what each wait
delivers ([results][displays-asleep]).

| Requested | 0.50 | 1.00 | 1.10 | 1.25 | 1.50 | 1.75 | 2.30 | 5.00 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **High resolution, median** | 1.014 | 1.531 | 1.530 | 1.529 | 2.029 | 2.031 | 2.567 | 5.432 |

| Requested | 1.0 | 10.0 | 15.0 | 15.6 | 16.0 | 20.0 | 31.0 | 32.0 |
|---|---:|---:|---:|---:|---:|---:|---:|---:|
| **Default resolution, median** | 16.106 | 16.109 | 16.095 | 16.113 | 16.152 | 32.279 | 32.267 | 32.310 |

All values are milliseconds.

- **High resolution:** short waits end at the next half-millisecond step after the due time, plus about 30 µs.
- **Default resolution:** waits come in units of about 16.1 ms, consistent with 15.625 ms rounded onto the same
  steps.
- **Not a simple rule:** 2 ms requests ranged from 2.1 to 3.0 ms and 5 ms requests from 5.1 to 5.8 ms, so the
  exact rounding is not established.

### 6. How late a timer fires depends on when the wait starts

[`RandomPhase`][random-phase] measures a high-resolution timer's lateness when each wait starts straight after
the previous wake-up, as a benchmark loop does, and when it starts at a random moment ([results][displays-asleep]).

| Requested | Back-to-back, mean lateness | Random start, mean lateness |
|---|---:|---:|
| 1.00 ms | 0.526 ms (99% at 0.5 ms) | 0.176 ms (68% under 0.1 ms, 30% at 0.5 ms) |
| 1.25 ms | 0.280 ms | 0.288 ms |
| 5.00 ms | 0.464 ms | 0.494 ms |

- **Worst case in loops:** a loop that re-arms the moment it wakes starts on a step boundary and pays about
  0.5 ms on a 1 ms wait every time.
- **Other durations:** at 1.25 ms and 5 ms the start makes little difference, which the step model alone does not
  explain.

### 7. Absolute scheduling keeps a periodic timer on time

[`PeriodicDrift`][periodic-drift] runs a 1 ms period on a high-resolution timer two ways ([results][displays-asleep]).

| Schedule | Mean interval | Ticks delivered |
|---|---:|---:|
| Re-armed one period after each wake-up | 1.530 ms | 65.4% |
| Each tick aimed at start + n periods | 1.001 ms | 99.9% |

- **Relative:** every tick inherits the previous tick's lateness, so a third of the ticks are lost.
- **Absolute:** individual ticks are still late, but the next due time does not move, so the rate holds. This is
  why `Invicta.Time`'s periodic benchmarks report 1.00 ms while its one-shot benchmarks report 1.54 ms.

### 8. Changing the timer resolution fires every overdue timer

[`WakeGrid`][wake-grid] runs alongside [`TimerNeighbor`][timer-neighbor], a separate process doing one of four
things.

| Other process | Mean interval, all four mechanisms | Fundamental grid |
|---|---:|---:|
| [Nothing](results/neighbor-control/WakeGrid.txt) | 16.083–16.154 ms | 16.100–16.175 ms |
| [High-resolution timer every 3 ms](results/neighbor-precise-3ms/WakeGrid.txt) | 15.872–16.001 ms | 15.917–16.015 ms |
| [Holding a 1 ms resolution](results/neighbor-hold-1ms/WakeGrid.txt) | 15.665–15.765 ms | 15.655–15.767 ms |
| [Raising and lowering the resolution every 3 ms](results/neighbor-toggle-3ms/WakeGrid.txt) | 2.998–3.003 ms | 3.000 ms, R = 0.79–0.91 |
| [Raising and lowering the resolution every 7 ms](results/neighbor-toggle-7ms/WakeGrid.txt) | 6.990–7.001 ms | 7.000 ms, R = 0.97–0.98 |

- **The change, not the value:** holding 1 ms left `WakeGrid` at about 15.7 ms, only slightly sooner, probably
  because the finer clock no longer adds the half-millisecond step. Raising and immediately lowering it put every
  mechanism exactly on the other process's schedule.
- **Precise timers are harmless:** a high-resolution timer changes nothing for other processes, so
  `Invicta.Time`'s `HighResolutionTimeProvider` does not disturb them.

### 9. Animating Chromium windows change the resolution in bursts

[`Invoke-WithChromiumAnimation`][chromium-tool] opens [animation.html][animation] in a small Edge window, then runs
[`WakeGrid`][wake-grid] and [`ResolutionWatch`][resolution-watch], which counts changes of the global timer
resolution by polling it in a tight loop. Other Chromium-based windows were minimised.

| Page | Resolution changes | Time at 1 ms | `WakeGrid` fundamental grid |
|---|---:|---:|---|
| [Static](results/chromium-static) | 0 in 4 s | 0% | 16.125–16.170 ms, mean 16.04–16.16 ms |
| [Animating at 178 fps](results/chromium-animating) | 4,714 in 4 s | 8.9% | 5.555–5.558 ms, R = 0.98–0.99, mean 5.67–5.77 ms |

- **Brief spells:** each spell at 1 ms lasts about 45 µs (median), in bursts up to a frame apart, far too brief for
  an occasional check of the resolution to see.
- **Chromium's timers:** Chromium's message pump
  [raises the resolution to 1 ms before sleeping when short timers are pending][chromium-pump], through
  [`timeBeginPeriod` and `timeEndPeriod`][chromium-time].
- **Variable strength:** how often Chromium changes the resolution varies from run to run, and so does the effect.
- **Not the display:** a non-Chromium window redrawing on every frame, recorded in the trace with nothing else
  animating, left `WakeGrid` on a 15.8 ms grid.

This explains the frame-aligned wake-ups seen with the screens on, when the Claude desktop app, an Electron app,
was open and showing the investigation's progress:

| Condition | Frame | Fundamental grid | R |
|---|---:|---|---|
| [Desktop, 60 Hz][desktop-60hz] | 16.667 ms | 16.660–16.662 ms | 0.93–0.95 |
| [Desktop, 120 Hz][desktop-120hz] | 8.333 ms | 8.333–8.335 ms | 0.96–0.97 |
| [Desktop, 180 Hz][desktop-180hz] | 5.556 ms | 5.555 ms for the first two mechanisms | 0.84–0.88, then 0.28–0.32 |
| [Desktop, 180 Hz, displays kept on][desktop-180hz-kept-on] | 5.556 ms | 5.555 ms | 0.43–0.61 |
| [Window redrawing every 15 ms, 180 Hz][redraw-15ms] | 5.556 ms | 5.28–5.56 ms | 0.31–0.38 |
| [Window redrawing every frame, 180 Hz][redraw-every-frame] | 5.556 ms | 5.26–7.95 ms | 0.27–0.41 |

Benchmark `TimeProvider.System` with the displays asleep, or with every Chromium-based application closed or
minimised.

> [!WARNING]
> A screen timeout during a screens-on run silently turns it into a displays-asleep run: three runs at 60, 120 and
> 180 Hz all gave the 16.1 ms grid, most likely for that reason, and were discarded.
> [`Invoke-WithScreenActivity`][screen-tool] and [`Invoke-WithChromiumAnimation`][chromium-tool] keep the
> displays on while they run.

### 10. Device interrupts did not wake timers

[`TimerTraceAnalysis`][trace-analysis] also counts interrupts per driver and lists what the kernel logged on the
same processor just before each `WakeGrid` thread was readied ([results][trace-results]).

| Scenario | Busiest device interrupt | Wake-ups following a device interrupt |
|---|---|---:|
| Desktop | `dxgkrnl.sys`, 21 per second | none in the top patterns |
| Mouse moving | `Wdf01000.sys`, 123 per second | none in the top patterns |
| Silent audio | `Wdf01000.sys`, 47 + 33 + 13 per second on three vectors | none in the top patterns |
| Redraw every frame | `dxgkrnl.sys`, 917 per second | none in the top patterns |
| Displays asleep | `storport.sys`, 8 per second | none in the top patterns |

- **Kernel timer processing:** in every scenario the most common sequences before a wake-up were timer
  expirations, or a processor leaving an idle state followed by timer processing.
- **Devices ruled out:** the wireless mouse, audio playback, the GPU and storage all raised interrupts, but none
  of them woke a timer early.

## Open questions

- **Busy polling:** the source of the interrupts every 1.5–2 ms (finding 4).
- **Half-millisecond steps:** the exact rounding rule, given the 2 ms and 5 ms results (finding 5).
- **Event meanings:** the timer set, timer expiration and idle-state events in the trace are undocumented; their
  meanings were inferred from their payloads and from which threads log them.
- **Hyper-V:** whether virtualisation-based security affects any of this; testing it means turning memory
  integrity off and restarting.

## Running the samples

Requires Windows 10 version 1803 or later and the .NET 10 SDK.

```powershell
dotnet run -c Release --project samples/WakeGrid
```

| Sample | Finding |
|---|---|
| [`WakeGrid`][wake-grid] | 1, 8 and 9 |
| [`IdleVersusBusyCore`][idle-busy] | 3 |
| [`InterruptTime`][interrupt-time] | 4 |
| [`DurationSweep`][duration-sweep] | 5 |
| [`RandomPhase`][random-phase] | 6 |
| [`PeriodicDrift`][periodic-drift] | 7 |
| [`TimerNeighbor`][timer-neighbor] | 8 |
| [`ResolutionWatch`][resolution-watch] | 9 |
| [`TimerTraceAnalysis`][trace-analysis] | 2 and 10 |

The scripts in `tools` control the conditions the results were recorded under.

- **[`Invoke-WithDisplaysAsleep`][asleep-tool]:** builds the solution, puts the displays to sleep, runs samples
  with their output written to files, then wakes the displays, announcing each step aloud. `-Companion` runs
  another sample alongside each one. Touching the mouse or keyboard wakes the displays and spoils the run.
- **[`Invoke-WithChromiumAnimation`][chromium-tool]:** runs samples while [animation.html][animation] animates in
  a temporary Edge profile, and records the page's frame rate.
- **[`Invoke-WithScreenActivity`][screen-tool]:** runs samples while a small non-Chromium window redraws on every
  frame, or on a timer with `-RedrawInterval`.
- **[`Set-DisplayRefreshRate`][refresh-tool]:** lists supported refresh rates or changes every display's rate
  without saving it; `-Restore` returns to the saved settings.
- **[`Trace-TimerWakeUps`][trace-tool]:** from an elevated prompt, records a kernel trace with
  [TimerWakeUps.wprp][wprp] while `WakeGrid` runs under five narrated scenarios.

```powershell
tools\Invoke-WithDisplaysAsleep.ps1 -Sample 'WakeGrid 3' -Companion 'TimerNeighbor toggle 18 3' -OutputDirectory results\neighbor-toggle-3ms
tools\Invoke-WithChromiumAnimation.ps1 -Sample 'WakeGrid 3', 'ResolutionWatch 4' -OutputDirectory results\chromium-animating
tools\Trace-TimerWakeUps.ps1 -OutputDirectory $env:TEMP\timer-trace
dotnet run -c Release --project tools/TimerTraceAnalysis -- $env:TEMP\timer-trace\trace.etl $env:TEMP\timer-trace\timeline.json
```

> [!CAUTION]
> A kernel trace records details of the machine, its network configuration, its devices and every running process.
> Share the analysis, which contains only aggregates, rather than the trace itself; the trace behind these results
> is not included.

## References

- [High-resolution timers][high-resolution-timers], on how Windows runs the clock faster around a timer's expiry.
- [Saving energy with VSync control][vsync-control], on when Windows stops VSync interrupts.
- [Windows timer resolution: the great rule change][great-rule-change], on per-process timer resolution since
  Windows 10 version 2004.
- [MessagePump timer resolution][chromium-pump] and [time_win.cc][chromium-time], on when Chromium raises the
  timer resolution.

## Licence

Released under the [MIT License][license]. The repository configuration files are based on other projects'; their
notices are in [THIRD-PARTY-NOTICES.md][notices].

[wake-grid]: samples/WakeGrid/Program.cs
[idle-busy]: samples/IdleVersusBusyCore/Program.cs
[interrupt-time]: samples/InterruptTime/Program.cs
[duration-sweep]: samples/DurationSweep/Program.cs
[random-phase]: samples/RandomPhase/Program.cs
[periodic-drift]: samples/PeriodicDrift/Program.cs
[timer-neighbor]: samples/TimerNeighbor/Program.cs
[resolution-watch]: samples/ResolutionWatch/Program.cs
[trace-analysis]: tools/TimerTraceAnalysis/Program.cs
[asleep-tool]: tools/Invoke-WithDisplaysAsleep.ps1
[chromium-tool]: tools/Invoke-WithChromiumAnimation.ps1
[screen-tool]: tools/Invoke-WithScreenActivity.ps1
[refresh-tool]: tools/Set-DisplayRefreshRate.ps1
[trace-tool]: tools/Trace-TimerWakeUps.ps1
[wprp]: tools/TimerWakeUps.wprp
[animation]: tools/animation.html
[displays-asleep]: results/displays-asleep
[trace-results]: results/trace
[desktop-60hz]: results/desktop-60hz/WakeGrid.txt
[desktop-120hz]: results/desktop-120hz/WakeGrid.txt
[desktop-180hz]: results/desktop-180hz/WakeGrid.txt
[desktop-180hz-kept-on]: results/desktop-180hz-kept-on/WakeGrid.txt
[redraw-15ms]: results/redraw-15ms-180hz/WakeGrid.txt
[redraw-every-frame]: results/redraw-every-frame-180hz/WakeGrid.txt
[high-resolution-timers]: https://learn.microsoft.com/windows-hardware/drivers/kernel/high-resolution-timers
[vsync-control]: https://learn.microsoft.com/windows-hardware/drivers/display/saving-energy-with-vsync-control
[great-rule-change]: https://randomascii.wordpress.com/2020/10/04/windows-timer-resolution-the-great-rule-change/
[chromium-pump]: https://groups.google.com/a/chromium.org/g/scheduler-dev/c/eK5-9yNZ-Og/m/WzyjMraQBwAJ
[chromium-time]: https://github.com/adobe/chromium/blob/master/base/time_win.cc
[license]: LICENSE
[notices]: THIRD-PARTY-NOTICES.md
