# Invicta.TimerInvestigation

Measurements of when Windows actually wakes a thread that asks to wait 1 ms, with a sample program for each
finding and the output it recorded.

The investigation started with the benchmarks for `Invicta.Time`, a high-resolution `TimeProvider`. A 1 ms
`Task.Delay` on `TimeProvider.System` should take one 15.625 ms clock tick, but took 11.6–12.5 ms with the screens
on at 60 Hz and 16.1 ms with the displays asleep, while the high-resolution provider took 1.54 ms rather than 1 ms.

## Machine

Every figure below comes from this one machine; treat them as observations of it, not of Windows in general.

| | |
|---|---|
| **OS** | Windows 11 Pro for Workstations 25H2, build 26200.9445, Balanced power plan |
| **Processor** | AMD Ryzen 7 9800X3D, 8 cores, 16 logical processors |
| **Virtualisation** | Hyper-V running, with virtualisation-based security and memory integrity enabled |
| **Graphics** | NVIDIA RTX 4000 Ada, two 2560×1440 displays supporting 60–180 Hz |
| **Runtime** | .NET 10.0.12 |
| **Clock** | 15.625 ms clock increment and timer resolution; 0.5 ms finest resolution |

`WakeGrid` and `InterruptTime` read the global timer resolution as 15.625 ms in every recorded run, so no other
process had lowered it with `timeBeginPeriod` while they were checking.

> [!NOTE]
> Several findings use a concentration, R, to say how closely a series of wake-ups lies on a grid of some period:
> 1 means every wake-up lands at the same point in the period, and a value near 0 means no relationship.
> `PeriodScan` in the library also finds the fundamental period of whatever grid the wake-ups do lie on.

## Findings

Unless stated otherwise, results were recorded with the displays asleep, because screen activity changes them
(finding 7).

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

### 2. The periodic tick services timers only on a busy processor

[`IdleVersusBusyCore`][idle-busy] waits on one logical processor while it is idle, then while a low-priority
thread keeps it busy ([results][displays-asleep]).

| Median | Idle processor | Busy processor |
|---|---:|---:|
| `Thread.Sleep(1)` interval | 16.117 ms | 15.694 ms |
| 1 ms default-resolution timer, total wait | 16.105 ms | 15.693 ms |
| 1 ms high-resolution timer, lateness | 0.530 ms | 0.529 ms |

- **Busy:** waits end on the periodic 15.625 ms tick, plus about 70 µs.
- **Idle:** waits take about half a millisecond longer, the same amount a high-resolution timer is late on either.
  That fits Windows waking an idle processor with a one-off timer interrupt instead of the periodic tick, which
  is how a dynamic tick behaves, but the mechanism is inferred rather than observed.

### 3. Interrupt time comes from the precise clock

[`InterruptTime`][interrupt-time] compares the unbiased interrupt time against the performance counter
([results][displays-asleep]).

- **Precise:** interrupt time advanced exactly as far as the performance counter, a ratio of 1.00000, both when
  sampled after each wake-up and when polled continuously; Windows is not counting it up in 15.625 ms steps.
- **Busy polling:** a thread polling on a busy core saw interrupt time change every 1.5–2 ms for most of the run,
  although the global timer resolution stayed at 15.625 ms. What generates those interrupts is not known.

### 4. Timers expire on roughly 0.5 ms steps

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

### 5. How late a timer fires depends on when the wait starts

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

### 6. Absolute scheduling keeps a periodic timer on time

[`PeriodicDrift`][periodic-drift] runs a 1 ms period on a high-resolution timer two ways ([results][displays-asleep]).

| Schedule | Mean interval | Ticks delivered |
|---|---:|---:|
| Re-armed one period after each wake-up | 1.530 ms | 65.4% |
| Each tick aimed at start + n periods | 1.001 ms | 99.9% |

- **Relative:** every tick inherits the previous tick's lateness, so a third of the ticks are lost.
- **Absolute:** individual ticks are still late, but the next due time does not move, so the rate holds. This is
  why `Invicta.Time`'s periodic benchmarks report 1.00 ms while its one-shot benchmarks report 1.54 ms.

### 7. Screen activity can move timers onto the display's frame grid

[`WakeGrid`][wake-grid] again, this time with the screens on. With nothing arranged, wake-ups often lie on a grid
exactly one frame long; the same code, deliberately redrawing a window, mostly does not.

| Condition | Frame | Fundamental grid | R |
|---|---:|---|---|
| [Desktop, 60 Hz][desktop-60hz] | 16.667 ms | 16.660–16.662 ms | 0.93–0.95 |
| [Desktop, 120 Hz][desktop-120hz] | 8.333 ms | 8.333–8.335 ms | 0.96–0.97 |
| [Desktop, 180 Hz][desktop-180hz] | 5.556 ms | 5.555 ms for the first two mechanisms | 0.84–0.88, then 0.28–0.32 |
| [Desktop, 180 Hz, displays kept on][desktop-180hz-kept-on] | 5.556 ms | 5.555 ms | 0.43–0.61 |
| [Window redrawing every 15 ms, 180 Hz][redraw-15ms] | 5.556 ms | 5.28–5.56 ms | 0.31–0.38 |
| [Window redrawing every frame, 180 Hz][redraw-every-frame] | 5.556 ms | 5.26–7.95 ms | 0.27–0.41 |

