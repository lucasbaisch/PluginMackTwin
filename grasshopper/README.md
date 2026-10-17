# Gaze session simulator — Grasshopper C# component

Synthesises plausible VR eye-tracking sessions and writes them as CSVs in exactly the
dialect of the real HTC Vive Focus Vision captures: same 38 columns, same interleaved
left/right ordering, same RFC 4180 quoting (only `timeStamp`, `nomeDoObjeto` and
`htcDeviceName` are quoted, always), CRLF, UTF-8 without BOM, newest row first.

Alongside each one it writes an **environment CSV** — temperature, humidity, CO and noise —
on exactly the same timestamps.

**One solution writes the whole study.** The protocol is baked into the component: 26
participants with a fixed seat, period and queue position each, 3 scenes, 3 rounds a month
apart — **234 sessions**, scheduled so none of them overlap. Give it the folder holding the
seat JSONs and it opens what it needs itself; the only things you choose on the canvas are
*which* of those sessions to write.

**Re-running part of it is safe.** The full study is planned on every solve and the
selection is applied afterwards, so a session's start instant, its seed and its path come
from its identity rather than from its position in the run. Running one participant on its
own reproduces, byte for byte, the file the full run produced.

Target: **Rhino 7 → C# 7.3 → .NET Framework 4.8**. No NuGet packages — including the JSON
reader, which is written by hand in `REGION 2`. File I/O is exactly three things: the seat
JSONs are read, the two CSVs are written.

### Hit points repeat exactly

A fixation writes **one** hit point, byte for byte, on every frame it covers; only the
EyeGaze and EyeOrientation columns micro-move within it. Each saccade is written the same
way, as a short stretch repeating one point. This is deliberate, and it is not what the HTC
files do — there the hit point drifts by a few millimetres per frame and exact repeats are
under 1 % of rows.

The reason is that a consumer may group frames by an **absolute distance** between
successive hit points instead of an angular one. PluginCAISM uses 0.01 m, which at a 4 m
viewing distance is 0.14° — finer than the angular spacing of any star of realistic
density, so a drifting hit point can never satisfy it and no fixation is ever detected.
Repeating the point exactly removes the dependence on how dense the star is.

A saccade is a *movement*, so nothing in the gaze path marks where one was; a consumer can
only see it as a short stretch where the gaze was not somewhere else. Four constants in
`SimulationDefaults` control them:

| Constant | Default | What it does |
|---|---|---|
| `SaccadeHoldsPerTransitionMin` | 1 | Floor on saccades between two fixations. Keep at 1 or higher — reaching the next fixation takes one saccade by definition, so a lower value would put fewer saccades than fixations in the file, which never happens in recorded data. |
| `SaccadeHoldsPerTransitionMax` | 3 | Ceiling. The extra ones are the corrective movements after an overshoot. Set the max to 0 to switch saccades off entirely. |
| `SaccadeHoldMinSeconds` | 0.03 | Real saccades last 30–80 ms, which is also the window PluginCAISM classifies on. |
| `SaccadeHoldMaxSeconds` | 0.08 | Widen it and the extra length lands in that consumer's dead zone — above its 80 ms saccade ceiling, below its 300 ms fixation floor — written but never counted. |

Each stretch is floored at one sample interval plus its jitter, so it always spans at least
two frames: a single frame is indistinguishable from the moving samples around it. At 20 Hz
that floor is 58 ms. A sample rate low enough to push it past 80 ms cannot represent a
saccade at all, and the report's own numbers will show it.

Saccades cost session time, so raising the per-transition range lowers how many fixations
fit. At 20 Hz with 1–3 per transition the split runs about 63 % fixating, against ~69 % in
the reference capture.

---

## Files

| File | What it is |
|---|---|
| `GazeSessionSimulator.cs` | The deliverable. Two marked regions: one for `RunScript`, one for the component's *custom additional code*. |
| `ClimateTable.py` | A GhPython component holding one row of raw climate data per day of the year. Its `climate_table` output wires into the C# component's `ClimateTable` input. |
| `verify/` | An offline test bench. **Not** part of what you paste into Grasshopper. It compiles the component's logic against stub Rhino types and runs every acceptance check. |

---

## Installing it on the canvas

1. Drop a **C# Script** component on the canvas.
2. Add the inputs from the table below, **in order**, with the given type hint and access.
   Delete the default `x` and `y`.
3. Rename the outputs to `CsvPath`, `EnvCsvPath`, `Report`, `SceneNames`, `TimeStamps`,
   `Rodadas`, `ScanPath`, `FixationPoints`, `FixationDurations`, `HeadDirections`,
   `HitPoints`, `GazeRays` — twelve, in that order.
4. Open the code editor. At the top, make sure these using directives are present — the
   script template does **not** include `System.Linq` or `System.Globalization`:

   ```csharp
   using System;
   using System.Collections.Generic;
   using System.Globalization;
   using System.IO;
   using System.Linq;
   using System.Text;
   using Rhino.Geometry;
   ```

5. Paste the block marked `REGION 1` into the body of `RunScript`.
6. Paste the block marked `REGION 2` into the `<Custom additional code>` region.

### The protocol is not on the canvas

Everything that defines *the study* — the participant roster with its seats, periods and
queue positions, the three scenes, the three rounds, the starting day, the interval, the
session length and the sample rate — lives in **`StudyConfiguration`**, at the very top of
REGION 2. None of it is an input.

That is deliberate. Change any of it and every file already on disk describes a different
study, so all participants, all seats, all scenes and all rounds have to be generated
again. Burying that behind a slider on the canvas is how a batch quietly ends up
half-and-half. The three canvas selections below choose **which** of the planned sessions
get written; they never change **when** one happens.

### Inputs

**`ParticipantIds`, `Scenes`, `Rounds` and `ClimateTable` are List access; every other
input is Item.**

| # | Name | Type hint | Access | Notes |
|---|---|---|---|---|
| 1 | `JsonFolder` | string | Item | The folder the raycast wrote its seat JSONs into. A file is looked up **by name** — scene `01` seat `A1` is `01_A1.json` — so anything else in the folder is ignored rather than picked up. |
| 2 | `ParticipantIds` | string | **List** | Which participants to write. Empty = all 26. Every id must be in the roster. |
| 3 | `Scenes` | string | **List** | Which scenes to write. Empty = all of them. |
| 4 | `Rounds` | int | **List** | Which rounds to write, **1-based** (`1`, `2`, `3`). Empty = all of them. |
| 5 | `ClimateTable` | string | **List** | Wire the `climate_table` output of `ClimateTable.py`. Without it the batch fails rather than writing gaze files with no environment files beside them. |
| 6 | `PreviewIndex` | int | Item | Which session's geometry and full report come out on the outputs. Use the `[index]` shown in the report. Out of range means the first session of the selection. |
| 7 | `ConvertZUpToYUp` | bool | Item | **True when the JSONs hold ordinary Rhino coordinates**, which is the normal case. See below. |

The component opens each seat file and takes from it: `origin` → the eye position,
`forward_vector` → the seat's neutral facing direction, `rays[].vector/point/name` → the
star, and `nearest_index` → where the walk starts. The scene and the seat come from the
**roster**, not from the file's own `scene_name` / `seat_name`: the file is chosen by name,
so a JSON whose contents disagree with the name it is filed under is a mis-named file, not
a second opinion.

Each file is parsed **once for the whole run**, not once per participant. The sessions are
executed grouped by seat file rather than in schedule order to make that true — which is
free, because every timestamp and every seed is fixed during planning, from the session's
identity, never from its position in the run.

`scene_name`, `seat_name` and a ray's `name` are each read as a string **or** as a
one-element array holding one. A Grasshopper script input set to List access hands the
raycast script a list even when a single value is wired, so files in the wild carry
`"scene_name": ["01"]`; the array is unwrapped rather than rejected.

A ray whose `point` or `name` is `null` — it hit nothing — keeps its place in the star, so
the three internal lists stay aligned by index, and is marked `Objeto sem nome`. Such a ray
is **never offered as a gaze target**: if `nearest_index` points at one, the walk starts at
the nearest usable ray instead and the report says so. A seat file where fewer than two rays
hit something is rejected.

> **Coordinate frames — read this once.** The CSV is written in Unity's **Y-up** frame,
> because that is what the headset logged: `playerPosition_y` in the real captures is the
> seated eye height, 1.18 m. The Revit plugin already swaps it on read
> (`ConversorDeCoordenadas` maps `Unity(X,Y,Z)` to `Revit(X, Z, Y)`), so **the CSV must never
> be swapped on the way out** — doing so would place every hit point on its side.
>
> Rhino, however, is **Z-up**, like Revit. So the conversion that *is* needed happens at the
> input: `ConvertZUpToYUp` swaps Y and Z on everything read out of the JSON, and swaps the
> geometry outputs back so the scan path and rays still draw the right way up on the canvas.
>
> Quick test for which one you have: look at `origin` in the JSON. If its **third** number is
> around 1.1–1.3 (seated eye height), the model is Z-up — set the flag true. If the **second**
> is, it is already Y-up — leave it false.

Session:

| # | Name | Type | Notes |
|---|---|---|---|
| 8 | `Seed` | int | The study seed. Each session mixes it with its own **identity** — participant, scene, seat, period, round — so no two sessions are the same walk, and running one on its own produces the file the full run produced. The environment signal is seeded separately from the same identity, so changing a gaze parameter does not silently redraw the weather. |

> `SessionSeconds` and `SampleRateHz` used to be inputs 9 and 10. They are protocol, so
> they are now `StudyConfiguration.SessionSeconds` (300) and `.SampleRateHz` (17).