- **Real:** on the desktop the grid matches the frame to within 0.05% at every refresh rate, with no relation to
  the tick, and the mean interval at 60 Hz (12.2–12.9 ms) is close to the 11.6–12.5 ms the `Invicta.Time`
  benchmarks reported.
- **Variable:** similar conditions gave R = 0.88 and, sixteen minutes later, 0.43–0.61. "Desktop" means an
  ordinary session with the Claude desktop app showing the session's progress, which was not controlled.
- **Not VSync alone:** Windows keeps VSync interrupts running while the screen changes at least every ten frames,
  which a 15 ms redraw satisfies, yet most waits in that run ended on a 15.7 ms tick instead.
- **Not composition alone:** a window measured redrawing 180 times a second did no better. Its constant activity
  appears to keep the periodic tick running, as on the busy processor in finding 2.

Benchmark `TimeProvider.System` with the displays asleep, or expect results that depend on the refresh rate and
on whatever else is drawing.

> [!WARNING]
> A screen timeout during a screens-on run silently turns it into a displays-asleep run: three runs at 60, 120 and
> 180 Hz all gave the 16.1 ms grid, most likely for that reason, and were discarded.
> [`Invoke-WithScreenActivity`][screen-tool] keeps the displays on while it runs.

## Open questions

- **Frame-aligned wake-ups:** what delivers them on an ordinary desktop, and why a window redrawing every frame
  does not. A Windows Performance Recorder trace of interrupts and deferred procedure calls, which needs an
  elevated prompt, should show the source.
- **Half-millisecond steps:** the exact rounding rule, given the 2 ms and 5 ms results.
- **Idle processors:** whether the extra half-millisecond really comes from a dynamic tick's one-off interrupts.
- **Busy polling:** the source of the interrupts every 1.5–2 ms.
- **Hyper-V:** whether virtualisation-based security affects any of this; testing it means turning memory
  integrity off and restarting.

## Running the samples

Requires Windows 10 version 1803 or later and the .NET 10 SDK.

```powershell
dotnet run -c Release --project samples/WakeGrid
```

| Sample | Finding |
|---|---|
| [`WakeGrid`][wake-grid] | 1 and 7 |
| [`IdleVersusBusyCore`][idle-busy] | 2 |
| [`InterruptTime`][interrupt-time] | 3 |
| [`DurationSweep`][duration-sweep] | 4 |
| [`RandomPhase`][random-phase] | 5 |
| [`PeriodicDrift`][periodic-drift] | 6 |

The scripts in `tools` control the conditions the results were recorded under.

- **[`Invoke-WithDisplaysAsleep`][asleep-tool]:** builds the solution, puts the displays to sleep, runs samples
  with their output written to files, then wakes the displays, announcing each step aloud. Touching the mouse or
  keyboard wakes the displays and spoils the run.
- **[`Invoke-WithScreenActivity`][screen-tool]:** runs samples with the displays kept on and a small window
  redrawing on every frame, or on a timer with `-RedrawInterval`, and records the window's frame rate.
- **[`Set-DisplayRefreshRate`][refresh-tool]:** lists supported refresh rates or changes every display's rate
  without saving it; `-Restore` returns to the saved settings.

```powershell
tools\Invoke-WithDisplaysAsleep.ps1 -Sample WakeGrid, DurationSweep -OutputDirectory results\displays-asleep
tools\Set-DisplayRefreshRate.ps1 -Rate 120
tools\Invoke-WithScreenActivity.ps1 -Sample WakeGrid -OutputDirectory results\redraw-every-frame-120hz
tools\Set-DisplayRefreshRate.ps1 -Restore
```

## References

- [High-resolution timers][high-resolution-timers], on how Windows runs the clock faster around a timer's expiry.
- [Saving energy with VSync control][vsync-control], on when Windows stops VSync interrupts.
- [Windows timer resolution: the great rule change][great-rule-change], on why another process's
  `timeBeginPeriod` no longer changes when this process's timers fire.

[wake-grid]: samples/WakeGrid/Program.cs
[idle-busy]: samples/IdleVersusBusyCore/Program.cs
[interrupt-time]: samples/InterruptTime/Program.cs
[duration-sweep]: samples/DurationSweep/Program.cs
[random-phase]: samples/RandomPhase/Program.cs
[periodic-drift]: samples/PeriodicDrift/Program.cs
[asleep-tool]: tools/Invoke-WithDisplaysAsleep.ps1
[screen-tool]: tools/Invoke-WithScreenActivity.ps1
[refresh-tool]: tools/Set-DisplayRefreshRate.ps1
[displays-asleep]: results/displays-asleep
[desktop-60hz]: results/desktop-60hz/WakeGrid.txt
[desktop-120hz]: results/desktop-120hz/WakeGrid.txt
[desktop-180hz]: results/desktop-180hz/WakeGrid.txt
[desktop-180hz-kept-on]: results/desktop-180hz-kept-on/WakeGrid.txt
[redraw-15ms]: results/redraw-15ms-180hz/WakeGrid.txt
[redraw-every-frame]: results/redraw-every-frame-180hz/WakeGrid.txt
[high-resolution-timers]: https://learn.microsoft.com/windows-hardware/drivers/kernel/high-resolution-timers
[vsync-control]: https://learn.microsoft.com/windows-hardware/drivers/display/saving-energy-with-vsync-control
[great-rule-change]: https://randomascii.wordpress.com/2020/10/04/windows-timer-resolution-the-great-rule-change/