Behaviour — every one has the measured default:

| # | Name | Type | Default |
|---|---|---|---|
| 9 | `FixationCountMin` | int | 0 = derive the count from the rate |
| 10 | `FixationCountMax` | int | 0 = derive the count from the rate |
| 11 | `FixationRatePerSecond` | double | 1.7 |
| 12 | `FixationDurationMinSec` | double | 0.10 |
| 13 | `FixationDurationMaxSec` | double | 2.50 |
| 14 | `FixationDurationLogMu` | double | −1.2 |
| 15 | `FixationDurationLogSigma` | double | 0.7 |
| 16 | `IntraFixationDispersionDeg` | double | 1.3 |
| 17 | `SaccadeAmplitudeLogMu` | double | 1.4 |
| 18 | `SaccadeAmplitudeLogSigma` | double | 0.6 |
| 19 | `SaccadeAmplitudeMinDeg` | double | 0.5 |
| 20 | `SaccadeAmplitudeMaxDeg` | double | 60 |
| 21 | `ReturnBiasProbability` | double | **must be wired** — 0 means "never return" |
| 22 | `DistinctObjectsMin` | int | 0 disables the steering |
| 23 | `DistinctObjectsMax` | int | 0 disables the steering |
| 24 | `NeckYawLimitDeg` | double | 90 |
| 25 | `NeckPitchUpLimitDeg` | double | 65 |
| 26 | `NeckPitchDownLimitDeg` | double | 65 |
| 27 | `EyeYawLimitDeg` | double | 20 |
| 28 | `EyePitchLimitDeg` | double | 20 |
| 29 | `HeadTimeConstantSec` | double | 0.30 |
| 30 | `InvalidFrameRate` | double | **must be wired** — 0 means "no dropped frames" |
| 31 | `BlinkRatePerSecond` | double | **must be wired** — 0 means "no blinks" |
| 32 | `PupilMinDomain` | **Domain** | 1.6 to 2.0 — the participant's *smallest* pupil is drawn from here |
| 33 | `PupilMaxDomain` | **Domain** | 4.5 to 6.0 — their *largest* is drawn from here |
| 34 | `BaselineSeconds` | double | **must be wired** — the reader's pupil baseline window, which the pupil is held still for and after which every fixation is driven away from the measured baseline. Match PluginCAISM's `JanelaDeBaselinePupilarFimMs` (60 s). 0 is taken literally and means neither. |

Everything the CSV records to identify a session is **derived, not typed**: the participant,
the seat and the period come from the roster, the scene from `StudyConfiguration.Scenes`,
the start instant from the schedule. The two composed names are built in one place,
`SessionNaming`, so the metadata columns and the file name cannot drift apart:

| CSV column | Built from |
|---|---|
| `identificacaoManual` | the participant id |
| `selectedScene` | `{participant}_{scene}_{seat}_{period}` |
| `nomeDaSessao` | `Cenário_{scene}_{period}` |
| `selectedSeat` | the seat the roster assigns to the participant |
| `timeStamp` | the session's scheduled start, plus each sample's offset |

> **The round is deliberately not in any CSV column.** It is in the folder and in the file
> name only, so a reader of the file — PluginCAISM included — sees exactly the columns it
> saw before rounds existed. If the analysis needs to tell rounds apart from inside the
> file rather than from its path, say so: it is one line in `SessionNaming`.

Output control:

| # | Name | Type | Notes |
|---|---|---|---|
| 35 | `DeviceName` | string | The headset in the gaze CSV. Empty uses the exact real device string. Not to be confused with the environment sensor, which is `StudyConfiguration.EnvironmentDeviceName` (`MM-11`). |
| 36 | `OutputFolder` | string | The root. Everything below it — `RODADA nn/CENÁRIO nn/HTC` and `.../AMBIENTE` — **is created if missing**, so nothing has to be laid out by hand. |
| 37 | `Run` | bool | Nothing happens unless true. A full run is minutes of work and freezes the canvas while it runs — this is what keeps it from firing on every change. |

> **Unwired numeric inputs.** Grasshopper hands an unwired numeric input to the script as
> `0`. For parameters where zero is meaningless (rates, limits, durations, sigmas) that is
> read as "unset" and the measured default is used. For the five marked **must be wired**
> above, zero is a legitimate value and is taken literally — wire them. The two `Domain`
> inputs follow the same rule: only the empty `0 to 0` domain counts as unset, so a
> deliberate `2.0 to 2.0` — "every participant gets exactly 2.0" — is honoured.

> **The start instant is derived, never typed.** Rhino 7's script component has no `DateTime`
> type hint, and the instant has to be fixed rather than "now" or the same seed would not
> reproduce the same file. So `StudyConfiguration.StartingDay` is a `yyyy-MM-dd` string and
> every session's start is worked out from the schedule.

### Outputs

The first six cover the **whole run**; the last six are the **one session** named by
`PreviewIndex`. Keeping the geometry of every session would put a million points on the
canvas.

| Name | Type | Purpose |
|---|---|---|
| `CsvPath` | List&lt;string&gt; | Every gaze CSV written, in schedule order. |
| `EnvCsvPath` | List&lt;string&gt; | Parallel to `CsvPath` — the environment CSV beside each one. |
| `Report` | string | The study summary, one line per session, then the full self-check of the previewed session. Read this. |
| `SceneNames` | List&lt;string&gt; | Parallel to `CsvPath` — the composed name of each session. |
| `TimeStamps` | List&lt;string&gt; | Parallel to `CsvPath` — each session's start instant. |
| `Rodadas` | List&lt;int&gt; | Parallel to `CsvPath` — which round each session belongs to, 1-based. |
| `ScanPath` | Polyline | Fixation centroids joined in order. |
| `FixationPoints` | List&lt;Point3d&gt; | Fixation centroids. |
| `FixationDurations` | List&lt;double&gt; | Parallel to the above — good for scaling display spheres. |
| `HeadDirections` | List&lt;Vector3d&gt; | Head forward per fixation, to draw the neck path separately from the gaze path. |
| `HitPoints` | List&lt;Point3d&gt; | One per CSV row, in chronological order. |
| `GazeRays` | List&lt;Line&gt; | One per CSV row: recorded player position → recorded hit point. |

> `HitPoints` and `GazeRays` are **chronological**, which is the reverse of the CSV — the
> file is written newest first. Item `i` of these lists is data line `count - i` of the
> file. Both include invalid frames, which repeat the previous hit point exactly as the
> CSV does; filter on `nomeDoObjeto == "Objeto sem nome"` if you want only valid ones.

---

## How the study is laid out

**The roster.** 26 participants, 14 seats, two periods. Each participant has a **fixed**
seat, a **fixed** period and a **fixed** position in the queue, for the whole study:

* `Order` is the position in the queue of the day. Participant `7580` is the 8th, so it
  starts 7 slots after the window opens — **even if it is the only participant ticked**.
* Seat is `((Order − 1) mod 14)`, so orders 1–14 fill `A1`…`B7` once and orders 15–26 fill
  `A1`…`B5` a second time. `B6` and `B7` carry one participant each; every other seat
  carries two.
* Orders 1–13 are `Dia`, orders 14–26 are `Noite` — 13 and 13. The split falls where it does
  so the two participants sharing a seat are never in the same period: every doubled seat is
  measured once in the morning and once at night.

**The plan is always complete.** Every solve plans the whole study — 3 rounds × 3 scenes ×
26 participants = **234 sessions** — and only then applies `ParticipantIds`, `Scenes` and
`Rounds` as a filter over it. That is the whole point: a session's start instant, its seed
and its path come from its *identity*, never from its position in the run, so re-running one
participant reproduces exactly the file the full run produced. Nothing slides into an
earlier slot because the people ahead of it were not selected.

**The schedule.** One queue per period — `Dia` 08:00–12:00 and `Noite` 20:00–24:00 — each
slot `SessionSeconds + IntervalMinutes` wide, rolling over to the next day when the window
fills. At 300 s + 30 min that is **7 slots per window per day**, so a period's 13
participants span two days. Only one participant is ever in the room: no two sessions of the
whole study overlap, across every seat, every scene and every round.

**Scenes and rounds.** Within a round the scenes run in order, and **a scene change always
opens a new day** whatever hour the previous scene stopped at — so no day ever carries two
scenes. Three scenes at two days each is a six-day round. Round *n* opens
`MonthsBetweenRounds` after round *n−1* on the same day of the month, and the planner refuses
to build a study whose rounds would run into each other.

Starting `2026-01-01`, that is:

| Round | Folder | Days |
|---|---|---|
| 1 | `RODADA 01` | 2026-01-01 … 2026-01-06 |
| 2 | `RODADA 02` | 2026-02-01 … 2026-02-06 |
| 3 | `RODADA 03` | 2026-03-01 … 2026-03-06 |

**On disk.** Two files per session, in a folder per round and a folder per scene, all
created on demand:

```
OutputFolder/
  RODADA 01/
    CENÁRIO 01/
      HTC/
        6710_01_A1_Dia_R1.csv
      AMBIENTE/
        6710_01_A1_Dia_R1_AMBIENTE.csv
```

**Cost.** A full run is 234 sessions — 468 files — and the Grasshopper canvas is frozen for
all of it, with no progress bar. Two things that would otherwise dominate are handled: the
sessions are executed grouped by seat file, so each of the 42 JSONs is parsed **once for the
whole run** instead of once per participant; and the determinism self-check, which re-runs a
session from scratch to compare the two files, runs on the first session only. A later
session's report says `SKIP  16` rather than claiming a pass nobody checked.

**What belongs to the person.** Most draws are per session, but the pupil's anatomical range
is per **participant** — see below. It is seeded from `Seed` and the participant id alone, so
a person's eye is the same in every file they appear in, whatever the seat or the time of
day. Change `Seed` and everyone gets a new eye; change the participant list and everyone
else keeps theirs.

---

## The environment CSV

Every session writes a second file, `…_AMBIENTE.csv`, into the `AMBIENTE` folder of its
scene, with **one row per gaze row, on the same instants and in the same order** — newest
first. Row *i* of one file and row *i* of the other describe the same moment, so the two
merge on the timestamp with no interpolation.

### Making Excel type the columns correctly

**A CSV has no type layer.** There is nowhere in the format to say "this column is a
number" — every reader guesses from the text. So the only lever is to write text that the
reader's *locale* reads the way it was meant, and for pt-BR Excel that means four things,
all of which this file now does:

| | What | Why |
|---|---|---|
| 1 | Fields separated by `;` | pt-BR Excel's list separator. With `,` the entire row lands in **one cell** and nothing can be typed at all. |
| 2 | Decimals written `21,40` | pt-BR Excel reads `21.40` as text — or, worse, as the date 21 April. |
| 3 | Measurements **not** quoted | A quoted `"21,40"` is text in every locale. Only the genuinely textual columns are quoted, which is also what stops a MAC address being read as a time and an empty cell becoming `0`. |
| 4 | A UTF-8 **BOM** at the start | Without it Excel opens the file as the system code page and `ALIMENTAÇÃO` becomes `ALIMENTAÃ‡ÃƒO`. |

All four are constants in `StudyConfiguration` — set them to `,` / `.` / `false` for an
English-locale Excel, pandas or R:

```csharp
public const string EnvironmentFieldDelimiter   = ";";
public const string EnvironmentDecimalSeparator = ",";
public const bool   EnvironmentByteOrderMark    = true;
```

> **The one column Excel will still get wrong is the timestamp.** Excel has no millisecond
> datetime type on import — `2026-01-01 08:00:00.940` comes in as text whatever the file
> does. Two ways round it: import via **Dados → Obter Dados → De Texto/CSV**, which lets you
> set the column type explicitly; or drop `.fff` from the format in
> `EnvironmentCsvWriter.BuildDataLine`, at the cost of the row-for-row alignment with the
> gaze file that made you ask for milliseconds in the first place.

> **The gaze CSV is deliberately *not* changed.** It is a stand-in for a real HTC capture
> and PluginCAISM parses it, so its dialect is fixed by that contract: commas, dot decimals,
> no BOM. Open it in Excel through the import wizard rather than by double-clicking.

### Columns

| Column | Value | Written as |
|---|---|---|
| `DATA: YYYY-MM-DD HH:MM:SS` | the gaze row's timestamp, `yyyy-MM-dd HH:mm:ss.fff`. The header says seconds because that is what the device writes; the value carries milliseconds, like every other timestamp in this study. | quoted |
| `DISPOSITIVO` | `MM-11` | quoted |
| `DNS REDE` | `181.213.132.2` | quoted |
| `TEMPO ATIVIDADE` | always empty | quoted |
| `MAC ADDRESS` | `EC:64:C9:CD:D2:29` | quoted |
| `RESET` | always empty | quoted |
| `ALIMENTAÇÃO` | `Power On` | quoted |
| `TEMPERATURA` | °C, two decimals | **bare number** |
| `UMIDADE` | %, two decimals | **bare number** |
| `CO` | `0` or `1` | **bare number** |
| `RUÍDO` | dB, two decimals | **bare number** |

```
DATA: YYYY-MM-DD HH:MM:SS;DISPOSITIVO;…;ALIMENTAÇÃO;TEMPERATURA;UMIDADE;CO;RUÍDO
"2026-01-01 20:39:59.940";"MM-11";…;"Power On";17,75;78,17;0;64,40
```

**The climate table is the anchor.** `ClimateTable.py` holds one row per day of the year:
the mean temperature and humidity for the morning and for the night. A session opens within
±1 °C and ±3 % of *its day's* mean and moves from there. That is what keeps re-running one
participant from putting a step in the middle of an afternoon — two sessions recorded on the
same day agree because they are anchored to the same row, not because they were generated in
the same solve.

The bands the table was drawn from, for Campinas (southern hemisphere):

| Season | Day | Night |
|---|---|---|
| Summer | 22–32 °C | 15–21 °C |
| Fall | 20–29 °C | 13–19 °C |
| Winter | 15–22 °C | 10–14 °C |
| Spring | 20–29 °C | 13–19 °C |

Humidity never leaves 35–85 %, sits in 45–75 % on most days, and is **always** higher at
night than during the day.

**The signal within a session**, all of it in `StudyConfiguration`.

Temperature, humidity and the noise floor are the same construction: **one slice of one sine
wave**, stretched over the session. A random start phase and a random span — never wider than
a full period — pick the slice, and the slice is rescaled to a peak-to-peak drawn for that
session. One rule gives every shape a room really has: a slow climb, a slow fall, a rise that
turns over, a dip that recovers, or a session that barely moves at all.

What it cannot give is what a random walk gives by default. The previous model was mean-
reverting noise, and it changed direction about **175 times in five minutes** while covering
0.22 °C — busy and static at the same time. A slice of a sine turns **at most twice**, turns
smoothly, and holds still near its crests without anyone coding a hold.

* **Temperature** opens within ±1 °C of the day's mean and covers **0.1–1.0 °C** end to end,
  drawn per session, never faster than **0.25 °C/min**.
* **Humidity** opens within ±3 %, covers **0.4–2.2 %**, is capped at **0.5 %/min** and is
  hard-bounded to 35–85 %.
* **CO** is a flag, not a level: an even coin per row, no pattern of any kind.
* **Noise** is three layers that add up, and only the third is loud:
  1. a **floor** — the room's own hum — a sine slice like the two above, drifting about a
     decibel across the session inside 43–62 dB;
  2. a **jitter on every single row**, gaussian at σ 0.2 dB and clamped to **±0.5 dB**. This
     is the microphone breathing, and it is the only layer allowed to differ row to row;
  3. **events**, about 0.6 per minute, whose rise, body and fall are measured in **seconds**.
     Two envelopes cover what a room does — something switches on, sits there for 5–30 s at
     +3 to +12 dB and switches off, or something passes by and its level draws a bell of
     +8 to +28 dB over 6–20 s. Both excesses are **exponential**, so every extra decibel is
     less likely than the last and 90 dB stays reachable and rare. Events may overlap,
     because a room does not wait for one sound to finish before making the next.

> **Two limits, one knob.** The excursion is drawn and *then* cut down so the steepest point
> of the curve still respects the rate cap — the amplitude gives way, never the shape, because
> clamping the value would flat-top the sine and a flat top is a corner. So steep and wide
> cannot co-occur: a full period has to settle for less travel than a quarter period does. For
> a livelier room raise `TemperatureExcursionMaxC`; the 0.25 °C/min cap holds regardless, and
> because the mean value theorem puts the slope factor at 1.0 or above, a session can never
> travel further than cap × duration either.

---

## The pupil

**Its range is the participant's, not the session's.** Each person draws a smallest diametre
out of `PupilMinDomain` and a largest out of `PupilMaxDomain`, once, from `Seed` + their id.
Those two numbers then bound every session that person ever runs — same seat or another,
morning or evening. The report prints them on the `Pupil range of '…'` line, and it is the
one line that reads the same in all four of their files.

**How it moves.** A damped harmonic oscillator driven by noise — a sine wave whose amplitude
and phase are continually knocked about, so it is smooth but never repeats:

```
p'' = -ω²(p - centre) - 2ζω p' + drive
```

Two things follow from it that the previous AR(1)-per-row model could not give:

- **It is smooth.** Position *and* velocity are continuous, so between two adjacent rows the
  pupil moves by a hair. It also carries momentum: about 89 % of steps continue the direction
  of the one before, which is what an iris does and what white noise does not.
- **The row rate does not change it.** The oscillator is integrated in continuous time at a
  fixed internal 240 Hz, so `SampleRateHz` only decides how finely the CSV traces one and the
  same curve. Doubling the rows halves the step between them; the distribution of how far the
  pupil moves *per second* does not budge. The old model stepped ~0.14 mm between rows however
  close together they were, which is exactly the jitter this replaces.

**The two columns are one signal.** `right_PupilDiametre` is `left_PupilDiametre` plus a
jitter of at most ±0.15 mm, clamped into the same anatomical range — the same iris seen by a
second camera, not a second iris. The real captures do show the right eye running 0.29–0.45 mm
wider with the pair correlated 0.67–0.84, and reproducing that put a visible and permanent gap
between the two columns, which is not what this dataset wants. The jitter is drawn per row, so
it is white where the underlying signal is smooth; if the right column ever needs to be as
smooth as the left, give the offset its own slow drift instead of a fresh draw each row.

**The first minute is a baseline, so it is held still.** PluginCAISM takes the median of the
pupil over the first `BaselineSeconds` of every file and subtracts it from every diametre
after — every ΔD in the analysis is measured against that one number. A pupil free to swing
across its whole 3–4 mm range during that minute makes the median a coin toss between the two
ends of the swing, and the sign of every ΔD with it. So while the window lasts the wander is
**squeezed vertically** toward the value the session opened on: the same curve at the same
pace, reaching at most ±0.25 mm either side of it instead of a couple of millimetres.

The opening value itself is still drawn from the stationary distribution — one participant
starts at 5.1 mm and the next at 2.4, exactly as before. What changed is that they stay there
while they are being measured. The squeeze is soft rather than clipped (a `tanh` on the
deviation, scaled so the free wander's half-range maps onto the band), so the held trace keeps
the shape of the free one — its turning points, its momentum, its calm and agitated stretches
— rather than flattening against a wall. When the window closes the held **value becomes the
oscillator's state**: position carries over as it was written and velocity is scaled by the
squeeze's own slope, so both are continuous and the trace simply carries on from where it was
held. No blend, no step, no corner.

## Driven fixations

Once the window closes the simulator computes **the same baseline the reader will** — the
median of the row-mean diametre over the window, which is `CalcularBaselinePupilar`'s rule
transcribed — and from then on every fixation that *starts* is driven **one way only: away
from that baseline**.

- The direction is read off the diametre the fixation's **first row** landed on. Above the
  baseline and it dilates for the whole fixation; below and it constricts. That first row is
  itself free wander — it is what *decides* the direction, so it cannot already be following it.
- Only the **sign** of each step is replaced. `|dp|` is whatever the oscillator produced, so
  the speed ladder, the arousal process and the per-row smoothness are all untouched. The
  velocity is deliberately *not* re-signed with it: doing that lets the restoring pull cancel
  it every substep and the pupil creeps at a fraction of its pace.
- A fixation that reaches the end of the participant's anatomical range **repeats that value**
  until it ends.
- **Between fixations the wander is free and unmodified**, carrying on from wherever the driven
  stretch left it. At the default 1.7 fixations/s most rows of a session are *not* in a
  fixation, so the trace reverses direction often — those reversals are the free stretches,
  not a broken drive.

The point is a dataset where every fixation carries an unambiguous ΔD of one sign instead of
one that averages itself away over the fixation's own rows. Over twenty participants the bench
measures 7006 driven fixations, none of which ever stepped back toward the baseline, with a
median excursion of 0.29 mm; about one in six ends pinned at an anatomical limit.

**What this does to the one-second change distribution.** It moves it, and it has to: rows that
all go the same way cover more ground in a second than rows that cancel, and a fixation pinned
at a wall covers none. The ladder itself is unchanged, so the split is still the fitted one on
an **undriven** session (`BaselineSeconds = 0`) — which is where the bench measures it and where
check 13 still gates it. On a driven session check 13 prints the split as a description of the
file and says it is not gated.

`BaselineSeconds` must be the same number as PluginCAISM's
`LimiaresDePotencialRestaurador.JanelaDeBaselinePupilarFimMs` (60 000 ms). Change it in one
place only and the file is simulating a protocol nobody ran. Check 13 reports how far the
pupil actually strayed inside the window, and the bench asserts it over twenty participants.

**Zero means zero.** `BaselineSeconds` is taken literally, unlike the rates and durations that
read 0 as "unset": 0 means no window, so no hold *and no driven fixations* — the pupil the model
had before any of this existed. An unwired input is a zero. Wire it. The report says which you
got, on its own line:

```
Pupil baseline hold ........ first 60 s held within 0.25 mm of the opening value
Pupil baseline (reader) .... 3.288 mm   median row-mean over the window, the same number PluginCAISM will subtract;
                             149 fixation(s) driven up from it, 195 driven down
```

If the first line reads `OFF`, the pupil wandered freely from the first sample and nothing was
driven — read it before trusting a batch.

> **The CSV is written newest first.** The first rows of the file are the *end* of the session;
> the held minute is at the **bottom**. Sort by `timeStamp` ascending before plotting anything,
> or [check-baseline-hold.ps1](verify/check-baseline-hold.ps1) will do it for you.

**How far it moves.** Range and speed are separate knobs on purpose. Six standard deviations
of the wander span the participant's range, so the extremes are reached now and then rather
than lived at; the speed is set in mm per second by `SimulationDefaults.PupilSpeedLadder`,
the same for everyone, and ω is then solved from the two per participant — so a person with
a wide range does not automatically get a faster pupil. The ladder is read off a slow
arousal process (25 s), which is what makes some stretches near-still and others agitated.

The ladder's nine numbers are **fitted, not measured**: they are what lands the distribution
of |change over one second| on the requested split. Requested against realised, pooled over
twenty participants of an **undriven** session — with a baseline window the first minute is
held and every fixation after it is driven, neither of which is the free random walk these
bands describe:

| change over 1 s | < 0.3 | 0.3–0.7 | 0.7–1.0 | 1.0–1.3 | > 1.3 |
|---|---|---|---|---|---|
| requested | 45 % | 25 % | 15 % | 10 % | 5 % |
| realised | 43 % | 31 % | 13 % | 7 % | 6 % |

That is as close as a smooth process gets — the 0.3–0.7 excess is the shape of a Gaussian
increment and cannot be flattened away without making the trace jagged again. Scaling the
whole ladder scales how much the pupil moves per second without changing the shape of the
split. **Per session the split rattles by ten points or more**, because 300 s holds only about
a dozen independent stretches of arousal; that is why check 13 reports it every run but only
gates it loosely, and why the bench's `PUPIL SIGNAL` section pools twenty participants to
hold it to within 6 points.

## Reading the `Report` output

The report ends with the sixteen acceptance checks of the brief plus four extra
assertions, each marked `PASS` or `FAIL`. Two of them are worth understanding:

- **Check 14 (blink count)** is a Poisson draw, so its standard deviation is
  `sqrt(expected)` however correct the generator is. At the default 0.09 /s over 300 s
  the ±40 % band is only about ±2σ, so it fails by chance roughly one run in thirty. The
  line reports the deviation in sigmas as well; anything under 2σ is noise. Below about
  25 expected blinks the band is narrower than the noise floor and the check stops being
  meaningful.
- **Check 15 (invalid frames)** has a floor. A shut eye produces no gaze, so every blink
  frame is invalid. If `InvalidFrameRate` is set below the fraction of frames the blinks
  already occupy, the check is made against that floor and the report says so. Lower
  `BlinkRatePerSecond` to go below it.
- **Check 13 (pupils)** gates hard on the things that are exact — inside the participant's
  own range, `-1` on every blink frame, a smooth step between rows, momentum, staying within
  ±0.25 mm of the opening value for the whole baseline window, and **every driven fixation
  moving away from the baseline and never back**, which is named and not just counted when it
  fails. The one-second distribution is gated only on an undriven session, and reported as a
  description otherwise. See the two pupil sections above.

Lines starting with `!!` at the top of the report are **warnings that a parameter was
overridden**. Read them — they mean the session you got is not quite the one you asked for,
and they say what to change.

---

## The three states of the gaze

Session time is spent in one of three states, and the `Session composition` line of the
report shows the split:

1. **Fixating** — the gaze direction is held for a drawn duration, moving only by the
   intra-fixation micro-jitter. This is the only state that produces a fixation.
2. **Approach** — the saccade onto the next fixation plus its corrective movements, up to
   0.6 s at the end of each gap.
3. **Free scanning** — everything left over. The eye hops to a new star vector on *every*
   sample along a random walk, never dwelling long enough to register as a fixation.

That last state is what makes a low fixation count mean "the eye moves constantly and only
settles a few times" rather than "the eye stares at three things for five minutes each".
`FixationCountMin/Max` therefore controls **how many times the eye stops**, not how much of
the session it spends moving. Durations are never stretched to fill the session — the
leftover time becomes free scanning instead.

When the requested fixations nearly fill the session (the measured defaults do: ~510
fixations over 300 s), free scanning collapses to zero samples and the behaviour is the
classic one.

Two consequences worth knowing:

- **Free-scanning hops are floored at the I-VT step size** (~2.5° at 17 Hz). A hop smaller
  than that would read as a stationary sample, and two in a row would invent a fixation. So
  a very small `SaccadeAmplitudeMinDeg` has no effect during free scanning.
- **`Movements made` is the honest saccade-amplitude statistic**, not `Fixation-to-fixation`.
  Once free scanning exists, two consecutive fixations are separated by an entire random
  walk rather than by one saccade, so the distance between them says nothing about the
  amplitude distribution.

## If the session barely moves

1. **`FixationCountMin` / `FixationCountMax` are the count for the *whole session*, not per
   second**, and they silently override `FixationRatePerSecond`. Leave both at `0` unless
   you specifically want a fixed count.
2. For a busy session raise `FixationRatePerSecond` (try 3–4) with the count range at zero.
3. To make the jumps bigger, raise `SaccadeAmplitudeLogMu`. Note that raising
   `SaccadeAmplitudeMinDeg` also lifts the median because it truncates the distribution; the
   report shows both the truncated and untruncated medians.

---

## Running the offline test bench

Needs the .NET SDK, not Rhino. It extracts the *custom additional code* region straight
out of `GazeSessionSimulator.cs`, compiles it with `LangVersion 7.3` against stub
`Vector3d`/`Point3d` types, builds a synthetic auditorium and star, and runs everything.

```bash
powershell -ExecutionPolicy Bypass -File grasshopper/verify/run-verify.ps1
```

It prints three sections:

1. the generator's own report and self-check;
2. a byte-level format comparison against `EyeGazingData.csv` and `EyeGazingData2.csv`;
3. a sweep over eight seeds and eleven parameter configurations, plus a 40-seed test that
   the blink count is unbiased.

Because it pins the language version to 7.3, it will also catch any accidental use of a
C# 8+ feature that Rhino 7 would reject.
