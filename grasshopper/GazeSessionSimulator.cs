// =====================================================================================
//  GazeSessionSimulator.cs
//
//  Grasshopper C# script component (Rhino 7 / C# 7.3 / .NET Framework 4.8) that
//  synthesises plausible VR eye-tracking sessions and writes them out in exactly the
//  same CSV dialect as the real HTC Vive Focus Vision captures.
//
//  One solution writes the WHOLE STUDY: 26 participants, each with a fixed seat, period of
//  the day and position in the queue, crossed with 3 scenes and 3 rounds a month apart -
//  234 sessions, scheduled so that none of them overlap. Each session writes TWO files: the
//  gaze CSV, and an environment CSV carrying temperature, humidity, CO and noise on exactly
//  the same timestamps.
//
//  The protocol - the roster, the scenes, the rounds, the schedule, the shape of the
//  environment signals - is all in StudyConfiguration at the top of REGION 2, not on the
//  canvas. The canvas only SELECTS which of the planned sessions get written: the full
//  study is planned on every solve and the selection applied afterwards, so a session's
//  instant, seed and path come from its identity rather than from its position in the run.
//  Re-running one participant therefore reproduces the file the full run produced.
//
//  HOW TO USE THIS FILE
//  --------------------
//  The file is split into two clearly marked regions:
//
//    REGION 1  ->  paste into the body of the component's RunScript method.
//    REGION 2  ->  paste into the "<Custom additional code>" region at the bottom
//                  of the script component (below RunScript, inside the class).
//
//  See README.md in this folder for the ordered list of inputs to create on the
//  component, their type hints and their access (item / list).
//
//  Disk access is exactly three things: the seat JSON is read (and cached), and the two
//  CSVs are written. Folders below OutputFolder are created as needed. Nothing else.
// =====================================================================================


// #####################################################################################
// ##  REGION 1 - RunScript body                                                      ##
// ##  The signature below is what Grasshopper generates once the inputs of README.md ##
// ##  have been added in order. Copy only the statements inside the braces.          ##
// #####################################################################################
#region RunScriptRegion
/*
private void RunScript(
    string JsonFolder, List<string> ParticipantIds, List<string> Scenes, List<int> Rounds,
    List<string> ClimateTable, int PreviewIndex,
    bool ConvertZUpToYUp,
    int Seed,
    int FixationCountMin, int FixationCountMax, double FixationRatePerSecond,
    double FixationDurationMinSec, double FixationDurationMaxSec,
    double FixationDurationLogMu, double FixationDurationLogSigma,
    double IntraFixationDispersionDeg,
    double SaccadeAmplitudeLogMu, double SaccadeAmplitudeLogSigma,
    double SaccadeAmplitudeMinDeg, double SaccadeAmplitudeMaxDeg,
    double ReturnBiasProbability, int DistinctObjectsMin, int DistinctObjectsMax,
    double NeckYawLimitDeg, double NeckPitchUpLimitDeg, double NeckPitchDownLimitDeg,
    double EyeYawLimitDeg, double EyePitchLimitDeg, double HeadTimeConstantSec,
    double InvalidFrameRate, double BlinkRatePerSecond,
    Interval PupilMinDomain, Interval PupilMaxDomain, double BaselineSeconds,
    string DeviceName, string OutputFolder, bool Run,
    ref object CsvPath, ref object EnvCsvPath, ref object Report, ref object SceneNames,
    ref object TimeStamps, ref object Rodadas,
    ref object ScanPath, ref object FixationPoints, ref object FixationDurations,
    ref object HeadDirections, ref object HitPoints, ref object GazeRays)
{
*/

// ---- begin RunScript body -----------------------------------------------------------

    if (!Run)
    {
        Report = "Run is false. Set Run = true to generate and write the batch.";
        return;
    }

    // Everything below the metadata is one template: the behaviour of a session, identical
    // across the batch. The per-session fields - which seat file, which participant, which
    // period of the day, the start instant, the seed and the output path - are stamped on
    // by BatchRunner, one copy per session.
    SimulationSettings settings = new SimulationSettings();

    // Rhino is Z-up; the CSV must be Y-up, because that is what the Unity logger wrote and
    // what the Revit plugin's reader expects. The JSON holds ordinary Rhino world
    // coordinates, so this is normally true.
    settings.ConvertZUpToYUp = ConvertZUpToYUp;

    // Session length and sample rate are NOT inputs any more. They are part of the protocol:
    // changing either one invalidates every file already written, so they live next to the
    // roster and the schedule in StudyConfiguration where that is impossible to miss.
    settings.SessionSeconds = StudyConfiguration.SessionSeconds;
    settings.SampleRateHz = StudyConfiguration.SampleRateHz;
    settings.Seed = Seed;

    // Behaviour.
    settings.FixationCountMin = FixationCountMin;
    settings.FixationCountMax = FixationCountMax;
    settings.FixationRatePerSecond = SimulationSettings.DefaultIfZero(FixationRatePerSecond, SimulationDefaults.FixationRatePerSecond);
    settings.FixationDurationMinSec = SimulationSettings.DefaultIfZero(FixationDurationMinSec, SimulationDefaults.FixationDurationMinSeconds);
    settings.FixationDurationMaxSec = SimulationSettings.DefaultIfZero(FixationDurationMaxSec, SimulationDefaults.FixationDurationMaxSeconds);
    settings.FixationDurationLogMu = SimulationSettings.DefaultIfZero(FixationDurationLogMu, SimulationDefaults.FixationDurationLogMu);
    settings.FixationDurationLogSigma = SimulationSettings.DefaultIfZero(FixationDurationLogSigma, SimulationDefaults.FixationDurationLogSigma);
    settings.IntraFixationDispersionDeg = SimulationSettings.DefaultIfZero(IntraFixationDispersionDeg, SimulationDefaults.IntraFixationDispersionDegrees);
    settings.SaccadeAmplitudeLogMu = SimulationSettings.DefaultIfZero(SaccadeAmplitudeLogMu, SimulationDefaults.SaccadeAmplitudeLogMu);
    settings.SaccadeAmplitudeLogSigma = SimulationSettings.DefaultIfZero(SaccadeAmplitudeLogSigma, SimulationDefaults.SaccadeAmplitudeLogSigma);
    settings.SaccadeAmplitudeMinDeg = SimulationSettings.DefaultIfZero(SaccadeAmplitudeMinDeg, SimulationDefaults.SaccadeAmplitudeMinDegrees);
    settings.SaccadeAmplitudeMaxDeg = SimulationSettings.DefaultIfZero(SaccadeAmplitudeMaxDeg, SimulationDefaults.SaccadeAmplitudeMaxDegrees);
    settings.ReturnBiasProbability = ReturnBiasProbability;               // 0 is a meaningful value
    settings.DistinctObjectsMin = DistinctObjectsMin;                     // 0 disables the steering
    settings.DistinctObjectsMax = DistinctObjectsMax;
    settings.NeckYawLimitDeg = SimulationSettings.DefaultIfZero(NeckYawLimitDeg, SimulationDefaults.NeckYawLimitDegrees);
    settings.NeckPitchUpLimitDeg = SimulationSettings.DefaultIfZero(NeckPitchUpLimitDeg, SimulationDefaults.NeckPitchUpLimitDegrees);
    settings.NeckPitchDownLimitDeg = SimulationSettings.DefaultIfZero(NeckPitchDownLimitDeg, SimulationDefaults.NeckPitchDownLimitDegrees);
    settings.EyeYawLimitDeg = SimulationSettings.DefaultIfZero(EyeYawLimitDeg, SimulationDefaults.EyeYawLimitDegrees);
    settings.EyePitchLimitDeg = SimulationSettings.DefaultIfZero(EyePitchLimitDeg, SimulationDefaults.EyePitchLimitDegrees);
    settings.HeadTimeConstantSec = SimulationSettings.DefaultIfZero(HeadTimeConstantSec, SimulationDefaults.HeadTimeConstantSeconds);
    settings.InvalidFrameRate = InvalidFrameRate;                         // 0 is a meaningful value
    settings.BlinkRatePerSecond = BlinkRatePerSecond;                     // 0 is a meaningful value
    // Two domains, not two numbers: each participant draws their own smallest and largest
    // pupil out of these, once, and keeps them for every session of the batch.
    settings.PupilMinimumDomain = SimulationSettings.DefaultIfEmpty(PupilMinDomain,
        SimulationDefaults.PupilMinimumDomainLow, SimulationDefaults.PupilMinimumDomainHigh);
    settings.PupilMaximumDomain = SimulationSettings.DefaultIfEmpty(PupilMaxDomain,
        SimulationDefaults.PupilMaximumDomainLow, SimulationDefaults.PupilMaximumDomainHigh);
    // The consumer's baseline window. It has to match PluginCAISM's, which is 60 s; change it
    // in both places or neither. Taken literally, including zero: 0 means no baseline window,
    // so no hold and no driven fixations, and the report's "Pupil baseline hold" line says
    // OFF. Wire it.
    settings.BaselineSeconds = BaselineSeconds;

    // The only metadata that is the same for every session of the batch. The participant,
    // the seat, the scene, the two composed names and the start instant are all per
    // session, and BatchRunner fills them in - see SessionNaming for the two it composes.
    settings.DeviceName = SimulationSettings.DefaultIfEmpty(DeviceName, SimulationDefaults.DeviceName);

    BatchRequest request = new BatchRequest();
    request.Template = settings;
    request.JsonFolder = JsonFolder;
    // The three selection lists. None of them changes the PLAN - the plan is always the
    // complete 3 rounds x 3 scenes x 26 participants - they only decide which of its
    // sessions are actually simulated and written. That is what makes a re-run of one
    // participant land on exactly the timestamp it had in the full run.
    request.SelectedParticipantIds = ParticipantIds;
    request.SelectedScenes = Scenes;
    request.SelectedRounds = Rounds;
    // Day-of-year -> mean temperature and humidity, morning and night. Raw data, not a
    // model: see the ClimateTable Python component that feeds this input.
    request.ClimateRows = ClimateTable;
    request.PreviewIndex = PreviewIndex;
    request.OutputFolder = OutputFolder;
    request.BaseSeed = Seed;

    BatchResult batch = new BatchRunner().Run(request);

    Report = batch.ReportText;
    CsvPath = batch.CsvPaths;
    EnvCsvPath = batch.EnvironmentCsvPaths;
    SceneNames = batch.SceneNames;
    TimeStamps = batch.TimeStamps;
    Rodadas = batch.Rounds;

    if (!batch.Succeeded)
    {
        // Loud but not an exception dump: the report explains exactly what is wrong.
        Component.AddRuntimeMessage(GH_RuntimeMessageLevel.Error, batch.FailureMessage);
        if (batch.Preview == null || !batch.Preview.Succeeded) return;
    }

    // The geometry outputs show ONE session - the one selected by PreviewIndex. Keeping all
    // of them would put a million points on the canvas for a batch of a hundred.
    SimulationResult result = batch.Preview;
    if (result == null) return;

    FixationPoints = result.FixationCentroids;
    FixationDurations = result.FixationDurations;
    HeadDirections = result.HeadForwardPerFixation;

    Polyline scanPathPolyline = new Polyline();
    for (int i = 0; i < result.FixationCentroids.Count; i++)
        scanPathPolyline.Add(result.FixationCentroids[i]);
    ScanPath = scanPathPolyline;

    // One entry per emitted sample, in CHRONOLOGICAL order - that is the reverse of the
    // CSV, which is written newest first. Item i here is CSV data line (count - i).
    HitPoints = result.HitPoints;

    // The gaze ray a downstream consumer reconstructs from the file: recorded player
    // position to recorded hit point. The eye position drifts with the postural sway, so
    // it is taken per sample rather than from the fixed EyeOrigin.
    List<Line> gazeRayLines = new List<Line>(result.HitPoints.Count);
    for (int i = 0; i < result.HitPoints.Count; i++)
        gazeRayLines.Add(new Line(result.EyePositions[i], result.HitPoints[i]));
    GazeRays = gazeRayLines;

// ---- end RunScript body -------------------------------------------------------------
/*
}
*/
#endregion


// #####################################################################################
// ##  REGION 2 - Custom additional code                                              ##
// ##  Paste everything below into the "<Custom additional code>" region.             ##
// ##                                                                                 ##
// ##  Required using directives at the top of the component (the template does NOT   ##
// ##  include Linq or Globalization):                                                ##
// ##      using System;                                                              ##
// ##      using System.Collections.Generic;                                          ##
// ##      using System.Globalization;                                                ##
// ##      using System.IO;                                                           ##
// ##      using System.Linq;                                                         ##
// ##      using System.Text;                                                         ##
// ##      using Rhino.Geometry;                                                      ##
// #####################################################################################
#region AdditionalCodeRegion

// #####################################################################################
// ##                                                                                 ##
// ##   S T U D Y   C O N F I G U R A T I O N                                         ##
// ##                                                                                 ##
// ##   Everything that defines the PROTOCOL rather than the behaviour of an eye.     ##
// ##   None of it is a component input, on purpose: change any of it and every file  ##
// ##   already on disk is from a different study, so all participants, all seats,    ##
// ##   all scenes and all rounds have to be generated again.                         ##
// ##                                                                                 ##
// #####################################################################################

/// <summary>One period of the day, and the window of hours a session may start in.</summary>
public struct DayPeriodWindow
{
  public readonly string Label;
  public readonly int StartHour;          // inclusive
  public readonly int EndHour;            // exclusive; 24 means midnight
  public readonly bool IsNight;           // picks which pair of columns of the climate table applies

  public DayPeriodWindow(string label, int startHour, int endHour, bool isNight)
  {
    Label = label; StartHour = startHour; EndHour = endHour; IsNight = isNight;
  }
}

/// <summary>
/// One participant's fixed place in the study. The period of the day, the seat and the
/// position in the queue are properties of the PERSON, not of the run: they are the same
/// in every scene and in every round, and they never depend on the seed or on how many
/// participants were ticked on the canvas.
/// </summary>
public struct ParticipantAssignment
{
  public readonly int Order;              // 1-based position in the queue of the day
  public readonly string Id;
  public readonly string Seat;
  public readonly string DayPeriod;

  public ParticipantAssignment(int order, string id, string seat, string dayPeriod)
  {
    Order = order; Id = id; Seat = seat; DayPeriod = dayPeriod;
  }
}

public static class StudyConfiguration
{
  // -----------------------------------------------------------------------------------
  //  SCHEDULE
  // -----------------------------------------------------------------------------------

  /// <summary>The first day of round 1. Every other instant in the study is derived from it.</summary>
  public const string StartingDay = "2026-01-01";

  /// <summary>Idle time between the end of one session and the start of the next.</summary>
  public const double IntervalMinutes = 30.0;

  /// <summary>How long one session lasts. Was an input; it is protocol, so it is not any more.</summary>
  public const double SessionSeconds = 300.0;

  /// <summary>Emitted samples per second. Also protocol - the real Unity logger ran at 17 Hz.</summary>
  public const double SampleRateHz = 17.0;

  /// <summary>Each participant walks through every scene this many times.</summary>
  public const int RoundCount = 3;

  /// <summary>Round n starts this many months after round n-1, on the same day of the month.</summary>
  public const int MonthsBetweenRounds = 1;

  /// <summary>
  /// Two windows, and every participant belongs to exactly one of them for the whole study.
  /// </summary>
  public static readonly DayPeriodWindow[] DayPeriods =
    {
    new DayPeriodWindow("Dia", 8, 12, false),
    new DayPeriodWindow("Noite", 20, 24, true)
    };

  // -----------------------------------------------------------------------------------
  //  THE MODEL
  // -----------------------------------------------------------------------------------

  /// <summary>
  /// Every scene of the study, in the order they are run. The Scenes input SELECTS from
  /// this list; it cannot extend it, and leaving a scene out does not shift the days of
  /// the ones that are left in.
  /// </summary>
  public static readonly string[] Scenes = { "01", "02", "03" };

  /// <summary>
  /// How a seat file is named inside JsonFolder: {0} is the scene, {1} is the seat, so
  /// scene "01" seat "A1" is "01_A1.json".
  /// </summary>
  public const string SeatFileNameFormat = "{0}_{1}.json";

  // -----------------------------------------------------------------------------------
  //  THE ROSTER
  //
  //  26 participants, 14 seats, two periods of the day. Fixed for the whole study:
  //
  //    * Order      the position in that period's queue. Participant 7580 is the 8th, so
  //                 it starts 7 slots after the window opens EVEN IF it is the only
  //                 participant ticked on the canvas.
  //    * Seat       seat = ((Order - 1) mod 14), so orders 1..14 fill A1..B7 once and
  //                 orders 15..26 fill A1..B5 a second time. B6 and B7 carry one
  //                 participant each; every other seat carries two.
  //    * DayPeriod  orders 1..13 are morning, orders 14..26 are night - 13 and 13. The
  //                 split falls where it does so that the two participants sharing a seat
  //                 are never in the same period: every doubled seat is measured once in
  //                 the morning and once at night.
  // -----------------------------------------------------------------------------------
  public static readonly ParticipantAssignment[] Roster =
    {
    new ParticipantAssignment( 1, "6710", "A1", "Dia"),
    new ParticipantAssignment( 2, "3845", "A2", "Dia"),
    new ParticipantAssignment( 3, "9012", "A3", "Dia"),
    new ParticipantAssignment( 4, "1388", "A4", "Dia"),
    new ParticipantAssignment( 5, "5249", "A5", "Dia"),
    new ParticipantAssignment( 6, "4056", "A6", "Dia"),
    new ParticipantAssignment( 7, "6105", "A7", "Dia"),
    new ParticipantAssignment( 8, "7580", "B1", "Dia"),
    new ParticipantAssignment( 9, "1943", "B2", "Dia"),
    new ParticipantAssignment(10, "5567", "B3", "Dia"),
    new ParticipantAssignment(11, "3219", "B4", "Dia"),
    new ParticipantAssignment(12, "8094", "B5", "Dia"),
    new ParticipantAssignment(13, "6421", "B6", "Dia"),
    new ParticipantAssignment(14, "8950", "B7", "Noite"),
    new ParticipantAssignment(15, "4128", "A1", "Noite"),
    new ParticipantAssignment(16, "7304", "A2", "Noite"),
    new ParticipantAssignment(17, "3047", "A3", "Noite"),
    new ParticipantAssignment(18, "6953", "A4", "Noite"),
    new ParticipantAssignment(19, "8521", "A5", "Noite"),
    new ParticipantAssignment(20, "4976", "A6", "Noite"),
    new ParticipantAssignment(21, "1533", "A7", "Noite"),
    new ParticipantAssignment(22, "2608", "B1", "Noite"),
    new ParticipantAssignment(23, "6294", "B2", "Noite"),
    new ParticipantAssignment(24, "5087", "B3", "Noite"),
    new ParticipantAssignment(25, "8165", "B4", "Noite"),
    new ParticipantAssignment(26, "4390", "B5", "Noite")
    };

  // -----------------------------------------------------------------------------------
  //  OUTPUT LAYOUT
  //
  //  OutputFolder / Rodada_1 / 01 / 6710_01_A1_Dia_R1.csv
  //                              / 6710_01_A1_Dia_R1_AMBIENTE.csv
  //
  //  Missing folders are created. The round is in the file name as well as in the folder
  //  so that the three rounds survive being merged into one directory later.
  // -----------------------------------------------------------------------------------
  /// <summary>{0:00} zero-pads, so round 1 sorts before round 10 in Explorer.</summary>
  public const string RoundFolderFormat = "RODADA {0:00}";

  /// <summary>{0} is the scene as it is named in Scenes, which already carries its zero.</summary>
  public const string SceneFolderFormat = "CENÁRIO {0}";

  /// <summary>The two files of a session are split into a folder each, inside the scene.</summary>
  public const string GazeFolderName = "HTC";
  public const string EnvironmentFolderName = "AMBIENTE";

  public const bool IncludeRoundInFileName = true;
  public const string RoundFileSuffixFormat = "_R{0}";
  public const string EnvironmentFileSuffix = "_AMBIENTE";

  // -----------------------------------------------------------------------------------
  //  ENVIRONMENT CSV - the dialect
  //
  //  A CSV carries no types; the reader guesses. The only thing that decides whether it
  //  guesses RIGHT is whether the file matches the separators the reader's locale expects.
  //  These three are set for pt-BR Excel, which wants ';' between fields and ',' inside a
  //  number. Set them to "," and "." for an English-locale Excel, pandas or R.
  //
  //  This is the ENVIRONMENT file only. The gaze CSV is a stand-in for a real HTC capture
  //  and PluginCAISM parses it; its dialect is fixed by that contract, not by convenience.
  // -----------------------------------------------------------------------------------
  public const string EnvironmentFieldDelimiter = ";";
  public const string EnvironmentDecimalSeparator = ",";
  public const bool EnvironmentByteOrderMark = true;

  // -----------------------------------------------------------------------------------
  //  ENVIRONMENT CSV - the columns that never vary
  // -----------------------------------------------------------------------------------
  public const string EnvironmentDeviceName = "MM-11";
  public const string EnvironmentNetworkDns = "181.213.132.2";
  public const string EnvironmentMacAddress = "EC:64:C9:CD:D2:29";
  public const string EnvironmentPowerState = "Power On";

  // -----------------------------------------------------------------------------------
  //  ENVIRONMENT CSV - the shape of the simulated signals
  //
  //  The day's MEAN temperature and humidity come from the climate table, so two sessions
  //  recorded on the same day agree with each other no matter which run produced them.
  //  Everything here describes only how far a single session may wander from that mean.
  // -----------------------------------------------------------------------------------

  //  Temperature, humidity and the noise floor all move the same way: ONE SLICE OF ONE SINE
  //  WAVE, stretched over the session. A random start phase and a random span - never wider
  //  than a single period - pick the slice, and the slice is then rescaled to a peak-to-peak
  //  drawn for that session. One rule covers every honest scenario: a slow climb, a slow
  //  fall, a rise that turns over, a dip that recovers, or a room that barely moves at all.
  //  What it cannot produce is what a random walk produces by default - a signal that
  //  changes its mind hundreds of times in five minutes.
  //
  //  Because the curve is a sine, the rate is a cosine: every turn is smooth, there are at
  //  most two of them in a session, and the flat stretches near the crests are where "the
  //  room held still for a while" comes from without anyone coding a hold.
  // -----------------------------------------------------------------------------------

  /// <summary>The slice of the wave a session may use. A whole period is the widest.</summary>
  public const double SignalSpanMinRadians = 0.35;
  public const double SignalSpanMaxRadians = 2.0 * Math.PI;

  /// <summary>A session opens within this much of the day's mean temperature.</summary>
  public const double TemperatureStartDeviationC = 1.0;
  /// <summary>Peak-to-peak for the session, drawn per session. Small means a still room.</summary>
  public const double TemperatureExcursionMinC = 0.10;
  public const double TemperatureExcursionMaxC = 1.00;
  /// <summary>Hard ceiling on |dTemperature/dt|. The excursion is shrunk to respect it.</summary>
  public const double TemperatureMaxChangePerMinuteC = 0.25;

  public const double HumidityStartDeviationPercent = 3.0;
  public const double HumidityExcursionMinPercent = 0.40;
  public const double HumidityExcursionMaxPercent = 2.20;
  public const double HumidityMaxChangePerMinutePercent = 0.5;
  /// <summary>Never outside these, whatever the table and the wave say.</summary>
  public const double HumidityFloorPercent = 35.0;
  public const double HumidityCeilingPercent = 85.0;

  /// <summary>CO is a flag, not a level: an even coin per sample, no pattern of any kind.</summary>
  public const double CarbonMonoxideHighProbability = 0.5;

  // -----------------------------------------------------------------------------------
  //  NOISE - three layers that add up, and only the third one is loud.
  //
  //  1. A floor: the room's own hum. A sine slice like the two above, so it drifts by about
  //     a decibel across the session and never jumps.
  //  2. A jitter on EVERY sample - a couple of tenths of a decibel, +-0.5 dB at the far
  //     edge. This is the microphone breathing, not the room changing. It is the only layer
  //     that is allowed to differ from one sample to the next.
  //  3. Events, whose rise, body and fall are measured in SECONDS and never in samples. Two
  //     envelopes cover what a room does: something switches on, sits there, switches off;
  //     or something passes by and its level draws a bell. They may overlap, because a room
  //     does not wait for one sound to finish before making the next.
  // -----------------------------------------------------------------------------------

  /// <summary>The quiet band the floor sits in, before jitter and before any event.</summary>
  public const double NoiseBaseFloorDb = 43.0;
  public const double NoiseBaseCeilingDb = 62.0;
  public const double NoiseBaseExcursionMinDb = 0.3;
  public const double NoiseBaseExcursionMaxDb = 1.5;

  /// <summary>Always on. Gaussian, so most samples barely move and +-0.5 dB is the rare edge.</summary>
  public const double NoiseJitterSigmaDb = 0.20;
  public const double NoiseJitterMaxDb = 0.5;

  /// <summary>How often anything happens, and how often that anything is a pass-by.</summary>
  public const double NoiseEventsPerMinute = 0.6;
  public const double NoisePassByShare = 0.45;

  /// <summary>Switched on, held, switched off. The excess is exponential: louder is rarer.</summary>
  public const double NoiseSustainedMinExcessDb = 3.0;
  public const double NoiseSustainedExcessMeanDb = 3.0;
  public const double NoiseSustainedMaxExcessDb = 12.0;
  public const double NoiseSustainedRiseMinSeconds = 0.5;
  public const double NoiseSustainedRiseMaxSeconds = 3.0;
  public const double NoiseSustainedHoldMinSeconds = 5.0;
  public const double NoiseSustainedHoldMaxSeconds = 30.0;
  public const double NoiseSustainedFallMinSeconds = 0.5;
  public const double NoiseSustainedFallMaxSeconds = 4.0;

  /// <summary>A pass-by has no body at all: the rise and the fall meet at the peak.</summary>
  public const double NoisePassByMinExcessDb = 8.0;
  public const double NoisePassByExcessMeanDb = 6.0;
  public const double NoisePassByMaxExcessDb = 28.0;
  public const double NoisePassByMinSpanSeconds = 6.0;
  public const double NoisePassByMaxSpanSeconds = 20.0;

  public const double NoiseAbsoluteFloorDb = 35.0;
  public const double NoiseAbsoluteCeilingDb = 90.0;

  // -----------------------------------------------------------------------------------
  //  Lookups over the tables above.
  // -----------------------------------------------------------------------------------
  public static bool TryFindPeriod(string label, out DayPeriodWindow window)
  {
    for (int i = 0; i < DayPeriods.Length; i++)
      if (string.Equals(DayPeriods[i].Label, label, StringComparison.Ordinal))
      { window = DayPeriods[i]; return true; }
    window = default(DayPeriodWindow);
    return false;
  }

  /// <summary>Session plus interval: the pitch of the queue, and the width of one slot.</summary>
  public static double SlotSeconds { get { return SessionSeconds + IntervalMinutes * 60.0; } }

  public static string SeatFileName(string scene, string seat)
  {
    return string.Format(CultureInfo.InvariantCulture, SeatFileNameFormat, scene, seat);
  }

  /// <summary>1-based, so round 0 of the plan reads "Rodada_1" everywhere a human sees it.</summary>
  public static string RoundFolderName(int roundIndex)
  {
    return string.Format(CultureInfo.InvariantCulture, RoundFolderFormat, roundIndex + 1);
  }

  public static string SceneFolderName(string scene)
  {
    return string.Format(CultureInfo.InvariantCulture, SceneFolderFormat, scene);
  }

  public static string RoundFileSuffix(int roundIndex)
  {
    return IncludeRoundInFileName
      ? string.Format(CultureInfo.InvariantCulture, RoundFileSuffixFormat, roundIndex + 1)
      : "";
  }
}

// -------------------------------------------------------------------------------------
//  Constants. Every number here is traceable to a statistic measured from the two real
//  capture files; the comment names which one.
// -------------------------------------------------------------------------------------
public static class SimulationDefaults
{
    // Sampling. Real mean interval was 56-61 ms => 16-18 Hz; this is a Unity Update()
    // loop, not the tracker's native rate.
    public const double SessionSeconds = 300.0;
    public const double SampleRateHz = 17.0;
    public const double SampleIntervalJitterFraction = 0.15;   // real intervals ran 45-81 ms

    // The window the CONSUMER measures its pupil baseline over. PluginCAISM takes the median
    // of the mean diametre across the first 60 s of every session and subtracts it from
    // everything after (LimiaresDePotencialRestaurador.JanelaDeBaselinePupilarFimMs); this
    // number has to be the same one, or the file is simulating a protocol nobody ran.
    // See PupilSignal for what the pupil does while it lasts.
    public const double BaselineSeconds = 60.0;

    // Fixations (vector-defined). I-VT at 30 deg/s, minimum 100 ms.
    public const double FixationRatePerSecond = 1.7;           // 1.63 /s and 1.81 /s
    public const double FixationDurationLogMu = -1.2;          // mu -0.96 and -1.50 (ln seconds)
    public const double FixationDurationLogSigma = 0.7;        // sigma 0.83 and 0.61
    public const double FixationDurationMinSeconds = 0.10;     // 10th pct was 0.11-0.13 s
    public const double FixationDurationMaxSeconds = 2.50;     // observed max 0.98-2.45 s
    public const double IntraFixationDispersionDegrees = 1.3;  // 95th pct radius 1.01-1.63 deg

    // Saccades.
    public const double SaccadeAmplitudeLogMu = 1.4;           // mu 1.27 and 1.51 (ln degrees)
    public const double SaccadeAmplitudeLogSigma = 0.6;        // sigma 0.54 and 0.71
    public const double SaccadeAmplitudeMinDegrees = 0.5;
    public const double SaccadeAmplitudeMaxDegrees = 60.0;
    public const double ReturnBiasProbability = 0.30;          // A->B->A returns were 28-32 %

    // Saccades written as pauses. A saccade is a MOVEMENT, so nothing in the gaze path marks
    // where one was; a consumer can only see it as a short stretch during which the gaze was
    // not somewhere else. So each transition writes its saccades as stretches that repeat one
    // hit point verbatim, which also survives a consumer grouping frames by an ABSOLUTE
    // distance rather than an angular one - PluginCAISM uses 0.01 m, a tolerance no star of
    // realistic density can meet while the gaze is still moving.
    //
    // Saccades outnumber fixations in every real session, and not by accident: reaching the
    // next fixation takes one saccade by definition, and overshoot makes it take one or two
    // more. That is why the minimum is 1 per transition rather than a probability - anything
    // less would put fewer saccades than fixations in the file, which never happens in
    // recorded data. Measured reference: 18 fixations against 29 saccades over two minutes.
    public const int SaccadeHoldsPerTransitionMin = 1;
    public const int SaccadeHoldsPerTransitionMax = 3;

    // Real saccades last 30-80 ms, which is also the window PluginCAISM classifies on. Widen
    // this and the extra length lands in that consumer's dead zone - above its 80 ms saccade
    // ceiling, below its 300 ms fixation floor - where the pause is written but never counted.
    public const double SaccadeHoldMinSeconds = 0.03;
    public const double SaccadeHoldMaxSeconds = 0.08;

    // Head / eye division of labour, measured over 0.3 s windows.
    public const double HeadFractionBase = 0.40;               // ~40 % for shifts below 5 deg
    public const double HeadFractionPerDegree = 0.004;         // rises to ~55-60 % above 20 deg
    public const double HeadFractionCeiling = 0.65;
    public const double HeadFractionNoiseSd = 0.10;            // per-saccade noise
    public const double HeadTimeConstantSeconds = 0.30;        // head settles over 0.2-0.4 s
    public const double HeadRollSdDegrees = 2.0;               // roll is essentially zero
    public const double HeadRollTimeConstantSeconds = 0.5;

    // Neck and eye budgets. The eye limit is behavioural, not anatomical: the physical
    // eye reaches +-35 deg but measured 1st-99th percentile eye-in-head was -19..+22 deg.
    public const double NeckYawLimitDegrees = 90.0;
    public const double NeckPitchUpLimitDegrees = 65.0;
    public const double NeckPitchDownLimitDegrees = 65.0;
    public const double EyeYawLimitDegrees = 20.0;
    public const double EyePitchLimitDegrees = 20.0;

    // Seat sway. Real postural drift was 3-8 cm per axis, slow, not white noise.
    public const double PlayerPositionDriftSdMetres = 0.05;
    public const double PlayerPositionDriftTimeConstantSeconds = 4.0;

    // Pupil. See PupilSignal for the model; these are its constants.
    //
    // The anatomical range is per PARTICIPANT, not per session: one draw from each domain,
    // made from the participant seed, so the same person keeps the same eye across every
    // seat and both periods of the day. These two domains are what the component's
    // PupilMinDomain / PupilMaxDomain inputs default to when left unwired.
    public const double PupilMinimumDomainLow = 1.6;
    public const double PupilMinimumDomainHigh = 2.0;
    public const double PupilMaximumDomainLow = 4.5;
    public const double PupilMaximumDomainHigh = 6.0;
    // A participant whose two draws leave less than this between them is widened; the
    // model needs room to move and the domains could in principle be set to overlap.
    public const double PupilMinimumSpanMillimetres = 1.0;

    // The two eyes are ONE signal. The right pupil is the left one plus a small jitter,
    // clamped to the same anatomical range - it is the same iris seen by a second camera,
    // not a second iris. (The real captures do show the right eye running 0.29-0.45 mm
    // wider than the left, with the pair correlated 0.67-0.84; reproducing that put a
    // visible gap between the columns, which is not what this dataset wants.)
    public const double PupilRightEyeJitterMillimetres = 0.15;
    public const double PupilInvalidMarker = -1.0;             // the logger's "no data" value

    // Six standard deviations of the wander span the participant's whole range, so the
    // extremes are reached occasionally rather than lived at.
    public const double PupilRangeInStandardDeviations = 6.0;
    // Damping of the second-order wander. Below 1 it is a sine wave whose amplitude and
    // phase keep being knocked about - smooth, but never repeating.
    public const double PupilDamping = 0.5;
    // How slowly the person's arousal drifts, which is what makes a quiet minute quiet and
    // an agitated one agitated.
    public const double PupilArousalTimeConstantSeconds = 25.0;
    // The process is integrated at this fixed rate no matter what SampleRateHz is, so the
    // CSV always samples the SAME underlying curve - more rows means a finer trace of it,
    // never a different one.
    public const double PupilIntegrationRateHz = 240.0;

    // The baseline window, in the pupil. While the consumer is measuring its baseline the
    // wander is squeezed VERTICALLY toward the value the session opened on: the same curve,
    // same speed of change, reaching at most this far either side of it. A baseline measured
    // over a minute in which the pupil had already swung across its whole range is not a
    // baseline, and the median it produces is a coin toss between the two ends of that swing.
    public const double PupilBaselineExcursionMillimetres = 0.25;

    // Speed ladder: the sd of the one-second move, as a function of the arousal quantile
    // (knot 0 = the calmest the person gets, last knot = the most agitated), linearly
    // interpolated in between and in mm.
    //
    // These nine numbers are FITTED, not measured: they are the ladder that lands the
    // distribution of |change over one second| on the requested split - 45 % under 0.3 mm,
    // 25 % in 0.3-0.7, 15 % in 0.7-1.0, 10 % in 1.0-1.3, the rest above. The realised split
    // is 45 / 29 / 13 / 7 / 5, which is as close as a smooth process gets; the self-check
    // reports the actual numbers every run. Scaling the whole ladder scales how much the
    // pupil moves per second without changing the shape of that split.
    public static readonly double[] PupilSpeedLadder =
        { 0.04, 0.06, 0.56, 0.67, 0.67, 0.67, 0.81, 0.86, 1.06 };

    // The bands the self-check reports the realised distribution against, and the share of
    // one-second moves each one was asked for.
    public static readonly double[] PupilChangeBandEdges = { 0.3, 0.7, 1.0, 1.3 };
    public static readonly double[] PupilChangeBandTargets = { 45.0, 25.0, 15.0, 10.0, 5.0 };

    // Openness and blinks. Openness is continuous, not boolean.
    public const double OpennessExactlyOneFraction = 0.77;     // ~77 % of samples are exactly 1
    public const double OpennessNearOneFraction = 0.17;        // ~17 % fall in [0.9, 1.0)
    public const double OpennessPartialMin = 0.55;             // the remaining ~5 %
    public const double OpennessPartialMax = 0.90;
    public const double BlinkRatePerSecond = 0.09;             // 9 blinks in 104 s
    public const double BlinkDurationMinSeconds = 0.055;       // 55-250 ms = 1 to 4 samples
    public const double BlinkDurationMaxSeconds = 0.250;
    public const double BlinkPeakOpenness = 0.35;              // every blink sample stays < 0.5
    public const double BlinkRightEyeDifferenceSd = 0.06;      // L/R identical on only 55-90 %

    // Dropped frames.
    public const double InvalidFrameRate = 0.03;               // 0.75 % and 27.5 % - very variable
    public const string InvalidObjectName = "Objeto sem nome";

    // The EyeGaze columns are not unit vectors in the source data; their magnitude is
    // session-dependent (measured norms 0.296-0.447 and 0.602-0.797).
    public const double EyeGazeScaleMin = 0.35;
    public const double EyeGazeScaleMax = 0.75;
    public const double EyeGazeNoiseSd = 0.010;
    public const double EyeGazeJitterDegrees = 1.0;
    // Median angle between the LEFT and RIGHT EyeGaze columns, measured at 4.69 and 4.06 deg.
    public const double InterEyeGazeDivergenceDegrees = 4.4;
    // The two EyeOrientation quaternions, by contrast, are all but identical in the real
    // files: median angle between them was 0.008 and 0.016 deg.
    public const double InterEyeOrientationDivergenceDegrees = 0.012;
    // Roll of the eye about its own forward axis. Without it left_EyeOrientation_Z would be
    // identically 0.0000 on every row, which the real files are not (-0.15 .. +0.20, median
    // 0.03, implying a roll of a few degrees).
    public const double EyeRollMeanDegrees = 3.5;
    public const double EyeRollSdDegrees = 6.0;
    public const double EyeRollTimeConstantSeconds = 1.0;

    // Analysis thresholds, also used by the self-check.
    public const double IvtVelocityThresholdDegPerSecond = 30.0;
    public const double IvtMinimumFixationSeconds = 0.10;

    // Metadata defaults.
    public const string ParticipantId = "SIM001";
    public const string SessionName = "9999-01";
    // The scene and seat as they come out of the JSON, before SessionNaming composes them
    // with the participant and the period of the day.
    public const string SceneName = "Cenario1";
    public const string SeatName = "A1";
    public const string DayPeriod = "Dia";
    public const string SessionStartDateTime = "2026-01-01 10:00:00";
    public const string DeviceName =
        "Modelo: HTC VIVE Focus Vision | OS: Android OS 12 / API-32 (SKQ1.230810.001/10486)";
}

// -------------------------------------------------------------------------------------
//  Resolved component inputs.
// -------------------------------------------------------------------------------------
public class SimulationSettings
{
    public List<Vector3d> StarVectors;
    public List<Point3d> StarHitPoints;
    public List<string> StarObjectNames;
    public Point3d EyeOrigin;
    public Vector3d NeutralForward;
    public int StartVectorIndex;

    /// <summary>
    /// Set when the incoming geometry is Z-up (Rhino's default, and Revit's) rather than
    /// the Y-up frame the CSV has to be written in. See ApplyZUpToYUpConversion.
    /// </summary>
    public bool ConvertZUpToYUp;

    public double SessionSeconds;
    public double SampleRateHz;
    public int Seed;

    /// <summary>
    /// How long the consumer's pupil baseline window is, counted from the first sample. The
    /// simulation does not measure a baseline itself - it only has to make one measurable,
    /// which is why this is here: for these first seconds the pupil is held close to the
    /// value the session opened on. See PupilSignal.
    /// </summary>
    public double BaselineSeconds;

    public int FixationCountMin;
    public int FixationCountMax;
    public double FixationRatePerSecond;
    public double FixationDurationMinSec;
    public double FixationDurationMaxSec;
    public double FixationDurationLogMu;
    public double FixationDurationLogSigma;
    public double IntraFixationDispersionDeg;

    public double SaccadeAmplitudeLogMu;
    public double SaccadeAmplitudeLogSigma;
    public double SaccadeAmplitudeMinDeg;
    public double SaccadeAmplitudeMaxDeg;
    public double ReturnBiasProbability;

    public int DistinctObjectsMin;
    public int DistinctObjectsMax;

    public double NeckYawLimitDeg;
    public double NeckPitchUpLimitDeg;
    public double NeckPitchDownLimitDeg;
    public double EyeYawLimitDeg;
    public double EyePitchLimitDeg;
    public double HeadTimeConstantSec;

    public double InvalidFrameRate;
    public double BlinkRatePerSecond;

    /// <summary>
    /// The domain the participant's SMALLEST pupil diametre is drawn from, and the domain
    /// their LARGEST is drawn from. One draw each, per participant - see ParticipantSeed.
    /// </summary>
    public Interval PupilMinimumDomain;
    public Interval PupilMaximumDomain;

    /// <summary>
    /// Seeds the draws that belong to the PERSON rather than to the session - today that is
    /// the pupil's anatomical range. It is derived from the batch seed and the participant
    /// id alone, so the same person has the same eye in every seat and at both times of
    /// day. Zero means "not set", and the session seed is used instead.
    /// </summary>
    public int ParticipantSeed;

    public string ParticipantId;
    public string SessionName;
    public string SceneName;
    public string SeatName;
    public string SessionStartText;
    public string DeviceName;

    public string OutputFilePath;

    /// <summary>
    /// Set on every session of a batch except the first. The determinism check re-runs the
    /// whole simulation to compare the two files, which doubles the cost of a session for
    /// an answer that cannot differ between them.
    /// </summary>
    public bool SkipDeterminismCheck;

    /// <summary>
    /// A copy carrying every behaviour parameter, for a batch to stamp its per-session
    /// fields onto. Shallow is exactly right: the star lists are shared and never written
    /// to, and ApplyZUpToYUpConversion replaces them rather than mutating them.
    /// </summary>
    public SimulationSettings Copy()
    {
        return (SimulationSettings)MemberwiseClone();
    }

    // An unwired numeric input arrives as 0 in Grasshopper. For every parameter whose
    // zero value is meaningless (a rate, a limit, a duration) that is treated as "unset".
    // Parameters where zero IS meaningful - InvalidFrameRate, BlinkRatePerSecond,
    // ReturnBiasProbability, the distinct-object range and the fixation count range -
    // deliberately do not go through here and must be wired.
    public static double DefaultIfZero(double value, double fallback)
    {
        if (double.IsNaN(value) || Math.Abs(value) < 1e-12) return fallback;
        return value;
    }

    public static string DefaultIfEmpty(string value, string fallback)
    {
        return string.IsNullOrEmpty(value) ? fallback : value;
    }

    /// <summary>
    /// An unwired Domain input arrives as 0 to 0, which is the fallback case. A domain of
    /// zero width anywhere else is left alone - "every participant gets exactly 2.0" is a
    /// thing someone may well mean - and a reversed one is read the way it was written
    /// rather than rejected.
    /// </summary>
    public static Interval DefaultIfEmpty(Interval value, double fallbackLow, double fallbackHigh)
    {
        if (double.IsNaN(value.T0) || double.IsNaN(value.T1) ||
            (Math.Abs(value.T0) < 1e-12 && Math.Abs(value.T1) < 1e-12))
            return new Interval(fallbackLow, fallbackHigh);
        return new Interval(Math.Min(value.T0, value.T1), Math.Max(value.T0, value.T1));
    }
}

// -------------------------------------------------------------------------------------
//  One seat, as the Grasshopper pre-processing wrote it.
//
//  The raycast script emits one JSON per seat: the eye origin, the seat's forward
//  direction, and every ray of the star with the point and object name it hit. Reading it
//  here rather than passing the star in through the canvas keeps a batch of hundreds of
//  sessions cheap: the star exists once per file, not once per branch of a data tree.
// -------------------------------------------------------------------------------------
public class SeatGeometry
{
    public string SceneName = "";
    public string SeatName = "";
    public Point3d Origin;
    public Vector3d Forward;
    public double RayLength;
    public int NearestIndex = -1;

    // Parallel, one entry per ray, in the order of the JSON's own "index" field.
    public List<Vector3d> Vectors = new List<Vector3d>();
    public List<Point3d> HitPoints = new List<Point3d>();
    public List<string> ObjectNames = new List<string>();

    public int HitCount;
    public int MissCount { get { return Vectors.Count - HitCount; } }
}

public class JsonFormatException : Exception
{
    public JsonFormatException(string message) : base(message) { }
}

// -------------------------------------------------------------------------------------
//  Minimal JSON scanner.
//
//  Rhino 7 is .NET Framework 4.8 with no NuGet, so there is no JSON reader to reach for.
//  This one is deliberately a scanner rather than a DOM parser: a seat file can hold 10^5
//  rays, and materialising that as dictionaries of boxed objects costs an order of
//  magnitude more memory than the three typed lists the simulation actually wants.
// -------------------------------------------------------------------------------------
public class JsonScanner
{
    private readonly string text;
    private int position;

    public JsonScanner(string jsonText)
    {
        text = jsonText ?? "";
    }

    public JsonFormatException Error(string message)
    {
        int line = 1;
        for (int i = 0; i < position && i < text.Length; i++)
            if (text[i] == '\n') line++;
        int from = Math.Max(0, position - 20);
        int length = Math.Min(40, text.Length - from);
        string snippet = length > 0 ? text.Substring(from, length).Replace("\r", " ").Replace("\n", " ") : "";
        return new JsonFormatException(message + " (line " + line + ", offset " + position + ", near: " + snippet + ")");
    }

    public void SkipWhitespace()
    {
        while (position < text.Length)
        {
            char c = text[position];
            if (c == ' ' || c == '\t' || c == '\r' || c == '\n') position++;
            else break;
        }
    }

    public bool AtEnd
    {
        get { SkipWhitespace(); return position >= text.Length; }
    }

    public char Peek()
    {
        SkipWhitespace();
        if (position >= text.Length) throw Error("unexpected end of file");
        return text[position];
    }

    public void Expect(char expected)
    {
        SkipWhitespace();
        if (position >= text.Length) throw Error("expected '" + expected + "' but the file ended");
        if (text[position] != expected)
            throw Error("expected '" + expected + "' but found '" + text[position] + "'");
        position++;
    }

    public bool TryConsume(char candidate)
    {
        SkipWhitespace();
        if (position < text.Length && text[position] == candidate) { position++; return true; }
        return false;
    }

    /// <summary>Consumes the literal null and reports whether it was there.</summary>
    public bool TryConsumeNull()
    {
        SkipWhitespace();
        if (position + 4 <= text.Length && string.CompareOrdinal(text, position, "null", 0, 4) == 0)
        {
            position += 4;
            return true;
        }
        return false;
    }

    public string ReadString()
    {
        SkipWhitespace();
        Expect('"');
        StringBuilder builder = new StringBuilder();
        while (true)
        {
            if (position >= text.Length) throw Error("string is not terminated");
            char c = text[position++];
            if (c == '"') break;
            if (c != '\\') { builder.Append(c); continue; }

            if (position >= text.Length) throw Error("escape sequence at end of file");
            char escaped = text[position++];
            switch (escaped)
            {
                case '"': builder.Append('"'); break;
                case '\\': builder.Append('\\'); break;
                case '/': builder.Append('/'); break;
                case 'b': builder.Append('\b'); break;
                case 'f': builder.Append('\f'); break;
                case 'n': builder.Append('\n'); break;
                case 'r': builder.Append('\r'); break;
                case 't': builder.Append('\t'); break;
                case 'u':
                    // Python's json.dump escapes every non-ASCII character this way by
                    // default, so an object named "Poltrona lateral" survives but
                    // "Cadeira acolchoada" arrives as ç and friends. Surrogate pairs
                    // come through as two escapes and two chars, which is exactly right.
                    if (position + 4 > text.Length) throw Error("truncated \\u escape");
                    int code;
                    if (!int.TryParse(text.Substring(position, 4), NumberStyles.HexNumber,
                            CultureInfo.InvariantCulture, out code))
                        throw Error("invalid \\u escape '" + text.Substring(position, 4) + "'");
                    position += 4;
                    builder.Append((char)code);
                    break;
                default:
                    throw Error("unknown escape '\\" + escaped + "'");
            }
        }
        return builder.ToString();
    }

    /// <summary>A string, or null where the JSON holds the literal null.</summary>
    public string ReadStringOrNull()
    {
        if (TryConsumeNull()) return null;
        return ReadString();
    }

    /// <summary>
    /// A string, a null, or a one-element array holding one.
    ///
    /// The array case is not hypothetical: a Grasshopper script input set to List access
    /// hands the script a list even when a single value is wired, and the raycast
    /// pre-processing writes down whatever it was given - so a scene name can arrive as
    /// ["01"] rather than "01". Reading both spellings is cheaper than re-running the
    /// raycast over every seat.
    /// </summary>
    public string ReadStringOrNullUnwrappingArray()
    {
        if (TryConsumeNull()) return null;
        if (Peek() != '[') return ReadString();

        Expect('[');
        if (TryConsume(']')) return null;

        string first = ReadStringOrNull();
        while (TryConsume(',')) SkipValue();     // a name has one value; drop any extras
        Expect(']');
        return first;
    }

    public double ReadNumber()
    {
        SkipWhitespace();
        int start = position;
        if (position < text.Length && (text[position] == '-' || text[position] == '+')) position++;
        while (position < text.Length)
        {
            char c = text[position];
            if ((c >= '0' && c <= '9') || c == '.' || c == 'e' || c == 'E' || c == '+' || c == '-') position++;
            else break;
        }
        if (position == start) throw Error("expected a number");

        double value;
        if (!double.TryParse(text.Substring(start, position - start), NumberStyles.Float,
                CultureInfo.InvariantCulture, out value))
            throw Error("'" + text.Substring(start, position - start) + "' is not a number");
        return value;
    }

    /// <summary>
    /// Reads a three-number array. Returns null where the JSON holds the literal null,
    /// which is how the pre-processing writes a ray that hit nothing.
    /// </summary>
    public double[] ReadVector3OrNull(string fieldName)
    {
        if (TryConsumeNull()) return null;
        Expect('[');
        double[] values = new double[3];
        for (int i = 0; i < 3; i++)
        {
            values[i] = ReadNumber();
            if (i < 2) Expect(',');
        }
        SkipWhitespace();
        if (!TryConsume(']'))
            throw Error("'" + fieldName + "' must hold exactly three numbers");
        return values;
    }

    /// <summary>Consumes and discards any value - the fields this reader does not use.</summary>
    public void SkipValue()
    {
        SkipWhitespace();
        if (position >= text.Length) throw Error("expected a value");

        char c = text[position];
        if (c == '"') { ReadString(); return; }
        if (c == '{' || c == '[')
        {
            char open = c, close = c == '{' ? '}' : ']';
            int depth = 0;
            while (position < text.Length)
            {
                char current = text[position];
                if (current == '"') { ReadString(); continue; }
                position++;
                if (current == open) depth++;
                else if (current == close) { depth--; if (depth == 0) return; }
            }
            throw Error("unterminated " + (open == '{' ? "object" : "array"));
        }
        if (TryConsumeNull()) return;
        if (position + 4 <= text.Length && string.CompareOrdinal(text, position, "true", 0, 4) == 0)
        { position += 4; return; }
        if (position + 5 <= text.Length && string.CompareOrdinal(text, position, "false", 0, 5) == 0)
        { position += 5; return; }
        ReadNumber();
    }
}

// -------------------------------------------------------------------------------------
//  Seat JSON -> SeatGeometry.
//
//  Only the fields the simulation needs are decoded; anything else in the file is skipped,
//  so the pre-processing is free to add to it. Key order is not assumed either, which is
//  why the rays that hit nothing are resolved in a second pass: their placeholder point
//  needs "origin" and "ray_length", and those may well come after "rays" in the file.
// -------------------------------------------------------------------------------------
public static class SeatJsonReader
{
    public static SeatGeometry Load(string path, out string failure)
    {
        failure = null;

        if (string.IsNullOrEmpty(path))
        {
            failure = "JsonPath is empty. Wire the seat JSON written by the raycast script.";
            return null;
        }
        if (!File.Exists(path))
        {
            failure = "Seat JSON not found: '" + path + "'.";
            return null;
        }

        string text;
        try
        {
            text = File.ReadAllText(path, Encoding.UTF8);
        }
        catch (Exception exception)
        {
            failure = "Could not read '" + path + "': " + exception.Message;
            return null;
        }

        try
        {
            return Parse(text);
        }
        catch (JsonFormatException exception)
        {
            failure = "'" + Path.GetFileName(path) + "' is not a valid seat JSON: " + exception.Message;
            return null;
        }
    }

    public static SeatGeometry Parse(string text)
    {
        JsonScanner scanner = new JsonScanner(text);
        SeatGeometry seat = new SeatGeometry();
        List<int> rayIndices = new List<int>();
        List<int> missPositions = new List<int>();
        bool sawOrigin = false, sawForward = false, sawRays = false;

        scanner.Expect('{');
        if (!scanner.TryConsume('}'))
        {
            while (true)
            {
                string key = scanner.ReadString();
                scanner.Expect(':');

                switch (key)
                {
                    case "scene_name":
                        seat.SceneName = scanner.ReadStringOrNullUnwrappingArray() ?? "";
                        break;
                    case "seat_name":
                        seat.SeatName = scanner.ReadStringOrNullUnwrappingArray() ?? "";
                        break;
                    case "origin":
                    {
                        double[] values = scanner.ReadVector3OrNull("origin");
                        if (values == null) throw scanner.Error("'origin' is null");
                        seat.Origin = new Point3d(values[0], values[1], values[2]);
                        sawOrigin = true;
                        break;
                    }
                    case "forward_vector":
                    {
                        double[] values = scanner.ReadVector3OrNull("forward_vector");
                        if (values == null) throw scanner.Error("'forward_vector' is null");
                        seat.Forward = new Vector3d(values[0], values[1], values[2]);
                        sawForward = true;
                        break;
                    }
                    case "ray_length":
                        seat.RayLength = scanner.ReadNumber();
                        break;
                    case "nearest_index":
                        seat.NearestIndex = (int)scanner.ReadNumber();
                        break;
                    case "rays":
                        ReadRays(scanner, seat, rayIndices, missPositions);
                        sawRays = true;
                        break;
                    default:
                        scanner.SkipValue();
                        break;
                }

                if (scanner.TryConsume(',')) continue;
                scanner.Expect('}');
                break;
            }
        }

        if (!sawOrigin) throw new JsonFormatException("'origin' is missing.");
        if (!sawForward) throw new JsonFormatException("'forward_vector' is missing.");
        if (!sawRays || seat.Vectors.Count == 0) throw new JsonFormatException("'rays' is missing or empty.");
        if (seat.Forward.Length < 1e-9) throw new JsonFormatException("'forward_vector' has zero length.");

        ReorderByRayIndex(seat, rayIndices, missPositions);
        ResolveMissPlaceholders(seat, missPositions);
        return seat;
    }

    private static void ReadRays(JsonScanner scanner, SeatGeometry seat,
        List<int> rayIndices, List<int> missPositions)
    {
        if (scanner.TryConsumeNull()) return;
        scanner.Expect('[');
        if (scanner.TryConsume(']')) return;

        while (true)
        {
            ReadOneRay(scanner, seat, rayIndices, missPositions);
            if (scanner.TryConsume(',')) continue;
            scanner.Expect(']');
            return;
        }
    }

    private static void ReadOneRay(JsonScanner scanner, SeatGeometry seat,
        List<int> rayIndices, List<int> missPositions)
    {
        scanner.Expect('{');

        int rayIndex = seat.Vectors.Count;
        double[] vector = null;
        double[] point = null;
        string objectName = null;

        if (!scanner.TryConsume('}'))
        {
            while (true)
            {
                string key = scanner.ReadString();
                scanner.Expect(':');

                switch (key)
                {
                    case "index": rayIndex = (int)scanner.ReadNumber(); break;
                    case "vector": vector = scanner.ReadVector3OrNull("vector"); break;
                    case "point": point = scanner.ReadVector3OrNull("point"); break;
                    case "name": objectName = scanner.ReadStringOrNullUnwrappingArray(); break;
                    default: scanner.SkipValue(); break;      // "hit", "id", anything new
                }

                if (scanner.TryConsume(',')) continue;
                scanner.Expect('}');
                break;
            }
        }

        if (vector == null) throw scanner.Error("ray " + rayIndex + " has no 'vector'");

        seat.Vectors.Add(new Vector3d(vector[0], vector[1], vector[2]));
        rayIndices.Add(rayIndex);

        // A ray that hit nothing keeps its place - the three lists have to stay aligned by
        // index - and is marked with the name the simulator treats as unusable. Its point
        // is filled in later, once origin and ray_length are known.
        if (point == null || !StarDirectionIndex.IsSelectableName(objectName))
        {
            seat.HitPoints.Add(new Point3d(0, 0, 0));
            seat.ObjectNames.Add(SimulationDefaults.InvalidObjectName);
            missPositions.Add(seat.Vectors.Count - 1);
        }
        else
        {
            seat.HitPoints.Add(new Point3d(point[0], point[1], point[2]));
            seat.ObjectNames.Add(objectName);
            seat.HitCount++;
        }
    }

    /// <summary>
    /// The file is written in order, but the contract is the "index" field, not the
    /// position in the array. Sorting is skipped entirely when they already agree, which
    /// is the normal case.
    /// </summary>
    private static void ReorderByRayIndex(SeatGeometry seat, List<int> rayIndices, List<int> missPositions)
    {
        bool alreadyOrdered = true;
        for (int i = 0; i < rayIndices.Count; i++)
            if (rayIndices[i] != i) { alreadyOrdered = false; break; }
        if (alreadyOrdered) return;

        int[] order = new int[rayIndices.Count];
        for (int i = 0; i < order.Length; i++) order[i] = i;
        Array.Sort(rayIndices.ToArray(), order);

        List<Vector3d> vectors = new List<Vector3d>(order.Length);
        List<Point3d> points = new List<Point3d>(order.Length);
        List<string> names = new List<string>(order.Length);
        int[] newPositionOf = new int[order.Length];
        for (int i = 0; i < order.Length; i++)
        {
            vectors.Add(seat.Vectors[order[i]]);
            points.Add(seat.HitPoints[order[i]]);
            names.Add(seat.ObjectNames[order[i]]);
            newPositionOf[order[i]] = i;
        }
        seat.Vectors = vectors;
        seat.HitPoints = points;
        seat.ObjectNames = names;
        for (int i = 0; i < missPositions.Count; i++) missPositions[i] = newPositionOf[missPositions[i]];
    }

    /// <summary>
    /// Puts every missed ray's point at the far end of that ray. The value is never read as
    /// a gaze target - the name keeps it out of the star index - but a point at the origin
    /// would look like a real hit to anyone inspecting the data on the canvas.
    /// </summary>
    private static void ResolveMissPlaceholders(SeatGeometry seat, List<int> missPositions)
    {
        if (missPositions.Count == 0) return;

        double distance = seat.RayLength;
        if (distance <= 0.0)
        {
            // The file did not say. Anything beyond the farthest real hit reads as "off the
            // end of the ray" rather than as geometry.
            double farthest = 0.0;
            for (int i = 0; i < seat.HitPoints.Count; i++)
            {
                if (seat.ObjectNames[i] == SimulationDefaults.InvalidObjectName) continue;
                Point3d p = seat.HitPoints[i];
                double dx = p.X - seat.Origin.X, dy = p.Y - seat.Origin.Y, dz = p.Z - seat.Origin.Z;
                double d = Math.Sqrt(dx * dx + dy * dy + dz * dz);
                if (d > farthest) farthest = d;
            }
            distance = Math.Max(1.0, farthest * 2.0);
        }

        for (int i = 0; i < missPositions.Count; i++)
        {
            int position = missPositions[i];
            Vector3d direction = VectorMath.Unit(seat.Vectors[position]);
            seat.HitPoints[position] = new Point3d(
                seat.Origin.X + direction.X * distance,
                seat.Origin.Y + direction.Y * distance,
                seat.Origin.Z + direction.Z * distance);
        }
    }
}

// -------------------------------------------------------------------------------------
//  Finding the seat file for one (scene, seat) pair.
//
//  The folder is no longer LISTED. Which seats exist is decided by the roster, not by
//  what happens to be lying in the folder, so the path is composed from the scene and the
//  seat - "01" + "A1" -> "01_A1.json" - and the only question left is whether that one
//  file is there. A folder holding the archived output of an earlier run therefore cannot
//  silently add sessions nobody asked for.
// -------------------------------------------------------------------------------------
public static class SeatJsonFolder
{
    /// <summary>
    /// The full path of one seat file, or null with a failure that names the file that is
    /// missing rather than the folder that is incomplete.
    /// </summary>
    public static string ResolveSeatFile(string folder, string scene, string seat, out string failure)
    {
        failure = null;

        if (string.IsNullOrEmpty(folder) || folder.Trim().Length == 0)
        {
            failure = "JsonFolder is empty. Wire the folder the raycast script wrote its seat JSONs into.";
            return null;
        }

        folder = folder.Trim();
        if (!Directory.Exists(folder))
        {
            failure = "The seat JSON folder '" + folder + "' does not exist.";
            return null;
        }

        string fileName = StudyConfiguration.SeatFileName(scene, seat);
        string path = Path.Combine(folder, fileName);
        if (!File.Exists(path))
        {
            failure = "Scene '" + scene + "' seat '" + seat + "' needs '" + fileName +
                      "', which is not in '" + folder + "'.";
            return null;
        }
        return path;
    }
}

// -------------------------------------------------------------------------------------
//  Reading the same seat file 26 times in a row - once per participant - is the normal
//  shape of a batch, so the last few files parsed are kept. The entry is invalidated by
//  the file's own write time and length, so re-running the raycast is picked up.
// -------------------------------------------------------------------------------------
public static class SeatJsonCache
{
    private class Entry
    {
        public string Path;
        public DateTime WriteTimeUtc;
        public long Length;
        public SeatGeometry Seat;
    }

    private const int MaximumEntries = 8;
    private static readonly List<Entry> entries = new List<Entry>();

    public static SeatGeometry Load(string path, out string failure)
    {
        failure = null;
        if (string.IsNullOrEmpty(path))
        {
            failure = "JsonPath is empty. Wire the seat JSON written by the raycast script.";
            return null;
        }

        DateTime writeTimeUtc = DateTime.MinValue;
        long length = -1;
        try
        {
            FileInfo info = new FileInfo(path);
            if (info.Exists) { writeTimeUtc = info.LastWriteTimeUtc; length = info.Length; }
        }
        catch (Exception)
        {
            // Unreadable metadata is not fatal here; the load below reports the real error.
        }

        lock (entries)
        {
            for (int i = 0; i < entries.Count; i++)
            {
                Entry entry = entries[i];
                if (!string.Equals(entry.Path, path, StringComparison.OrdinalIgnoreCase)) continue;
                if (entry.WriteTimeUtc != writeTimeUtc || entry.Length != length) break;
                entries.RemoveAt(i);
                entries.Insert(0, entry);                  // most recently used first
                return entry.Seat;
            }
        }

        SeatGeometry seat = SeatJsonReader.Load(path, out failure);
        if (seat == null) return null;

        lock (entries)
        {
            entries.RemoveAll(e => string.Equals(e.Path, path, StringComparison.OrdinalIgnoreCase));
            entries.Insert(0, new Entry
            {
                Path = path,
                WriteTimeUtc = writeTimeUtc,
                Length = length,
                Seat = seat
            });
            while (entries.Count > MaximumEntries) entries.RemoveAt(entries.Count - 1);
        }
        return seat;
    }

    public static void Clear()
    {
        lock (entries) entries.Clear();
    }
}

// -------------------------------------------------------------------------------------
//  The metadata strings are composed here rather than on the canvas, so that the CSV
//  columns and the file names cannot drift apart.
//
//  The seat is part of the scene name because "scene_name" in the JSON is one value for
//  the whole scene - every seat of a scene carries the same one - so without it two seats
//  of the same scene would write the same file.
//
//  The ROUND is deliberately not in any CSV column. It is in the folder and in the file
//  name only, so that a reader of the file - PluginCAISM included - sees exactly the same
//  columns it saw before rounds existed.
// -------------------------------------------------------------------------------------
public static class SessionNaming
{
    // "Cenario" with the accent, written as an escape so the literal survives being pasted
    // into an editor that guesses the encoding.
    private const string SessionPrefix = "Cenário_";

    public static string BuildSceneName(string participantId, string sceneName, string seatName, string dayPeriod)
    {
        return participantId + "_" + sceneName + "_" + seatName + "_" + dayPeriod;
    }

    public static string BuildSessionName(string sceneName, string dayPeriod)
    {
        return SessionPrefix + sceneName + "_" + dayPeriod;
    }

    /// <summary>
    /// The stem both files of a session share: the gaze CSV is the stem, the environment
    /// CSV is the stem plus the suffix.
    /// </summary>
    public static string BuildFileStem(string sceneName, int roundIndex)
    {
        return sceneName + StudyConfiguration.RoundFileSuffix(roundIndex);
    }

    /// <summary>
    /// OutputFolder / "RODADA 01" / "CENÁRIO 01". The two files of the session go into a
    /// folder each below this one. All of them are created on demand when a file is written.
    /// </summary>
    public static string BuildSceneFolder(string outputFolder, int roundIndex, string scene)
    {
        return Path.Combine(outputFolder, StudyConfiguration.RoundFolderName(roundIndex),
            StudyConfiguration.SceneFolderName(scene));
    }

    /// <summary>... / "HTC" — the gaze CSVs of that scene.</summary>
    public static string BuildGazeFolder(string outputFolder, int roundIndex, string scene)
    {
        return Path.Combine(BuildSceneFolder(outputFolder, roundIndex, scene),
            StudyConfiguration.GazeFolderName);
    }

    /// <summary>... / "AMBIENTE" — the environment CSVs of that scene.</summary>
    public static string BuildEnvironmentFolder(string outputFolder, int roundIndex, string scene)
    {
        return Path.Combine(BuildSceneFolder(outputFolder, roundIndex, scene),
            StudyConfiguration.EnvironmentFolderName);
    }
}

// -------------------------------------------------------------------------------------
//  Writing an output file, creating whatever folders it needs on the way.
//
//  Both CSVs go through here. Missing folders used to be a hard error - one had to lay
//  the tree out by hand before pressing Run - and at four levels deep that is dozens of
//  folders nobody should have to make.
//
//  The byte order mark is the caller's choice, and the two files differ on it. The gaze CSV
//  must NOT have one: it is a stand-in for a real capture file, which has none, and a BOM
//  would land in the first header cell and break the reader looking for "timeStamp". The
//  environment CSV SHOULD have one: nothing but Excel reads it, and without a BOM Excel
//  opens a UTF-8 file as the system code page and turns ALIMENTAÇÃO into ALIMENTAÃ‡ÃƒO.
// -------------------------------------------------------------------------------------
public static class OutputFile
{
    public static string Write(string content, string path, bool byteOrderMark)
    {
        try
        {
            string directory = Path.GetDirectoryName(Path.GetFullPath(path));
            if (!string.IsNullOrEmpty(directory) && !Directory.Exists(directory))
                Directory.CreateDirectory(directory);
            File.WriteAllText(path, content, new UTF8Encoding(byteOrderMark));
            return null;
        }
        catch (Exception exception)
        {
            return "Could not write '" + path + "': " + exception.Message;
        }
    }
}

// -------------------------------------------------------------------------------------
//  The batch.
//
//  One component solution plans the WHOLE study - every round, every scene, every one of
//  the 26 participants - and then writes only the sessions that were selected on the
//  canvas. Planning the complete study every time is the entire point: it is what makes
//  the timestamp of participant 7580 in scene 02 of round 3 the same number whether it
//  was produced by a run of all 234 sessions or by a run of that one.
// -------------------------------------------------------------------------------------

/// <summary>
/// The queue of start times for one period of the day. Every session takes a slot of
/// SessionSeconds + the interval, and when the day's window fills the queue rolls over to
/// the next day. Nothing starts at the closing hour, and nothing runs past it.
/// </summary>
public class PeriodSchedule
{
    private readonly DateTime firstDay;
    private readonly DayPeriodWindow window;
    private readonly double slotSeconds;
    private int dayOffset;
    private int slotIndex;

    public readonly int SlotsPerDay;
    public readonly double WindowSeconds;

    public PeriodSchedule(DateTime firstDay, DayPeriodWindow window, double sessionSeconds, double slotSeconds)
    {
        this.firstDay = firstDay.Date;
        this.window = window;
        this.slotSeconds = slotSeconds;
        WindowSeconds = (window.EndHour - window.StartHour) * 3600.0;

        SlotsPerDay = (sessionSeconds > WindowSeconds || slotSeconds <= 0.0)
            ? 0
            : (int) ((WindowSeconds - sessionSeconds) / slotSeconds) + 1;
    }

    public string Label { get { return window.Label; } }

    /// <summary>Days since the first day of the round, for the slot issued most recently.</summary>
    public int CurrentDayOffset { get { return dayOffset; } }

    /// <summary>
    /// Reopens the queue at the top of a given day. A scene change calls this so that the
    /// new scene starts on a clean day rather than in whatever slot the previous scene
    /// happened to stop at.
    /// </summary>
    public void JumpToDay(int day)
    {
        dayOffset = day;
        slotIndex = 0;
    }

    public bool TryNextStart(out DateTime start)
    {
        start = DateTime.MinValue;
        if (SlotsPerDay <= 0) return false;

        if (slotIndex >= SlotsPerDay) { slotIndex = 0; dayOffset++; }
        start = firstDay.AddDays(dayOffset).AddHours(window.StartHour).AddSeconds(slotIndex * slotSeconds);
        slotIndex++;
        return true;
    }
}

/// <summary>One session of the study, before it is simulated.</summary>
public class PlannedSession
{
    public int Index;                       // position in the complete plan, never in the slice
    public int Round;                       // 0-based; round 0 is "Rodada_1"
    public string Scene;
    public ParticipantAssignment Person;
    public DateTime StartsAt;
    public string JsonPath;                 // resolved from Scene + Seat when the session runs

    public string ParticipantId { get { return Person.Id; } }
    public string SeatName { get { return Person.Seat; } }
    public string DayPeriod { get { return Person.DayPeriod; } }
}

public class BatchRequest
{
    /// <summary>The folder holding the seat JSONs, named "{scene}_{seat}.json".</summary>
    public string JsonFolder;

    // The three selections. Each one is a FILTER over the complete plan: an empty or
    // unwired list means "all of them", and no selection ever moves a session in time.
    public IList<string> SelectedParticipantIds;
    public IList<string> SelectedScenes;
    public IList<int> SelectedRounds;        // 1-based on the canvas, to match "Rodada_1"

    /// <summary>"dayOfYear,tempDay,tempNight,humidityDay,humidityNight", one row per day.</summary>
    public IList<string> ClimateRows;

    public string OutputFolder;
    public int BaseSeed;
    public int PreviewIndex;

    /// <summary>Every behaviour parameter; the runner stamps the per-session fields on.</summary>
    public SimulationSettings Template;
}

public class BatchSessionResult
{
    public PlannedSession Session;
    public string SceneName = "";
    public string CsvPath = "";
    public string EnvironmentCsvPath = "";
    public bool Succeeded;
    public string FailureMessage = "";
    public int FailedChecks;
    public int Warnings;
}

public class BatchResult
{
    public bool Succeeded;
    public string FailureMessage = "";
    public string ReportText = "";

    public List<string> CsvPaths = new List<string>();
    public List<string> EnvironmentCsvPaths = new List<string>();
    public List<string> SceneNames = new List<string>();
    public List<string> TimeStamps = new List<string>();
    public List<int> Rounds = new List<int>();          // 1-based, parallel to CsvPaths
    public List<BatchSessionResult> Sessions = new List<BatchSessionResult>();

    /// <summary>The one session whose geometry goes to the component outputs.</summary>
    public SimulationResult Preview;
    public int PreviewIndex = -1;
}

// -------------------------------------------------------------------------------------
//  The climate table.
//
//  One row per day of the year, holding the day's MEAN temperature and humidity for each
//  period. It is raw data rather than a model on purpose: it is the anchor that keeps two
//  sessions recorded on the same day agreeing with each other. Without it, re-running one
//  participant would draw a fresh temperature and the merged data would show a step in
//  the middle of an afternoon.
// -------------------------------------------------------------------------------------
public class ClimateDay
{
    public int DayOfYear;
    public double TemperatureDayC;
    public double TemperatureNightC;
    public double HumidityDayPercent;
    public double HumidityNightPercent;
}

public class ClimateTable
{
    private readonly Dictionary<int, ClimateDay> byDayOfYear = new Dictionary<int, ClimateDay>();

    public int Count { get { return byDayOfYear.Count; } }

    /// <summary>
    /// Reads "dayOfYear,tempDay,tempNight,humidityDay,humidityNight". Blank rows and rows
    /// opening with '#' are comments. Returns null with a reason on the first bad row.
    /// </summary>
    public static ClimateTable Parse(IList<string> rows, out string failure)
    {
        failure = null;
        if (rows == null || rows.Count == 0)
        {
            failure = "ClimateTable is empty. Wire the climate component: without it there " +
                      "is no mean temperature or humidity for a day, so no environment CSV.";
            return null;
        }

        ClimateTable table = new ClimateTable();
        for (int i = 0; i < rows.Count; i++)
        {
            string row = (rows[i] ?? "").Trim();
            if (row.Length == 0 || row[0] == '#') continue;

            string[] cells = row.Split(',');
            if (cells.Length != 5)
            {
                failure = "ClimateTable row " + i + " has " + cells.Length + " cells, expected 5 " +
                          "(dayOfYear,tempDay,tempNight,humidityDay,humidityNight): '" + row + "'.";
                return null;
            }

            int dayOfYear;
            double temperatureDay, temperatureNight, humidityDay, humidityNight;
            if (!int.TryParse(cells[0].Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out dayOfYear) ||
                !TryReadNumber(cells[1], out temperatureDay) ||
                !TryReadNumber(cells[2], out temperatureNight) ||
                !TryReadNumber(cells[3], out humidityDay) ||
                !TryReadNumber(cells[4], out humidityNight))
            {
                failure = "ClimateTable row " + i + " could not be read as numbers: '" + row + "'.";
                return null;
            }
            if (dayOfYear < 1 || dayOfYear > 366)
            {
                failure = "ClimateTable row " + i + " has day of year " + dayOfYear + "; expected 1..366.";
                return null;
            }
            if (table.byDayOfYear.ContainsKey(dayOfYear))
            {
                failure = "ClimateTable lists day of year " + dayOfYear + " twice.";
                return null;
            }

            table.byDayOfYear.Add(dayOfYear, new ClimateDay
            {
                DayOfYear = dayOfYear,
                TemperatureDayC = temperatureDay,
                TemperatureNightC = temperatureNight,
                HumidityDayPercent = humidityDay,
                HumidityNightPercent = humidityNight
            });
        }

        if (table.byDayOfYear.Count == 0)
        {
            failure = "ClimateTable held no usable rows.";
            return null;
        }
        return table;
    }

    private static bool TryReadNumber(string text, out double value)
    {
        return double.TryParse(text.Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    /// <summary>
    /// The mean the session's walk is anchored to. A table of 365 rows is accepted: day 366
    /// of a leap year falls back to day 365 rather than failing the batch.
    /// </summary>
    public bool TryResolve(DateTime day, bool isNight, out double temperature, out double humidity)
    {
        temperature = 0.0; humidity = 0.0;

        ClimateDay entry;
        int dayOfYear = day.DayOfYear;
        if (!byDayOfYear.TryGetValue(dayOfYear, out entry) &&
            !(dayOfYear == 366 && byDayOfYear.TryGetValue(365, out entry)))
            return false;

        temperature = isNight ? entry.TemperatureNightC : entry.TemperatureDayC;
        humidity = isNight ? entry.HumidityNightPercent : entry.HumidityDayPercent;
        return true;
    }
}

// -------------------------------------------------------------------------------------
//  The environment sensor.
//
//  One reading per gaze sample, on the same instants, so that row i of the environment
//  CSV and row i of the gaze CSV describe the same moment. Temperature, humidity and the
//  noise floor are each one slice of one sine wave; CO is a coin; noise adds a permanent
//  sub-decibel jitter and, now and then, an event with a shape.
// -------------------------------------------------------------------------------------
public class EnvironmentSample
{
    public double TimeSeconds;
    public double TemperatureC;
    public double HumidityPercent;
    public int CarbonMonoxide;
    public double NoiseDb;
}

/// <summary>
/// One slice of one sine wave, stretched over a session and rescaled to a chosen
/// peak-to-peak. A start phase and a span decide the shape; because the span can never
/// exceed a full period, the curve turns at most twice, and because it is a sine, it turns
/// smoothly. Every scenario the study needs - climb, fall, rise-then-fall, dip-then-recover,
/// barely-moved - is some slice of the same wave.
/// </summary>
public class SineSlice
{
    private readonly double startPhase;
    private readonly double span;
    private readonly double scale;        // value units per unit of raw sine
    private readonly double baseValue;    // the value the session opens on, after fitting
    private readonly double startSine;

    private SineSlice(double startPhase, double span, double scale, double baseValue)
    {
        this.startPhase = startPhase;
        this.span = span;
        this.scale = scale;
        this.baseValue = baseValue;
        this.startSine = Math.Sin(startPhase);
    }

    /// <summary>normalizedTime runs 0 at the first sample to 1 at the last.</summary>
    public double ValueAt(double normalizedTime)
    {
        return baseValue + scale * (Math.Sin(startPhase + normalizedTime * span) - startSine);
    }

    /// <summary>
    /// Draws a slice for one session.
    ///
    /// The excursion is what the session covers end to end. It is drawn, and then cut down
    /// twice: once so the steepest point of the curve still respects the stated rate limit,
    /// and once so the whole curve fits between the floor and the ceiling. Cutting the
    /// AMPLITUDE rather than clamping the VALUE is what keeps the curve a sine - a clamp
    /// would flat-top it, and a flat top is a corner, which is the one thing this whole
    /// model exists to avoid.
    ///
    /// Note that steep and wide cannot co-occur: a full period at the rate limit has to
    /// settle for a smaller excursion than a quarter period does.
    /// </summary>
    public static SineSlice Draw(DeterministicRandom random, double startValue,
        double excursionMinimum, double excursionMaximum,
        double maximumRatePerMinute, double sessionMinutes,
        double floorValue, double ceilingValue)
    {
        double startPhase = random.NextInRange(0.0, 2.0 * Math.PI);
        double span = random.NextInRange(StudyConfiguration.SignalSpanMinRadians,
                                         StudyConfiguration.SignalSpanMaxRadians);
        double excursion = random.NextInRange(excursionMinimum, excursionMaximum);

        double endPhase = startPhase + span;
        double startSine = Math.Sin(startPhase);
        double endSine = Math.Sin(endPhase);

        // The extremes of the slice: a crest only counts if the slice actually reaches one.
        double rawMaximum = Reaches(startPhase, span, 0.5 * Math.PI, 2.0 * Math.PI)
            ? 1.0 : Math.Max(startSine, endSine);
        double rawMinimum = Reaches(startPhase, span, -0.5 * Math.PI, 2.0 * Math.PI)
            ? -1.0 : Math.Min(startSine, endSine);
        double rawRange = rawMaximum - rawMinimum;
        if (rawRange < 1e-9) rawRange = 1e-9;

        // The absolute cosine peaks at every multiple of pi; on a slice that reaches none of
        // them it is largest at one of the two ends.
        double maximumAbsoluteCosine = Reaches(startPhase, span, 0.0, Math.PI)
            ? 1.0 : Math.Max(Math.Abs(Math.Cos(startPhase)), Math.Abs(Math.Cos(endPhase)));

        // Steepest slope of the fitted curve, per unit of excursion, per session. The mean
        // value theorem puts this at 1.0 or above, which is why respecting the rate limit
        // also keeps the session total travel inside rate x duration.
        double slopeFactor = span * maximumAbsoluteCosine / rawRange;
        if (slopeFactor < 1.0) slopeFactor = 1.0;
        if (sessionMinutes > 0.0 && !double.IsInfinity(maximumRatePerMinute))
        {
            double allowed = maximumRatePerMinute * sessionMinutes / slopeFactor;
            if (excursion > allowed) excursion = allowed;
        }

        // Fit between the walls by sliding the curve, and shrink it only when sliding cannot
        // work because the curve is wider than the band itself.
        double band = ceilingValue - floorValue;
        if (excursion > band) excursion = band;

        double scale = excursion / rawRange;
        double curveMinimum = startValue + scale * (rawMinimum - startSine);
        double curveMaximum = startValue + scale * (rawMaximum - startSine);

        double shift = 0.0;
        if (curveMinimum < floorValue) shift = floorValue - curveMinimum;
        if (curveMaximum + shift > ceilingValue) shift = ceilingValue - curveMaximum;

        return new SineSlice(startPhase, span, scale, startValue + shift);
    }

    /// <summary>Does the slice contain some target plus a whole number of periods?</summary>
    private static bool Reaches(double startPhase, double span, double target, double period)
    {
        double turns = Math.Ceiling((startPhase - target) / period);
        return target + turns * period <= startPhase + span;
    }
}

/// <summary>
/// One sound with a shape: it rises over Rise, holds its peak for Hold, falls over Fall.
/// Both envelopes the study needs are this one function - a pass-by is simply an event with
/// no hold, whose rise and fall then meet at the peak and draw a bell.
/// </summary>
public class NoiseEvent
{
    public double StartSeconds;
    public double PeakDb;
    public double RiseSeconds;
    public double HoldSeconds;
    public double FallSeconds;

    public double EndSeconds
    {
        get { return StartSeconds + RiseSeconds + HoldSeconds + FallSeconds; }
    }

    /// <summary>
    /// Raised cosine on both edges, so the level leaves zero and returns to zero with zero
    /// slope. No sample is ever the first sample of a jump - the loudest event in the file
    /// still arrives over a second or more, the way a real one does.
    /// </summary>
    public double LevelAt(double nowSeconds)
    {
        double elapsed = nowSeconds - StartSeconds;
        if (elapsed <= 0.0) return 0.0;

        if (elapsed < RiseSeconds)
            return PeakDb * 0.5 * (1.0 - Math.Cos(Math.PI * elapsed / RiseSeconds));

        elapsed -= RiseSeconds;
        if (elapsed < HoldSeconds) return PeakDb;

        elapsed -= HoldSeconds;
        if (elapsed < FallSeconds)
            return PeakDb * 0.5 * (1.0 + Math.Cos(Math.PI * elapsed / FallSeconds));

        return 0.0;
    }
}

public class EnvironmentSimulator
{
    public List<EnvironmentSample> Generate(IList<double> sampleTimes,
        double meanTemperature, double meanHumidity, int seed)
    {
        List<EnvironmentSample> samples = new List<EnvironmentSample>(
            sampleTimes == null ? 0 : sampleTimes.Count);
        if (sampleTimes == null || sampleTimes.Count == 0) return samples;

        DeterministicRandom random = new DeterministicRandom(seed);

        double firstTime = sampleTimes[0];
        double sessionSeconds = sampleTimes[sampleTimes.Count - 1] - firstTime;
        if (sessionSeconds <= 0.0) sessionSeconds = 1.0;   // one-sample session: still a row
        double sessionMinutes = sessionSeconds / 60.0;

        // The session opens near the day mean, never on it - two sessions of the same day
        // should not report the identical opening temperature.
        double temperatureStart = meanTemperature +
            random.NextInRange(-StudyConfiguration.TemperatureStartDeviationC,
                                StudyConfiguration.TemperatureStartDeviationC);
        SineSlice temperature = SineSlice.Draw(random, temperatureStart,
            StudyConfiguration.TemperatureExcursionMinC,
            StudyConfiguration.TemperatureExcursionMaxC,
            StudyConfiguration.TemperatureMaxChangePerMinuteC, sessionMinutes,
            double.NegativeInfinity, double.PositiveInfinity);

        double humidityStart = Clamp(meanHumidity +
            random.NextInRange(-StudyConfiguration.HumidityStartDeviationPercent,
                                StudyConfiguration.HumidityStartDeviationPercent),
            StudyConfiguration.HumidityFloorPercent, StudyConfiguration.HumidityCeilingPercent);
        SineSlice humidity = SineSlice.Draw(random, humidityStart,
            StudyConfiguration.HumidityExcursionMinPercent,
            StudyConfiguration.HumidityExcursionMaxPercent,
            StudyConfiguration.HumidityMaxChangePerMinutePercent, sessionMinutes,
            StudyConfiguration.HumidityFloorPercent, StudyConfiguration.HumidityCeilingPercent);

        // The floor gets no rate limit of its own: a decibel or so spread over a session is
        // already slower than anything a limit would catch.
        double noiseFloorStart = random.NextInRange(StudyConfiguration.NoiseBaseFloorDb,
                                                    StudyConfiguration.NoiseBaseCeilingDb);
        SineSlice noiseFloor = SineSlice.Draw(random, noiseFloorStart,
            StudyConfiguration.NoiseBaseExcursionMinDb,
            StudyConfiguration.NoiseBaseExcursionMaxDb,
            double.PositiveInfinity, sessionMinutes,
            StudyConfiguration.NoiseBaseFloorDb, StudyConfiguration.NoiseBaseCeilingDb);

        List<NoiseEvent> active = new List<NoiseEvent>();
        double previousTime = firstTime;

        for (int i = 0; i < sampleTimes.Count; i++)
        {
            double now = sampleTimes[i];
            double elapsedSeconds = now - previousTime;
            if (elapsedSeconds < 0.0) elapsedSeconds = 0.0;
            previousTime = now;

            double normalizedTime = (now - firstTime) / sessionSeconds;
            if (normalizedTime < 0.0) normalizedTime = 0.0;
            else if (normalizedTime > 1.0) normalizedTime = 1.0;

            // ---- CO: a flag, an even coin, no pattern --------------------------------
            int carbonMonoxide = random.NextChance(StudyConfiguration.CarbonMonoxideHighProbability) ? 1 : 0;

            // ---- noise ---------------------------------------------------------------
            // Events start whenever they start. Nothing checks whether the room is already
            // busy, so a pass-by can happen while a fan is running and the two add up.
            if (random.NextChance(StudyConfiguration.NoiseEventsPerMinute * elapsedSeconds / 60.0))
                active.Add(DrawEvent(random, now));

            double eventLevel = 0.0;
            for (int e = active.Count - 1; e >= 0; e--)
            {
                if (active[e].EndSeconds <= now) { active.RemoveAt(e); continue; }
                eventLevel += active[e].LevelAt(now);
            }

            // The one layer allowed to differ from one sample to the next, and it is worth
            // tenths of a decibel: the microphone breathing, not the room changing.
            double jitter = Clamp(random.NextGaussian() * StudyConfiguration.NoiseJitterSigmaDb,
                -StudyConfiguration.NoiseJitterMaxDb, StudyConfiguration.NoiseJitterMaxDb);

            double noise = Clamp(noiseFloor.ValueAt(normalizedTime) + eventLevel + jitter,
                StudyConfiguration.NoiseAbsoluteFloorDb, StudyConfiguration.NoiseAbsoluteCeilingDb);

            samples.Add(new EnvironmentSample
            {
                TimeSeconds = now,
                TemperatureC = temperature.ValueAt(normalizedTime),
                HumidityPercent = humidity.ValueAt(normalizedTime),
                CarbonMonoxide = carbonMonoxide,
                NoiseDb = noise
            });
        }
        return samples;
    }

    /// <summary>
    /// A pass-by or a sustained sound, chosen by a coin weighted the way a room is: more
    /// things switch on and off than drive past.
    /// </summary>
    private static NoiseEvent DrawEvent(DeterministicRandom random, double nowSeconds)
    {
        NoiseEvent noiseEvent = new NoiseEvent();
        noiseEvent.StartSeconds = nowSeconds;

        if (random.NextChance(StudyConfiguration.NoisePassByShare))
        {
            double span = random.NextInRange(StudyConfiguration.NoisePassByMinSpanSeconds,
                                              StudyConfiguration.NoisePassByMaxSpanSeconds);
            noiseEvent.PeakDb = DrawExcess(random, StudyConfiguration.NoisePassByMinExcessDb,
                StudyConfiguration.NoisePassByExcessMeanDb,
                StudyConfiguration.NoisePassByMaxExcessDb);
            noiseEvent.RiseSeconds = span * 0.5;
            noiseEvent.HoldSeconds = 0.0;                 // no body: the edges meet at the peak
            noiseEvent.FallSeconds = span * 0.5;
            return noiseEvent;
        }

        noiseEvent.PeakDb = DrawExcess(random, StudyConfiguration.NoiseSustainedMinExcessDb,
            StudyConfiguration.NoiseSustainedExcessMeanDb,
            StudyConfiguration.NoiseSustainedMaxExcessDb);
        noiseEvent.RiseSeconds = random.NextInRange(
            StudyConfiguration.NoiseSustainedRiseMinSeconds,
            StudyConfiguration.NoiseSustainedRiseMaxSeconds);
        noiseEvent.HoldSeconds = random.NextInRange(
            StudyConfiguration.NoiseSustainedHoldMinSeconds,
            StudyConfiguration.NoiseSustainedHoldMaxSeconds);
        noiseEvent.FallSeconds = random.NextInRange(
            StudyConfiguration.NoiseSustainedFallMinSeconds,
            StudyConfiguration.NoiseSustainedFallMaxSeconds);
        return noiseEvent;
    }

    /// <summary>
    /// Exponential above a minimum, capped: every extra decibel is less likely than the one
    /// before it, which keeps the loudest readings rare instead of merely bounded.
    /// </summary>
    private static double DrawExcess(DeterministicRandom random, double minimum, double mean,
        double maximum)
    {
        double uniform = random.NextDouble();
        if (uniform > 0.999999) uniform = 0.999999;
        double value = minimum + -Math.Log(1.0 - uniform) * mean;
        return value > maximum ? maximum : value;
    }

    private static double Clamp(double value, double minimum, double maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }
}

// -------------------------------------------------------------------------------------
//  The environment CSV.
//
//  Same row order as the gaze CSV - newest first - and the same timestamp format, so the
//  two files of a session line up row for row. The header says HH:MM:SS because that is
//  what the device writes; the value carries milliseconds, like every other timestamp in
//  this study.
//
//  WHAT MAKES EXCEL GUESS THE TYPES RIGHT
//  --------------------------------------
//  A CSV has no type layer - there is nowhere in the format to say "this column is a
//  number". Every reader guesses from the text, so the only lever is to write text its
//  locale reads the way it was meant. Four things do that here, and all four are needed:
//
//    1. The field delimiter matches the reader's list separator, or the whole row lands in
//       one cell and nothing can be typed at all. pt-BR Excel wants ';'.
//    2. Numbers use the reader's decimal separator. pt-BR Excel reads "21.40" as TEXT (or,
//       worse, as the date 21 April); "21,40" is the number.
//    3. Numbers are NOT quoted. A quoted "21,40" is text no matter what the locale is.
//       Only the genuinely textual columns are quoted here - which is also what keeps a MAC
//       address from being read as a time and an empty cell from becoming a zero.
//    4. The file opens with a UTF-8 byte order mark, so the accented headers survive.
//
//  Set the three constants in StudyConfiguration to "," / "." / false for an English-locale
//  Excel, pandas or R.
// -------------------------------------------------------------------------------------
public static class EnvironmentCsvWriter
{
    public static readonly string[] ColumnNames =
        {
        "DATA: YYYY-MM-DD HH:MM:SS", "DISPOSITIVO", "DNS REDE", "TEMPO ATIVIDADE",
        "MAC ADDRESS", "RESET", "ALIMENTAÇÃO", "TEMPERATURA", "UMIDADE", "CO", "RUÍDO"
        };

    public const string LineTerminator = "\r\n";
    public const int ColumnCount = 11;

    // Always two decimals - "##.##" - unlike the gaze CSV, which drops trailing zeros
    // because the Unity logger did.
    private const string MeasurementFormat = "0.00";

    public static string Delimiter { get { return StudyConfiguration.EnvironmentFieldDelimiter; } }

    public static string HeaderLine { get { return string.Join(Delimiter, ColumnNames); } }

    public static string BuildFileContent(List<EnvironmentSample> samples, DateTime sessionStart)
    {
        StringBuilder builder = new StringBuilder(samples.Count * 128 + 256);
        builder.Append(HeaderLine).Append(LineTerminator);
        for (int i = samples.Count - 1; i >= 0; i--)          // newest first, like the gaze CSV
            builder.Append(BuildDataLine(samples[i], sessionStart)).Append(LineTerminator);
        return builder.ToString();
    }

    public static string BuildDataLine(EnvironmentSample sample, DateTime sessionStart)
    {
        string[] fields = new string[ColumnCount];
        int c = 0;

        // Quoted, because it is text as far as the delimiter is concerned - but see the
        // note in the README: Excel types a millisecond timestamp as text whatever we do,
        // and the fix for that is on the import side.
        fields[c++] = Quote(sessionStart.AddSeconds(sample.TimeSeconds)
            .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        fields[c++] = Quote(StudyConfiguration.EnvironmentDeviceName);
        fields[c++] = Quote(StudyConfiguration.EnvironmentNetworkDns);
        fields[c++] = Quote("");                              // TEMPO ATIVIDADE - never filled
        fields[c++] = Quote(StudyConfiguration.EnvironmentMacAddress);
        fields[c++] = Quote("");                              // RESET - never filled
        fields[c++] = Quote(StudyConfiguration.EnvironmentPowerState);

        // Bare, so the reader types them as numbers.
        fields[c++] = M(sample.TemperatureC);
        fields[c++] = M(sample.HumidityPercent);
        fields[c++] = sample.CarbonMonoxide.ToString(CultureInfo.InvariantCulture);
        fields[c] = M(sample.NoiseDb);

        return string.Join(Delimiter, fields);
    }

    /// <summary>
    /// Splits a line of THIS file back into its cells, unquoting as it goes. The reader has
    /// to agree with the writer about the delimiter, so it reads the same constant.
    /// </summary>
    public static string[] SplitDataLine(string line)
    {
        List<string> cells = new List<string>(ColumnCount);
        StringBuilder cell = new StringBuilder();
        bool inQuotes = false;

        for (int i = 0; i < line.Length; i++)
        {
            char character = line[i];
            if (inQuotes)
            {
                if (character != '"') { cell.Append(character); continue; }
                if (i + 1 < line.Length && line[i + 1] == '"') { cell.Append('"'); i++; continue; }
                inQuotes = false;
                continue;
            }
            if (character == '"') { inQuotes = true; continue; }
            if (StartsDelimiter(line, i))
            {
                cells.Add(cell.ToString());
                cell.Length = 0;
                i += Delimiter.Length - 1;
                continue;
            }
            cell.Append(character);
        }
        cells.Add(cell.ToString());
        return cells.ToArray();
    }

    private static bool StartsDelimiter(string line, int index)
    {
        string delimiter = Delimiter;
        if (index + delimiter.Length > line.Length) return false;
        for (int i = 0; i < delimiter.Length; i++)
            if (line[index + i] != delimiter[i]) return false;
        return true;
    }

    private static string Quote(string text)
    {
        return "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";
    }

    private static string M(double value)
    {
        // Formatted invariant, then re-pointed: one place decides what a decimal point looks
        // like, and it is a constant rather than whatever culture Rhino happens to run under.
        return value.ToString(MeasurementFormat, CultureInfo.InvariantCulture)
            .Replace(".", StudyConfiguration.EnvironmentDecimalSeparator);
    }
}

public class BatchRunner
{
    public BatchResult Run(BatchRequest request)
    {
        BatchResult batch = new BatchResult();
        StringBuilder log = new StringBuilder();

        string validationFailure = Validate(request);
        if (validationFailure != null) return Fail(batch, validationFailure);

        string climateFailure;
        ClimateTable climate = ClimateTable.Parse(request.ClimateRows, out climateFailure);
        if (climate == null) return Fail(batch, climateFailure);
        log.AppendLine("Climate table ............. " + climate.Count + " day(s) of the year");

        DateTime firstDay = DateTime.ParseExact(StudyConfiguration.StartingDay, "yyyy-MM-dd",
            CultureInfo.InvariantCulture);

        double sessionSeconds = StudyConfiguration.SessionSeconds;
        double slotSeconds = StudyConfiguration.SlotSeconds;

        // ---- the plan, always the complete study ------------------------------------
        string planFailure;
        Dictionary<string, PeriodSchedule> schedules;
        List<PlannedSession> plan = BuildPlan(firstDay, sessionSeconds, slotSeconds,
            out schedules, out planFailure);
        if (plan == null) return Fail(batch, planFailure);

        // ---- the selection, applied only after planning ------------------------------
        HashSet<string> wantedParticipants = ToSet(request.SelectedParticipantIds);
        HashSet<string> wantedScenes = ToSet(request.SelectedScenes);
        HashSet<int> wantedRounds = ToRoundSet(request.SelectedRounds);

        List<PlannedSession> selected = new List<PlannedSession>();
        for (int i = 0; i < plan.Count; i++)
        {
            PlannedSession planned = plan[i];
            if (wantedParticipants != null && !wantedParticipants.Contains(planned.ParticipantId)) continue;
            if (wantedScenes != null && !wantedScenes.Contains(planned.Scene)) continue;
            if (wantedRounds != null && !wantedRounds.Contains(planned.Round + 1)) continue;
            selected.Add(planned);
        }

        if (selected.Count == 0)
            return Fail(batch, "The selection matched none of the " + plan.Count + " planned sessions. " +
                UnknownSelectionHint(wantedParticipants, wantedScenes, wantedRounds));

        log.AppendLine("Selected .................. " + selected.Count + " of " + plan.Count + " planned session(s)");

        // ---- run, one seat file at a time --------------------------------------------
        // The order below is NOT the order of the plan: it is grouped by seat file, so each
        // of the JSONs is parsed once for the whole batch instead of once per participant.
        // It is free to reorder because every timestamp and every seed was decided during
        // planning, from the session's identity - never from its position in the run.
        List<PlannedSession> executionOrder = new List<PlannedSession>(selected);
        executionOrder.Sort(CompareForFileLocality);

        int previewIndex = request.PreviewIndex;
        bool previewIsInSelection = false;
        for (int i = 0; i < selected.Count; i++)
            if (selected[i].Index == previewIndex) { previewIsInSelection = true; break; }
        if (!previewIsInSelection) previewIndex = selected[0].Index;

        DateTime startedAt = DateTime.UtcNow;
        int written = 0, failed = 0, withFailedChecks = 0;
        bool determinismCheckedOnce = false;
        HashSet<string> reportedMissingFiles = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        for (int i = 0; i < executionOrder.Count; i++)
        {
            PlannedSession planned = executionOrder[i];

            BatchSessionResult sessionResult = new BatchSessionResult();
            sessionResult.Session = planned;
            batch.Sessions.Add(sessionResult);

            // ---- the seat file for this (scene, seat) --------------------------------
            string resolveFailure;
            planned.JsonPath = SeatJsonFolder.ResolveSeatFile(request.JsonFolder,
                planned.Scene, planned.SeatName, out resolveFailure);
            if (planned.JsonPath == null)
            {
                if (reportedMissingFiles.Add(resolveFailure)) log.AppendLine("!!  " + resolveFailure);
                sessionResult.Succeeded = false;
                sessionResult.FailureMessage = resolveFailure;
                failed++;
                continue;
            }

            string seatFailure;
            SeatGeometry seat = SeatJsonCache.Load(planned.JsonPath, out seatFailure);
            if (seat == null)
            {
                if (reportedMissingFiles.Add(seatFailure)) log.AppendLine("!!  " + seatFailure);
                sessionResult.Succeeded = false;
                sessionResult.FailureMessage = seatFailure;
                failed++;
                continue;
            }

            // ---- the gaze session -----------------------------------------------------
            SimulationSettings settings = request.Template.Copy();
            settings.StarVectors = seat.Vectors;
            settings.StarHitPoints = seat.HitPoints;
            settings.StarObjectNames = seat.ObjectNames;
            settings.EyeOrigin = seat.Origin;
            settings.NeutralForward = seat.Forward;
            settings.StartVectorIndex = seat.NearestIndex;

            settings.ParticipantId = planned.ParticipantId;
            // The roster decides the seat, so a JSON whose seat_name disagrees with the name
            // it is filed under is a mis-named file, not a second opinion.
            settings.SeatName = planned.SeatName;
            settings.SceneName = SessionNaming.BuildSceneName(
                planned.ParticipantId, planned.Scene, planned.SeatName, planned.DayPeriod);
            settings.SessionName = SessionNaming.BuildSessionName(planned.Scene, planned.DayPeriod);
            settings.SessionStartText = planned.StartsAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            settings.Seed = SessionSeed(request.BaseSeed, planned);
            settings.ParticipantSeed = ParticipantSeed(request.BaseSeed, planned.ParticipantId);

            string stem = SessionNaming.BuildFileStem(settings.SceneName, planned.Round);
            settings.OutputFilePath = Path.Combine(
                SessionNaming.BuildGazeFolder(request.OutputFolder, planned.Round, planned.Scene),
                stem + ".csv");
            settings.SkipDeterminismCheck = determinismCheckedOnce;
            determinismCheckedOnce = true;

            SimulationResult result = new SessionSimulator().Generate(settings);

            sessionResult.SceneName = settings.SceneName;
            sessionResult.CsvPath = settings.OutputFilePath;
            sessionResult.Succeeded = result.Succeeded;
            sessionResult.FailureMessage = result.FailureMessage;
            sessionResult.FailedChecks = CountLinesStartingWith(result.ReportText, "FAIL  ");
            sessionResult.Warnings = CountLinesStartingWith(result.ReportText, "!!  ");

            if (!result.Succeeded) { failed++; continue; }

            // ---- the environment session, on the same instants ------------------------
            string environmentFailure = WriteEnvironmentCsv(request, planned, result, climate,
                Path.Combine(
                    SessionNaming.BuildEnvironmentFolder(request.OutputFolder, planned.Round, planned.Scene),
                    stem + StudyConfiguration.EnvironmentFileSuffix + ".csv"),
                sessionResult);
            if (environmentFailure != null)
            {
                sessionResult.Succeeded = false;
                sessionResult.FailureMessage = environmentFailure;
                failed++;
                continue;
            }

            written++;
            if (sessionResult.FailedChecks > 0) withFailedChecks++;

            if (planned.Index == previewIndex)
            {
                batch.Preview = result;
                batch.PreviewIndex = planned.Index;
            }
        }

        // Back into plan order, so the report and the output lists read chronologically
        // however the run was internally shuffled for file locality.
        batch.Sessions.Sort(delegate(BatchSessionResult left, BatchSessionResult right)
        {
            return left.Session.Index.CompareTo(right.Session.Index);
        });
        for (int i = 0; i < batch.Sessions.Count; i++)
        {
            BatchSessionResult session = batch.Sessions[i];
            if (!session.Succeeded) continue;
            batch.CsvPaths.Add(session.CsvPath);
            batch.EnvironmentCsvPaths.Add(session.EnvironmentCsvPath);
            batch.SceneNames.Add(session.SceneName);
            batch.TimeStamps.Add(session.Session.StartsAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            batch.Rounds.Add(session.Session.Round + 1);
        }

        double elapsedSeconds = (DateTime.UtcNow - startedAt).TotalSeconds;
        batch.Succeeded = failed == 0 && written > 0;
        if (written == 0)
            batch.FailureMessage = "No session was written. " + (failed > 0 ? "Every one failed." : "The selection is empty.");
        else if (failed > 0)
            batch.FailureMessage = failed + " of " + (written + failed) + " sessions failed; see the report.";

        batch.ReportText = BuildReport(request, batch, plan, selected, schedules, log.ToString(),
            slotSeconds, elapsedSeconds, written, failed, withFailedChecks);
        return batch;
    }

    // -----------------------------------------------------------------------------------
    //  Planning
    // -----------------------------------------------------------------------------------

    /// <summary>
    /// The complete study, planned but not simulated. It is a pure function of
    /// StudyConfiguration - no folder, no seed, no selection - and every timestamp in every
    /// file the component writes comes out of it, which is what makes it worth reading and
    /// checking on its own.
    /// </summary>
    public static List<PlannedSession> PlanStudy(out string failure)
    {
        DateTime firstDay;
        if (!DateTime.TryParseExact(StudyConfiguration.StartingDay, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out firstDay))
        {
            failure = "StudyConfiguration.StartingDay '" + StudyConfiguration.StartingDay +
                      "' could not be read. Expected 'yyyy-MM-dd'.";
            return null;
        }

        Dictionary<string, PeriodSchedule> schedules;
        return BuildPlan(firstDay, StudyConfiguration.SessionSeconds, StudyConfiguration.SlotSeconds,
            out schedules, out failure);
    }

    /// <summary>
    /// The complete study: RoundCount rounds, each running every scene in turn, each scene
    /// running the whole roster in Order. A scene always opens on a fresh day, and a round
    /// always opens MonthsBetweenRounds after the one before it.
    /// </summary>
    private static List<PlannedSession> BuildPlan(DateTime firstDay, double sessionSeconds,
        double slotSeconds, out Dictionary<string, PeriodSchedule> lastSchedules, out string failure)
    {
        failure = null;
        lastSchedules = null;

        List<ParticipantAssignment> roster = new List<ParticipantAssignment>(StudyConfiguration.Roster);
        roster.Sort(delegate(ParticipantAssignment left, ParticipantAssignment right)
        {
            return left.Order.CompareTo(right.Order);
        });

        List<PlannedSession> plan = new List<PlannedSession>();

        for (int round = 0; round < StudyConfiguration.RoundCount; round++)
        {
            DateTime roundStart = firstDay.AddMonths(round * StudyConfiguration.MonthsBetweenRounds);

            Dictionary<string, PeriodSchedule> schedules =
                new Dictionary<string, PeriodSchedule>(StringComparer.Ordinal);
            for (int i = 0; i < StudyConfiguration.DayPeriods.Length; i++)
            {
                DayPeriodWindow window = StudyConfiguration.DayPeriods[i];
                PeriodSchedule schedule = new PeriodSchedule(roundStart, window, sessionSeconds, slotSeconds);
                if (schedule.SlotsPerDay <= 0)
                {
                    failure = "A session of " + Fmt(sessionSeconds) + " s plus a " +
                        Fmt(StudyConfiguration.IntervalMinutes) + " min interval does not fit the '" +
                        window.Label + "' window (" + window.StartHour + ":00-" + window.EndHour + ":00, " +
                        Fmt(schedule.WindowSeconds) + " s).";
                    return null;
                }
                schedules.Add(window.Label, schedule);
            }
            lastSchedules = schedules;

            int sceneStartDay = 0;
            for (int sceneIndex = 0; sceneIndex < StudyConfiguration.Scenes.Length; sceneIndex++)
            {
                string scene = StudyConfiguration.Scenes[sceneIndex];

                // A scene change moves BOTH queues to the same fresh day, whatever hour the
                // previous scene stopped at.
                foreach (PeriodSchedule schedule in schedules.Values) schedule.JumpToDay(sceneStartDay);

                for (int p = 0; p < roster.Count; p++)
                {
                    ParticipantAssignment person = roster[p];

                    PeriodSchedule schedule;
                    if (!schedules.TryGetValue(person.DayPeriod, out schedule))
                    {
                        failure = "Participant " + person.Id + " is assigned to period '" +
                            person.DayPeriod + "', which is not one of the periods in " +
                            "StudyConfiguration.DayPeriods.";
                        return null;
                    }

                    DateTime start;
                    if (!schedule.TryNextStart(out start))
                    {
                        failure = "The '" + person.DayPeriod + "' queue ran out of slots.";
                        return null;
                    }

                    plan.Add(new PlannedSession
                    {
                        Index = plan.Count,
                        Round = round,
                        Scene = scene,
                        Person = person,
                        StartsAt = start
                    });
                }

                int lastDay = 0;
                foreach (PeriodSchedule schedule in schedules.Values)
                    lastDay = Math.Max(lastDay, schedule.CurrentDayOffset);
                sceneStartDay = lastDay + 1;
            }

            // A round that runs into the next one would interleave two rounds of the same
            // participant, which is not a study anybody ran.
            DateTime nextRoundStart = firstDay.AddMonths((round + 1) * StudyConfiguration.MonthsBetweenRounds);
            DateTime lastStart = plan[plan.Count - 1].StartsAt;
            if (round + 1 < StudyConfiguration.RoundCount && lastStart >= nextRoundStart)
            {
                failure = "Round " + (round + 1) + " ends on " +
                    lastStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ", which is on or after " +
                    "round " + (round + 2) + " starts (" +
                    nextRoundStart.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) +
                    "). Raise MonthsBetweenRounds, or shorten the round.";
                return null;
            }
        }
        return plan;
    }

    private static int CompareForFileLocality(PlannedSession left, PlannedSession right)
    {
        int compared = string.CompareOrdinal(left.Scene, right.Scene);
        if (compared != 0) return compared;
        compared = string.CompareOrdinal(left.SeatName, right.SeatName);
        if (compared != 0) return compared;
        return left.Index.CompareTo(right.Index);
    }

    // -----------------------------------------------------------------------------------
    //  The environment file of one session
    // -----------------------------------------------------------------------------------
    private static string WriteEnvironmentCsv(BatchRequest request, PlannedSession planned,
        SimulationResult gaze, ClimateTable climate, string path, BatchSessionResult sessionResult)
    {
        DayPeriodWindow window;
        if (!StudyConfiguration.TryFindPeriod(planned.DayPeriod, out window))
            return "Period '" + planned.DayPeriod + "' is not in StudyConfiguration.DayPeriods.";

        double meanTemperature, meanHumidity;
        if (!climate.TryResolve(planned.StartsAt, window.IsNight, out meanTemperature, out meanHumidity))
            return "The climate table has no row for day of year " + planned.StartsAt.DayOfYear +
                   " (" + planned.StartsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + ").";

        List<EnvironmentSample> samples = new EnvironmentSimulator().Generate(
            gaze.SampleTimes, meanTemperature, meanHumidity,
            EnvironmentSeed(request.BaseSeed, planned));

        string content = EnvironmentCsvWriter.BuildFileContent(samples, planned.StartsAt);
        string writeFailure = OutputFile.Write(content, path, StudyConfiguration.EnvironmentByteOrderMark);
        if (writeFailure != null) return writeFailure;

        sessionResult.EnvironmentCsvPath = path;
        return null;
    }

    // -----------------------------------------------------------------------------------
    //  Validation, seeds and formatting
    // -----------------------------------------------------------------------------------

    private static BatchResult Fail(BatchResult batch, string message)
    {
        batch.Succeeded = false;
        batch.FailureMessage = message;
        batch.ReportText = "BATCH FAILED\r\n" + message;
        return batch;
    }

    private static string Validate(BatchRequest request)
    {
        if (string.IsNullOrEmpty(request.JsonFolder) || request.JsonFolder.Trim().Length == 0)
            return "JsonFolder is empty. Supply the folder holding the seat JSONs.";
        if (!Directory.Exists(request.JsonFolder.Trim()))
            return "The seat JSON folder '" + request.JsonFolder + "' does not exist.";
        if (string.IsNullOrEmpty(request.OutputFolder) || request.OutputFolder.Trim().Length == 0)
            return "OutputFolder is empty.";

        // The output folder is CREATED rather than demanded: a run writes a folder per round
        // per scene, and none of them can be expected to exist beforehand.
        try
        {
            if (!Directory.Exists(request.OutputFolder)) Directory.CreateDirectory(request.OutputFolder);
        }
        catch (Exception exception)
        {
            return "Could not create the output folder '" + request.OutputFolder + "': " + exception.Message;
        }

        DateTime parsed;
        if (!DateTime.TryParseExact(StudyConfiguration.StartingDay, "yyyy-MM-dd",
            CultureInfo.InvariantCulture, DateTimeStyles.None, out parsed))
            return "StudyConfiguration.StartingDay '" + StudyConfiguration.StartingDay +
                   "' could not be read. Expected 'yyyy-MM-dd'.";

        if (StudyConfiguration.Roster.Length == 0)
            return "StudyConfiguration.Roster is empty.";
        if (StudyConfiguration.Scenes.Length == 0)
            return "StudyConfiguration.Scenes is empty.";
        // Through a local, not the constant: comparing the constant directly folds away and
        // the compiler then warns that the guard is unreachable.
        int roundCount = StudyConfiguration.RoundCount;
        if (roundCount <= 0)
            return "StudyConfiguration.RoundCount must be at least 1.";

        // Two participants sharing an Order would share a start time, and the file of one
        // would be written over the file of the other.
        Dictionary<int, string> ordersSeen = new Dictionary<int, string>();
        Dictionary<string, string> idsSeen = new Dictionary<string, string>(StringComparer.Ordinal);
        for (int i = 0; i < StudyConfiguration.Roster.Length; i++)
        {
            ParticipantAssignment person = StudyConfiguration.Roster[i];
            if (ordersSeen.ContainsKey(person.Order))
                return "StudyConfiguration.Roster gives Order " + person.Order + " to both " +
                       ordersSeen[person.Order] + " and " + person.Id + ".";
            ordersSeen.Add(person.Order, person.Id);
            if (idsSeen.ContainsKey(person.Id))
                return "StudyConfiguration.Roster lists participant " + person.Id + " twice.";
            idsSeen.Add(person.Id, person.Seat);
        }
        return null;
    }

    private static HashSet<string> ToSet(IList<string> values)
    {
        if (values == null || values.Count == 0) return null;         // null means "all of them"
        HashSet<string> set = new HashSet<string>(StringComparer.Ordinal);
        for (int i = 0; i < values.Count; i++)
        {
            string value = (values[i] ?? "").Trim();
            if (value.Length > 0) set.Add(value);
        }
        return set.Count == 0 ? null : set;
    }

    private static HashSet<int> ToRoundSet(IList<int> values)
    {
        if (values == null || values.Count == 0) return null;
        HashSet<int> set = new HashSet<int>();
        for (int i = 0; i < values.Count; i++) set.Add(values[i]);
        return set.Count == 0 ? null : set;
    }

    /// <summary>
    /// An empty selection is nearly always a name that is not in the study rather than a
    /// deliberate choice, so the message names the ones that matched nothing.
    /// </summary>
    private static string UnknownSelectionHint(HashSet<string> participants, HashSet<string> scenes,
        HashSet<int> rounds)
    {
        StringBuilder text = new StringBuilder();

        if (participants != null)
        {
            List<string> unknown = new List<string>();
            foreach (string id in participants)
            {
                bool known = false;
                for (int i = 0; i < StudyConfiguration.Roster.Length; i++)
                    if (StudyConfiguration.Roster[i].Id == id) { known = true; break; }
                if (!known) unknown.Add(id);
            }
            if (unknown.Count > 0)
                text.Append("Not in the roster: " + string.Join(", ", unknown.ToArray()) + ". ");
        }
        if (scenes != null)
        {
            List<string> unknown = new List<string>();
            foreach (string scene in scenes)
                if (Array.IndexOf(StudyConfiguration.Scenes, scene) < 0) unknown.Add(scene);
            if (unknown.Count > 0)
                text.Append("Not a scene of the study: " + string.Join(", ", unknown.ToArray()) +
                    " (known: " + string.Join(", ", StudyConfiguration.Scenes) + "). ");
        }
        if (rounds != null)
        {
            List<string> unknown = new List<string>();
            foreach (int round in rounds)
                if (round < 1 || round > StudyConfiguration.RoundCount)
                    unknown.Add(round.ToString(CultureInfo.InvariantCulture));
            if (unknown.Count > 0)
                text.Append("Not a round of the study: " + string.Join(", ", unknown.ToArray()) +
                    " (1.." + StudyConfiguration.RoundCount + "). ");
        }
        return text.Length > 0 ? text.ToString().Trim() : "Check the three selection inputs.";
    }

    /// <summary>
    /// A seed of its own per session, so that 234 sessions are not 234 replays of the same
    /// random walk. Derived from the session's IDENTITY rather than from its position, so
    /// running one participant on its own reproduces the file the full run produced.
    /// </summary>
    public static int SessionSeed(int baseSeed, PlannedSession session)
    {
        return Fnv(baseSeed, SessionIdentity(session));
    }

    /// <summary>
    /// The environment walk of a session, seeded apart from the gaze so that changing a
    /// gaze parameter does not silently redraw the weather.
    /// </summary>
    public static int EnvironmentSeed(int baseSeed, PlannedSession session)
    {
        return Fnv(baseSeed, "environment|" + SessionIdentity(session));
    }

    private static string SessionIdentity(PlannedSession session)
    {
        return session.ParticipantId + "|" + session.Scene + "|" + session.SeatName + "|" +
               session.DayPeriod + "|round" + session.Round.ToString(CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// The seed for what belongs to the PERSON rather than to the session - today that is
    /// the pupil's anatomical range. The seat, the scene, the round and the period are
    /// deliberately not in it: a participant's eye has to be the same in every file of the
    /// study, and hashing only the id is what guarantees it.
    /// </summary>
    public static int ParticipantSeed(int baseSeed, string participantId)
    {
        return Fnv(baseSeed, "participant|" + participantId);
    }

    private static int Fnv(int baseSeed, string identity)
    {
        unchecked
        {
            uint hash = 2166136261u ^ (uint) baseSeed;
            for (int i = 0; i < identity.Length; i++)
            {
                hash ^= identity[i];
                hash *= 16777619u;
            }
            return (int) (hash & 0x7FFFFFFF);
        }
    }

    private static int CountLinesStartingWith(string text, string prefix)
    {
        if (string.IsNullOrEmpty(text)) return 0;
        int count = 0, from = 0;
        while (true)
        {
            int found = text.IndexOf(prefix, from, StringComparison.Ordinal);
            if (found < 0) return count;
            if (found == 0 || text[found - 1] == '\n') count++;
            from = found + prefix.Length;
        }
    }

    private static string Fmt(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    private static string BuildReport(BatchRequest request, BatchResult batch,
        List<PlannedSession> plan, List<PlannedSession> selected,
        Dictionary<string, PeriodSchedule> schedules, string seatLog,
        double slotSeconds, double elapsedSeconds, int written, int failed, int withFailedChecks)
    {
        StringBuilder text = new StringBuilder();
        text.AppendLine("STUDY");
        text.AppendLine("===============================================================");
        if (seatLog.Length > 0) text.Append(seatLog);

        text.AppendLine("Roster .................... " + StudyConfiguration.Roster.Length + " participant(s), " +
            StudyConfiguration.Scenes.Length + " scene(s), " + StudyConfiguration.RoundCount + " round(s)");
        text.AppendLine("Sessions planned .......... " + plan.Count + "   (" +
            StudyConfiguration.RoundCount + " rounds x " + StudyConfiguration.Scenes.Length +
            " scenes x " + StudyConfiguration.Roster.Length + " participants)");
        text.AppendLine("Slot ...................... " + Fmt(StudyConfiguration.SessionSeconds) +
            " s session + " + Fmt(StudyConfiguration.IntervalMinutes) + " min interval = " +
            Fmt(slotSeconds / 60.0) + " min");
        text.AppendLine("Rounds .................... start " + StudyConfiguration.StartingDay +
            ", then every " + StudyConfiguration.MonthsBetweenRounds + " month(s)");

        for (int i = 0; i < StudyConfiguration.DayPeriods.Length; i++)
        {
            DayPeriodWindow window = StudyConfiguration.DayPeriods[i];
            int inPeriod = 0;
            for (int p = 0; p < StudyConfiguration.Roster.Length; p++)
                if (StudyConfiguration.Roster[p].DayPeriod == window.Label) inPeriod++;

            PeriodSchedule schedule;
            int slotsPerDay = schedules != null && schedules.TryGetValue(window.Label, out schedule)
                ? schedule.SlotsPerDay : 0;

            text.AppendLine(("Period '" + window.Label + "' ").PadRight(28, '.') +
                " " + window.StartHour + ":00-" + window.EndHour + ":00, " +
                slotsPerDay + " slot(s) per day, " + inPeriod + " participant(s)");
        }

        // The span each round actually occupies, taken from the plan rather than recomputed.
        for (int round = 0; round < StudyConfiguration.RoundCount; round++)
        {
            DateTime first = DateTime.MaxValue, last = DateTime.MinValue;
            for (int s = 0; s < plan.Count; s++)
            {
                if (plan[s].Round != round) continue;
                if (plan[s].StartsAt < first) first = plan[s].StartsAt;
                if (plan[s].StartsAt > last) last = plan[s].StartsAt;
            }
            text.AppendLine(("Rodada_" + (round + 1) + " ").PadRight(28, '.') + " " +
                first.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture) + " .. " +
                last.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture));
        }

        text.AppendLine();
        text.AppendLine("THIS RUN");
        text.AppendLine("---------------------------------------------------------------");
        text.AppendLine("Selected .................. " + selected.Count + " session(s)");
        text.AppendLine("Written ................... " + written + " session(s) = " + (written * 2) +
            " file(s) in " + Fmt(elapsedSeconds) + " s" +
            (written > 0 ? " (" + Fmt(elapsedSeconds / written) + " s each)" : ""));
        text.AppendLine("Output .................... " + request.OutputFolder);
        if (failed > 0) text.AppendLine("FAILED .................... " + failed + " session(s)");
        if (withFailedChecks > 0)
            text.AppendLine("!!  " + withFailedChecks + " session(s) were written but reported a failed self-check.");
        text.AppendLine();

        text.AppendLine("SESSIONS");
        text.AppendLine("---------------------------------------------------------------");
        for (int i = 0; i < batch.Sessions.Count; i++)
        {
            BatchSessionResult session = batch.Sessions[i];
            text.Append("[" + session.Session.Index.ToString(CultureInfo.InvariantCulture).PadLeft(4) + "]  ");
            text.Append("R" + (session.Session.Round + 1) + "  ");
            text.Append(session.Session.StartsAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture));
            text.Append("  #" + session.Session.Person.Order.ToString(CultureInfo.InvariantCulture).PadLeft(2) + "  ");
            text.Append(session.SceneName.Length > 0
                ? session.SceneName
                : session.Session.ParticipantId + "_" + session.Session.Scene + "_" +
                  session.Session.SeatName + "_" + session.Session.DayPeriod);
            if (!session.Succeeded) text.Append("   FAILED: " + session.FailureMessage);
            else
            {
                if (session.FailedChecks > 0) text.Append("   " + session.FailedChecks + " FAILED CHECK(S)");
                if (session.Warnings > 0) text.Append("   " + session.Warnings + " warning(s)");
            }
            text.AppendLine();
        }
        text.AppendLine();

        if (batch.Preview != null)
        {
            text.AppendLine("FULL REPORT OF SESSION " + batch.PreviewIndex +
                            " (set PreviewIndex to the [index] of another one)");
            text.AppendLine("===============================================================");
            text.Append(batch.Preview.ReportText);
        }
        return text.ToString();
    }
}

// -------------------------------------------------------------------------------------
//  Deterministic PRNG. System.Random is not reproducible across .NET versions, and
//  acceptance criterion 16 requires byte-identical files for a given seed on any
//  machine, so the generator is written out explicitly (xorshift128).
// -------------------------------------------------------------------------------------
public class DeterministicRandom
{
    private uint stateX, stateY, stateZ, stateW;
    private double cachedGaussian;
    private bool hasCachedGaussian;

    public DeterministicRandom(int seed)
    {
        uint splitMixState = unchecked((uint)seed);
        stateX = NextSplitMix(ref splitMixState);
        stateY = NextSplitMix(ref splitMixState);
        stateZ = NextSplitMix(ref splitMixState);
        stateW = NextSplitMix(ref splitMixState);
        if ((stateX | stateY | stateZ | stateW) == 0u)
        {
            stateX = 123456789u; stateY = 362436069u;
            stateZ = 521288629u; stateW = 88675123u;
        }
        for (int i = 0; i < 24; i++) NextUnsigned();   // discard the seeding transient
    }

    private static uint NextSplitMix(ref uint state)
    {
        unchecked
        {
            state += 0x9E3779B9u;
            uint mixed = state;
            mixed = (mixed ^ (mixed >> 16)) * 0x85EBCA6Bu;
            mixed = (mixed ^ (mixed >> 13)) * 0xC2B2AE35u;
            return mixed ^ (mixed >> 16);
        }
    }

    public uint NextUnsigned()
    {
        unchecked
        {
            uint t = stateX ^ (stateX << 11);
            stateX = stateY; stateY = stateZ; stateZ = stateW;
            stateW = stateW ^ (stateW >> 19) ^ t ^ (t >> 8);
            return stateW;
        }
    }

    /// <summary>Uniform in [0, 1).</summary>
    public double NextDouble()
    {
        return (NextUnsigned() >> 8) * (1.0 / 16777216.0);
    }

    public double NextInRange(double minimum, double maximum)
    {
        return minimum + (maximum - minimum) * NextDouble();
    }

    public int NextInteger(int minimumInclusive, int maximumInclusive)
    {
        if (maximumInclusive <= minimumInclusive) return minimumInclusive;
        int span = maximumInclusive - minimumInclusive + 1;
        return minimumInclusive + (int)(NextDouble() * span);
    }

    public bool NextChance(double probability)
    {
        return NextDouble() < probability;
    }

    /// <summary>Standard normal via Box-Muller, with the second deviate cached.</summary>
    public double NextGaussian()
    {
        if (hasCachedGaussian)
        {
            hasCachedGaussian = false;
            return cachedGaussian;
        }
        double u1 = NextDouble();
        if (u1 < 1e-12) u1 = 1e-12;
        double u2 = NextDouble();
        double radius = Math.Sqrt(-2.0 * Math.Log(u1));
        double angle = 2.0 * Math.PI * u2;
        cachedGaussian = radius * Math.Sin(angle);
        hasCachedGaussian = true;
        return radius * Math.Cos(angle);
    }

    public double NextGaussian(double mean, double standardDeviation)
    {
        return mean + standardDeviation * NextGaussian();
    }

    /// <summary>Log-normal draw, resampled until it lands inside [minimum, maximum].</summary>
    public double NextTruncatedLogNormal(double logMu, double logSigma, double minimum, double maximum)
    {
        for (int attempt = 0; attempt < 200; attempt++)
        {
            double value = Math.Exp(logMu + logSigma * NextGaussian());
            if (value >= minimum && value <= maximum) return value;
        }
        // Pathological parameters: fall back to the median, clamped.
        return Math.Min(maximum, Math.Max(minimum, Math.Exp(logMu)));
    }

    /// <summary>
    /// Standard normal CDF - turns a unit-variance process into a uniform [0, 1] one, which
    /// is how the pupil's arousal is read off a ladder. Abramowitz and Stegun 7.1.26,
    /// accurate to 1.5e-7, which is far past anything that matters here.
    /// </summary>
    public static double NormalCdf(double x)
    {
        double sign = x < 0 ? -1.0 : 1.0;
        double z = Math.Abs(x) / Math.Sqrt(2.0);
        double t = 1.0 / (1.0 + 0.3275911 * z);
        double y = 1.0 - (((((1.061405429 * t - 1.453152027) * t) + 1.421413741) * t - 0.284496736) * t
                          + 0.254829592) * t * Math.Exp(-z * z);
        return 0.5 * (1.0 + sign * y);
    }
}

// -------------------------------------------------------------------------------------
//  The participant's own eye.
//
//  Drawn once per PERSON, never per session: the same participant carries the same pupil
//  range through every seat and both periods of the day, which is what makes a batch look
//  like 26 people rather than 104 strangers. That is the whole reason ParticipantSeed
//  exists separately from the session seed.
// -------------------------------------------------------------------------------------
public class PupilAnthropometry
{
    public double Minimum;
    public double Maximum;

    public double Centre { get { return 0.5 * (Minimum + Maximum); } }
    public double Span { get { return Maximum - Minimum; } }

    public static PupilAnthropometry ForParticipant(int participantSeed,
        Interval minimumDomain, Interval maximumDomain)
    {
        DeterministicRandom random = new DeterministicRandom(participantSeed);

        PupilAnthropometry anatomy = new PupilAnthropometry();
        anatomy.Minimum = random.NextInRange(minimumDomain.Min, minimumDomain.Max);
        anatomy.Maximum = random.NextInRange(maximumDomain.Min, maximumDomain.Max);

        // Nothing stops the two domains being given the wrong way round, or overlapping.
        if (anatomy.Maximum < anatomy.Minimum)
        {
            double swap = anatomy.Minimum;
            anatomy.Minimum = anatomy.Maximum;
            anatomy.Maximum = swap;
        }
        if (anatomy.Span < SimulationDefaults.PupilMinimumSpanMillimetres)
        {
            double middle = anatomy.Centre;
            anatomy.Minimum = middle - 0.5 * SimulationDefaults.PupilMinimumSpanMillimetres;
            anatomy.Maximum = middle + 0.5 * SimulationDefaults.PupilMinimumSpanMillimetres;
        }
        return anatomy;
    }
}

// -------------------------------------------------------------------------------------
//  The pupil signal.
//
//  WHAT IT REPLACES. The old model was an AR(1) chain on the SAMPLES: every row drew a
//  fresh Gaussian, so the trace was white noise with a pull toward a baseline. Two things
//  were wrong with that. It was jagged - the pupil could jump 1 mm between two rows 60 ms
//  apart, which no iris can do - and it was tied to the row rate, so raising SampleRateHz
//  did not trace the same curve more finely, it generated a different and busier one.
//
//  WHAT IT IS. A damped harmonic oscillator driven by noise: a sine wave whose amplitude
//  and phase are continually knocked about. It is smooth (position and velocity are both
//  continuous), so between two adjacent rows the pupil moves by a hair no matter how close
//  together they are, and it carries momentum - once the pupil is opening it tends to keep
//  opening, which is the "tendency in movement" a real iris has and white noise does not.
//
//      p'' = -w^2 (p - centre) - 2 zeta w p' + drive
//
//  It lives in CONTINUOUS TIME and is integrated at a fixed internal rate, independent of
//  SampleRateHz. The CSV samples one and the same curve; more rows per second means a
//  finer trace of it, never a busier one.
//
//  HOW FAST IT MOVES. Two knobs, and they are separate on purpose:
//    * the participant's [Minimum, Maximum] fixes how FAR it can wander - six standard
//      deviations span the range, so the extremes are reached now and then rather than
//      lived at;
//    * the speed ladder fixes how MUCH it moves in one second, in mm, the same for
//      everybody. w is then solved from the two, per participant, so a person with a wide
//      range does not automatically get a faster pupil.
//  The ladder is read off a slow arousal process, so a calm stretch really is calm and an
//  agitated one really is agitated - that spread is what puts both the near-still seconds
//  and the rare 2 mm ones in the distribution, which one fixed speed cannot do.
//
//  THE BASELINE WINDOW. The consumer measures its pupil baseline over the first
//  BaselineSeconds of the file and subtracts it from every diametre after, so what that
//  minute contains decides the sign of every DeltaD in the session. A pupil free to swing
//  across its whole 3-4 mm range during it makes the median a coin toss between the two ends
//  of the swing. So while the window lasts the wander is SQUEEZED VERTICALLY toward the value
//  the session opened on: the same curve at the same pace, reaching at most
//  PupilBaselineExcursionMillimetres either side of it. The opening value itself is still
//  drawn from the stationary distribution - one participant starts their session at 5.1 mm
//  and another at 2.4, exactly as before; what changed is that they now stay there while they
//  are being measured. When the window closes the held value simply BECOMES the oscillator's
//  state, so the trace continues from where it was held with no step and no corner.
//
//  DRIVEN FIXATIONS. After the window the simulator computes the same baseline the reader
//  will (the median of the row-mean diametre over the window, exactly PluginCAISM's rule) and
//  hands it back. From then on every fixation that STARTS is driven ONE WAY ONLY: away from
//  that baseline. The direction is read off the diametre the fixation's first row landed on -
//  above the baseline and it dilates for the whole fixation, below and it constricts - and it
//  is only the SIGN of the movement that is replaced. How far the pupil travels per second is
//  the speed ladder's business and is left exactly as it was. A fixation that reaches the end
//  of the participant's anatomical range repeats that value until it ends, and between
//  fixations the wander is free and unmodified, carrying on from wherever the driven stretch
//  left it. The point of all this is a dataset where every fixation carries an unambiguous
//  DeltaD of one sign, instead of one that averages itself away over the fixation's rows.
// -------------------------------------------------------------------------------------
public class PupilSignal
{
    private readonly DeterministicRandom random;
    private readonly DeterministicRandom jitterRandom;
    private readonly PupilAnthropometry anatomy;
    private readonly double omega;
    private readonly double driveBase;
    private readonly double ladderMean;
    private readonly double centre;
    private readonly double integrationStep;
    private readonly double positionSd;
    private readonly double baselineSeconds;
    private readonly double anchor;

    private double arousal;
    private double position, velocity;
    private double leftEye, rightEye;
    private double clockSeconds;
    private bool released;

    // The baseline the READER will compute out of this file's first minute, handed in by the
    // simulator the moment that window closes, and the fixation currently being driven away
    // from it. Direction is +1 (dilating), -1 (constricting), 0 (free wander).
    private double baseline = double.NaN;
    private int activeFixationIndex = -1;
    private double fixationDirection;

    /// <summary>
    /// The emitted diametre, which is the oscillator's position everywhere except inside the
    /// baseline window - see ShapeForBaseline. Read only after AdvanceTo.
    /// </summary>
    public double Left { get { return leftEye; } }
    public double Right { get { return rightEye; } }

    /// <summary>How many samples were pinned against one of the two anatomical walls.</summary>
    public int WallContactCount;

    /// <summary>Fixations driven away from the baseline, split by which way they went, and
    /// how many of their rows sat pinned against a wall having got there.</summary>
    public int DilatingFixationCount;
    public int ConstrictingFixationCount;
    public int FixationWallHoldCount;

    public PupilSignal(int seed, PupilAnthropometry anatomy, double baselineSeconds)
    {
        this.random = new DeterministicRandom(seed);
        this.anatomy = anatomy;
        this.baselineSeconds = baselineSeconds;

        // The right eye's jitter runs on a stream of ITS OWN, drawn once per emitted row.
        // Sharing the integration stream would have the row rate decide which numbers the
        // oscillator got, which is exactly the sample-rate dependence this model exists to
        // remove.
        this.jitterRandom = new DeterministicRandom(unchecked(seed ^ 0x21771E2));

        positionSd = anatomy.Span / SimulationDefaults.PupilRangeInStandardDeviations;
        ladderMean = LadderMean(SimulationDefaults.PupilSpeedLadder);
        omega = SolveOmega(positionSd, ladderMean, SimulationDefaults.PupilDamping);
        driveBase = positionSd * 2.0 * Math.Sqrt(SimulationDefaults.PupilDamping) *
                    Math.Pow(omega, 1.5);
        integrationStep = 1.0 / SimulationDefaults.PupilIntegrationRateHz;
        centre = anatomy.Centre;

        // Started AT its stationary distribution, not at the centre: a session that opened
        // with the pupil at rest every time would be a tell.
        arousal = random.NextGaussian();
        position = Clamp(centre + positionSd * random.NextGaussian());
        velocity = omega * positionSd * random.NextGaussian();

        // The value the baseline window holds the pupil near. It is the FIRST SAMPLE's, not
        // the centre of the range: the participant who opened at 5.1 mm is baselined at
        // 5.1 mm, which is the whole point of a per-session baseline.
        anchor = position;
        leftEye = position;
        rightEye = position;
    }

    /// <summary>The natural frequency, for the report.</summary>
    public double CyclesPerSecond { get { return omega / (2.0 * Math.PI); } }

    /// <summary>
    /// The baseline the reader will measure out of the first window of this file, handed in
    /// by the simulator the moment that window closes. Every fixation that STARTS after this
    /// is driven one way only: away from it.
    /// </summary>
    public void SetBaseline(double value) { baseline = value; }

    /// <summary>
    /// Integrates up to the given session time. Called once per emitted sample, including
    /// during blinks: the pupil keeps moving behind a shut eyelid, and freezing it would
    /// put a kink in the curve at every blink.
    /// </summary>
    /// <param name="fixationIndex">
    /// The fixation this sample belongs to, or -1 between fixations. A CHANGE of index is
    /// what starts a new one; the direction is latched from the diametre that first sample
    /// lands on, so that first row is still free wander.
    /// </param>
    public void AdvanceTo(double timeSeconds, int fixationIndex)
    {
        double zeta = SimulationDefaults.PupilDamping;
        double arousalTau = SimulationDefaults.PupilArousalTimeConstantSeconds;

        // Leaving the baseline window. The held VALUE becomes the oscillator's state rather
        // than the squeeze being faded out around it: fading needs a blend, and during a
        // blend a fixation's direction cannot be honoured exactly. Position carries over as
        // it was written and velocity is scaled by the squeeze's own slope, so both are
        // continuous across the instant - no step, no corner - and from here the wander is
        // the plain model again, starting from where it was held.
        if (!released && baselineSeconds > 0.0 && timeSeconds > baselineSeconds)
        {
            velocity *= SqueezeSlope(position - anchor);
            position = leftEye;
            released = true;
        }

        bool fixationStartsHere = fixationIndex >= 0 && fixationIndex != activeFixationIndex;
        if (fixationIndex != activeFixationIndex)
        {
            // Between fixations, and through the whole of the one that is just beginning, the
            // pupil is free. Ending a fixation leaves position and velocity exactly where the
            // driven stretch put them, so the wander simply carries on from there.
            activeFixationIndex = fixationIndex;
            fixationDirection = 0.0;
        }

        while (clockSeconds < timeSeconds - 1e-12)
        {
            double step = Math.Min(integrationStep, timeSeconds - clockSeconds);

            arousal += -arousal / arousalTau * step +
                       Math.Sqrt(2.0 * step / arousalTau) * random.NextGaussian();
            double speed = SpeedFromLadder(SimulationDefaults.PupilSpeedLadder,
                DeterministicRandom.NormalCdf(arousal));
            double drive = driveBase * (speed / ladderMean) * Math.Sqrt(step);

            Integrate(ref position, ref velocity, centre, zeta, step,
                drive * random.NextGaussian(), fixationDirection);

            clockSeconds += step;
        }

        // What the row actually gets: the oscillator, squeezed toward the opening value while
        // the baseline window lasts and untouched after it.
        double shaped = released || baselineSeconds <= 0.0 ? position : ShapeForBaseline(position);
        leftEye = Clamp(shaped);

        // ONE flow, two columns. The right pupil is the left one plus a jitter of at most
        // PupilRightEyeJitterMillimetres either way, clamped into the same anatomical range
        // - so the two columns track each other instead of wandering apart, which is what
        // two independent oscillators did however tightly they were correlated.
        double jitter = jitterRandom.NextInRange(
            -SimulationDefaults.PupilRightEyeJitterMillimetres,
            SimulationDefaults.PupilRightEyeJitterMillimetres);
        rightEye = Clamp(shaped + jitter);

        // The direction, latched now that the fixation's opening diametre exists. Reading it
        // off the emitted value rather than off the raw oscillator is deliberate: it is the
        // number in the FILE the reader compares against the baseline, so it is the number
        // that has to decide which way this fixation goes.
        if (fixationStartsHere && !double.IsNaN(baseline))
        {
            fixationDirection = leftEye >= baseline ? 1.0 : -1.0;
            if (fixationDirection > 0.0) DilatingFixationCount++; else ConstrictingFixationCount++;
        }

        if (leftEye <= anatomy.Minimum + 1e-9 || leftEye >= anatomy.Maximum - 1e-9)
        {
            WallContactCount++;
            // A driven fixation that reaches a wall cannot go further, so it repeats the wall
            // for the rest of its length. That is the intended behaviour, not a defect, but
            // it is worth counting: a session that spends most of its fixations pinned is one
            // whose participant's range is too narrow for the speed they were given.
            if (fixationDirection != 0.0) FixationWallHoldCount++;
        }
    }

    /// <summary>
    /// The baseline squeeze: the same wander, scaled down about the value the session opened
    /// on, so the consumer's baseline window sees a pupil that is where it started rather
    /// than wherever a free minute happened to leave it.
    ///
    /// The scaling is soft rather than clipped. tanh is straight for the small deviations and
    /// bends over as the wander approaches the edge of the band, so the squeezed trace keeps
    /// the SHAPE of the free one - its turning points, its momentum, its calm and agitated
    /// stretches - instead of flattening against a wall. The half-range of the free wander
    /// (three of its standard deviations) is what maps onto the band, so a participant with a
    /// wide anatomical range is squeezed harder than one with a narrow one and both come out
    /// inside the same +/- PupilBaselineExcursionMillimetres.
    /// </summary>
    private double ShapeForBaseline(double raw)
    {
        double halfRange = 0.5 * SimulationDefaults.PupilRangeInStandardDeviations * positionSd;
        if (halfRange <= 0.0) return anchor;
        return anchor + SimulationDefaults.PupilBaselineExcursionMillimetres *
               Math.Tanh((raw - anchor) / halfRange);
    }

    /// <summary>
    /// How steep the squeeze is at the given deviation - the derivative of ShapeForBaseline.
    /// The handover out of the window multiplies the velocity by it, which is what makes the
    /// released trace leave at the speed it appeared to be moving rather than at the speed
    /// the hidden oscillator was moving underneath.
    /// </summary>
    private double SqueezeSlope(double deviation)
    {
        double halfRange = 0.5 * SimulationDefaults.PupilRangeInStandardDeviations * positionSd;
        if (halfRange <= 0.0) return 0.0;
        double t = Math.Tanh(deviation / halfRange);
        return (SimulationDefaults.PupilBaselineExcursionMillimetres / halfRange) * (1.0 - t * t);
    }

    /// <summary>
    /// One integration step. <paramref name="direction"/> is 0 for the free wander and +/-1
    /// while a fixation is being driven away from the baseline.
    /// </summary>
    private void Integrate(ref double position, ref double velocity, double centre,
        double zeta, double step, double kick, double direction)
    {
        double before = position;

        velocity += (-omega * omega * (position - centre) - 2.0 * zeta * omega * velocity) * step + kick;
        position += velocity * step;

        if (direction != 0.0)
        {
            // The fixation decides WHICH WAY, the oscillator decides HOW FAR. Only the sign
            // of the DISPLACEMENT is replaced; |dp| is whatever the model produced, so the
            // speed the ladder sets is untouched. Between fixations direction is 0 and this
            // is the plain model.
            //
            // The velocity is deliberately NOT re-signed with it. Doing that was the first
            // attempt and it destroys the signal: every substep the restoring pull drags v
            // back toward zero, the re-signing flips whatever is left, and the pupil ends up
            // creeping at a fraction of its proper pace - the one-second change collapsed to
            // 92 % under 0.3 mm. Left alone, v keeps its own magnitude and its own sign, the
            // step keeps the size the ladder asked for, and a fixation driven against the
            // pull builds up the velocity that snaps the pupil back once it ends.
            position = before + direction * Math.Abs(position - before);
        }

        // The iris cannot open past its own maximum. Killing only the OUTWARD half of the
        // velocity leaves the pupil free to come straight back off the wall, which is why
        // the contact reads as a pause rather than as a bounce - and why a driven fixation
        // that reaches the wall simply repeats it until it ends: the drive keeps pushing
        // outward, the clamp keeps putting it back, and the row never changes.
        if (position < anatomy.Minimum) { position = anatomy.Minimum; if (velocity < 0.0) velocity = 0.0; }
        if (position > anatomy.Maximum) { position = anatomy.Maximum; if (velocity > 0.0) velocity = 0.0; }
    }

    private double Clamp(double value)
    {
        return VectorMath.Clamp(value, anatomy.Minimum, anatomy.Maximum);
    }

    private static double LadderMean(double[] ladder)
    {
        double sum = 0.0;
        for (int i = 0; i < ladder.Length - 1; i++) sum += 0.5 * (ladder[i] + ladder[i + 1]);
        return sum / (ladder.Length - 1);
    }

    /// <summary>Speed in mm per second at the given arousal quantile, linearly interpolated.</summary>
    private static double SpeedFromLadder(double[] ladder, double quantile)
    {
        if (quantile <= 0.0) return ladder[0];
        if (quantile >= 1.0) return ladder[ladder.Length - 1];
        double x = quantile * (ladder.Length - 1);
        int index = (int)x;
        if (index >= ladder.Length - 1) return ladder[ladder.Length - 1];
        double fraction = x - index;
        return ladder[index] * (1.0 - fraction) + ladder[index + 1] * fraction;
    }

    /// <summary>Lag-one-second autocorrelation of the oscillator's position.</summary>
    private static double Autocorrelation(double omega, double zeta)
    {
        if (zeta >= 1.0) return Math.Exp(-omega) * (1.0 + omega);
        double damped = omega * Math.Sqrt(1.0 - zeta * zeta);
        return Math.Exp(-zeta * omega) *
               (Math.Cos(damped) + (zeta * omega / damped) * Math.Sin(damped));
    }

    /// <summary>
    /// The natural frequency that turns a wander of the given width into a move of the
    /// given size per second: var(p(t+1) - p(t)) = 2 var(p) (1 - rho(1)), solved for rho
    /// and then for omega.
    ///
    /// Underdamped, rho is not monotone in omega - it rings - so this walks up from slow to
    /// fast and takes the FIRST crossing, the branch on which a faster oscillator really
    /// does mean a bigger one-second move.
    /// </summary>
    private static double SolveOmega(double positionSd, double oneSecondSd, double zeta)
    {
        const double Slowest = 0.05, Fastest = 12.0;
        if (positionSd < 1e-9) return Slowest;

        double target = 1.0 - (oneSecondSd * oneSecondSd) / (2.0 * positionSd * positionSd);
        if (target >= 1.0) return Slowest;
        if (target <= -1.0) return Fastest;

        double previousOmega = Slowest, previousRho = Autocorrelation(Slowest, zeta);
        for (double omega = Slowest + 0.01; omega <= Fastest; omega += 0.01)
        {
            double rho = Autocorrelation(omega, zeta);
            if (previousRho >= target && rho <= target)
            {
                double span = previousRho - rho;
                double fraction = span > 1e-12 ? (previousRho - target) / span : 0.0;
                return previousOmega + fraction * (omega - previousOmega);
            }
            previousOmega = omega; previousRho = rho;
        }
        return Fastest;
    }
}

// -------------------------------------------------------------------------------------
//  Quaternion in explicit X, Y, Z, W order.
//
//  Rhino's own Quaternion stores (A,B,C,D) = (W,X,Y,Z), which is a very easy way to
//  write the CSV columns out in the wrong order, so the maths is kept local and
//  unambiguous here. Hamilton convention, v' = q * v * conj(q).
// -------------------------------------------------------------------------------------
public struct QuaternionXyzw
{
    public double X, Y, Z, W;

    public QuaternionXyzw(double x, double y, double z, double w)
    {
        X = x; Y = y; Z = z; W = w;
    }

    public static QuaternionXyzw Identity
    {
        get { return new QuaternionXyzw(0, 0, 0, 1); }
    }

    public double Norm
    {
        get { return Math.Sqrt(X * X + Y * Y + Z * Z + W * W); }
    }

    public QuaternionXyzw Normalized()
    {
        double norm = Norm;
        if (norm < 1e-12) return Identity;
        return new QuaternionXyzw(X / norm, Y / norm, Z / norm, W / norm);
    }

    public static QuaternionXyzw FromAxisAngle(Vector3d axis, double radians)
    {
        Vector3d unitAxis = VectorMath.Unit(axis);
        double half = radians * 0.5;
        double s = Math.Sin(half);
        return new QuaternionXyzw(unitAxis.X * s, unitAxis.Y * s, unitAxis.Z * s, Math.Cos(half));
    }

    public static QuaternionXyzw Multiply(QuaternionXyzw a, QuaternionXyzw b)
    {
        return new QuaternionXyzw(
            a.W * b.X + a.X * b.W + a.Y * b.Z - a.Z * b.Y,
            a.W * b.Y - a.X * b.Z + a.Y * b.W + a.Z * b.X,
            a.W * b.Z + a.X * b.Y - a.Y * b.X + a.Z * b.W,
            a.W * b.W - a.X * b.X - a.Y * b.Y - a.Z * b.Z);
    }

    public Vector3d Rotate(Vector3d v)
    {
        Vector3d q = new Vector3d(X, Y, Z);
        Vector3d t = 2.0 * VectorMath.Cross(q, v);
        return v + W * t + VectorMath.Cross(q, t);
    }

    /// <summary>
    /// Rotation taking <paramref name="from"/> onto <paramref name="to"/> by the shortest arc.
    /// </summary>
    public static QuaternionXyzw FromToRotation(Vector3d from, Vector3d to)
    {
        Vector3d a = VectorMath.Unit(from);
        Vector3d b = VectorMath.Unit(to);
        double dot = VectorMath.Dot(a, b);

        if (dot > 1.0 - 1e-10) return Identity;

        if (dot < -1.0 + 1e-10)
        {
            // Antiparallel: any perpendicular axis is a valid 180 degree rotation.
            Vector3d axis = VectorMath.Cross(a, new Vector3d(1, 0, 0));
            if (axis.Length < 1e-8) axis = VectorMath.Cross(a, new Vector3d(0, 1, 0));
            axis = VectorMath.Unit(axis);
            return new QuaternionXyzw(axis.X, axis.Y, axis.Z, 0.0);
        }

        Vector3d cross = VectorMath.Cross(a, b);
        double w = 1.0 + dot;
        return new QuaternionXyzw(cross.X, cross.Y, cross.Z, w).Normalized();
    }

    /// <summary>
    /// Rotation whose local +Z maps onto <paramref name="forward"/> and whose local +Y is as
    /// close to <paramref name="upHint"/> as possible - the same contract as Unity's
    /// Quaternion.LookRotation, so the emitted playerRotation is meaningful in Unity.
    /// </summary>
    public static QuaternionXyzw LookRotation(Vector3d forward, Vector3d upHint)
    {
        Vector3d f = VectorMath.Unit(forward);
        Vector3d up = VectorMath.Unit(upHint);
        if (Math.Abs(VectorMath.Dot(f, up)) > 0.999)
            up = Math.Abs(f.Y) > 0.9 ? new Vector3d(0, 0, 1) : new Vector3d(0, 1, 0);

        Vector3d right = VectorMath.Unit(VectorMath.Cross(up, f));
        Vector3d trueUp = VectorMath.Cross(f, right);

        // Columns of the rotation matrix are (right, trueUp, forward); Shepperd's method.
        double m00 = right.X, m01 = trueUp.X, m02 = f.X;
        double m10 = right.Y, m11 = trueUp.Y, m12 = f.Y;
        double m20 = right.Z, m21 = trueUp.Z, m22 = f.Z;

        double trace = m00 + m11 + m22;
        double x, y, z, w;
        if (trace > 0.0)
        {
            double s = Math.Sqrt(trace + 1.0) * 2.0;
            w = 0.25 * s;
            x = (m21 - m12) / s;
            y = (m02 - m20) / s;
            z = (m10 - m01) / s;
        }
        else if (m00 > m11 && m00 > m22)
        {
            double s = Math.Sqrt(1.0 + m00 - m11 - m22) * 2.0;
            w = (m21 - m12) / s;
            x = 0.25 * s;
            y = (m01 + m10) / s;
            z = (m02 + m20) / s;
        }
        else if (m11 > m22)
        {
            double s = Math.Sqrt(1.0 + m11 - m00 - m22) * 2.0;
            w = (m02 - m20) / s;
            x = (m01 + m10) / s;
            y = 0.25 * s;
            z = (m12 + m21) / s;
        }
        else
        {
            double s = Math.Sqrt(1.0 + m22 - m00 - m11) * 2.0;
            w = (m10 - m01) / s;
            x = (m02 + m20) / s;
            y = (m12 + m21) / s;
            z = 0.25 * s;
        }
        return new QuaternionXyzw(x, y, z, w).Normalized();
    }
}

// -------------------------------------------------------------------------------------
//  Small vector helpers. Written explicitly so the maths reads the same whether or not
//  Rhino's operator overloads are in scope.
// -------------------------------------------------------------------------------------
public static class VectorMath
{
    public const double DegreesPerRadian = 180.0 / Math.PI;
    public const double RadiansPerDegree = Math.PI / 180.0;

    public static double Dot(Vector3d a, Vector3d b)
    {
        return a.X * b.X + a.Y * b.Y + a.Z * b.Z;
    }

    public static Vector3d Cross(Vector3d a, Vector3d b)
    {
        return new Vector3d(
            a.Y * b.Z - a.Z * b.Y,
            a.Z * b.X - a.X * b.Z,
            a.X * b.Y - a.Y * b.X);
    }

    public static Vector3d Unit(Vector3d v)
    {
        double length = v.Length;
        if (length < 1e-12) return new Vector3d(0, 0, 1);
        return new Vector3d(v.X / length, v.Y / length, v.Z / length);
    }

    public static double AngleDegrees(Vector3d a, Vector3d b)
    {
        double dot = Dot(Unit(a), Unit(b));
        if (dot > 1.0) dot = 1.0;
        if (dot < -1.0) dot = -1.0;
        return Math.Acos(dot) * DegreesPerRadian;
    }

    public static double Clamp(double value, double minimum, double maximum)
    {
        if (value < minimum) return minimum;
        if (value > maximum) return maximum;
        return value;
    }

    /// <summary>Rotate <paramref name="direction"/> by an angle toward a tangent offset.</summary>
    public static Vector3d RotateInTangentPlane(Vector3d direction, Vector3d tangentOffsetUnit, double angleDegrees)
    {
        double radians = angleDegrees * RadiansPerDegree;
        Vector3d d = Unit(direction);
        Vector3d t = Unit(tangentOffsetUnit - Dot(tangentOffsetUnit, d) * d);
        return Unit(Math.Cos(radians) * d + Math.Sin(radians) * t);
    }
}

// -------------------------------------------------------------------------------------
//  The seat frame: yaw and pitch are always measured relative to the seat's neutral
//  forward direction, because that is what the neck limits are defined against.
//
//  No handedness conversion is applied anywhere. Grasshopper world coordinates are
//  treated as already being in the same frame as the Unity data.
// -------------------------------------------------------------------------------------
public class SeatFrame
{
    public readonly Vector3d Forward;
    public readonly Vector3d Right;
    public readonly Vector3d Up;

    public SeatFrame(Vector3d neutralForward)
    {
        Forward = VectorMath.Unit(neutralForward);
        Vector3d worldUp = new Vector3d(0, 1, 0);
        if (Math.Abs(VectorMath.Dot(Forward, worldUp)) > 0.999)
            worldUp = new Vector3d(0, 0, 1);
        Right = VectorMath.Unit(VectorMath.Cross(worldUp, Forward));
        Up = VectorMath.Cross(Forward, Right);
    }

    public void ToYawPitchDegrees(Vector3d direction, out double yawDegrees, out double pitchDegrees)
    {
        Vector3d d = VectorMath.Unit(direction);
        double x = VectorMath.Dot(d, Right);
        double y = VectorMath.Dot(d, Up);
        double z = VectorMath.Dot(d, Forward);
        yawDegrees = Math.Atan2(x, z) * VectorMath.DegreesPerRadian;
        pitchDegrees = Math.Asin(VectorMath.Clamp(y, -1.0, 1.0)) * VectorMath.DegreesPerRadian;
    }

    public Vector3d FromYawPitchDegrees(double yawDegrees, double pitchDegrees)
    {
        double yaw = yawDegrees * VectorMath.RadiansPerDegree;
        double pitch = pitchDegrees * VectorMath.RadiansPerDegree;
        double cosPitch = Math.Cos(pitch);
        return VectorMath.Unit(
            (cosPitch * Math.Sin(yaw)) * Right +
            Math.Sin(pitch) * Up +
            (cosPitch * Math.Cos(yaw)) * Forward);
    }
}

// -------------------------------------------------------------------------------------
//  Spatial index over the star of direction vectors.
//
//  Nearest-vector lookup runs on the order of 10^4 times against 10^4-10^5 vectors, so a
//  linear scan is far too slow. Vectors are binned into yaw/pitch cells whose yaw width
//  grows as 1/cos(pitch), which keeps every cell roughly CellDegrees across in true
//  angular terms - that is what makes the expanding-radius search provably correct.
//
//  A star entry whose ray hit nothing is kept in the parallel arrays - the three input
//  lists must stay index-aligned - but is left OUT of the buckets and skipped by the full
//  scan, so NearestIndex can never return one. Every target the walk picks goes through
//  NearestIndex, which is what makes "the gaze never lands on a ray that hit nothing" hold
//  for the whole simulation rather than at one call site.
// -------------------------------------------------------------------------------------
public class StarDirectionIndex
{
    private const double CellDegrees = 5.0;

    private readonly Vector3d[] directions;
    private readonly double[] yawDegrees;
    private readonly double[] pitchDegrees;
    private readonly bool[] selectable;
    private readonly int selectableCount;
    private readonly int firstSelectableIndex = -1;

    private readonly int rowCount;
    private readonly int[] cellsPerRow;
    private readonly List<int>[][] buckets;

    /// <summary>
    /// A ray is a usable gaze target only if it actually hit a named object. The planner
    /// writes <see cref="SimulationDefaults.InvalidObjectName"/> for a ray that hit nothing,
    /// which is the same marker the logger writes for a dropped frame.
    /// </summary>
    public static bool IsSelectableName(string objectName)
    {
        if (string.IsNullOrEmpty(objectName)) return false;
        string trimmed = objectName.Trim();
        if (trimmed.Length == 0) return false;
        return !string.Equals(trimmed, SimulationDefaults.InvalidObjectName, StringComparison.OrdinalIgnoreCase);
    }

    public static int CountSelectable(IList<string> starObjectNames)
    {
        if (starObjectNames == null) return 0;
        int count = 0;
        for (int i = 0; i < starObjectNames.Count; i++)
            if (IsSelectableName(starObjectNames[i])) count++;
        return count;
    }

    public StarDirectionIndex(IList<Vector3d> starVectors, IList<string> starObjectNames, SeatFrame seatFrame)
    {
        int count = starVectors.Count;
        directions = new Vector3d[count];
        yawDegrees = new double[count];
        pitchDegrees = new double[count];
        selectable = new bool[count];
        for (int i = 0; i < count; i++)
        {
            selectable[i] = starObjectNames != null && i < starObjectNames.Count &&
                            IsSelectableName(starObjectNames[i]);
            if (selectable[i])
            {
                selectableCount++;
                if (firstSelectableIndex < 0) firstSelectableIndex = i;
            }
        }

        rowCount = (int)Math.Ceiling(180.0 / CellDegrees);
        cellsPerRow = new int[rowCount];
        buckets = new List<int>[rowCount][];
        for (int row = 0; row < rowCount; row++)
        {
            double rowCentrePitch = -90.0 + (row + 0.5) * CellDegrees;
            double cosLatitude = Math.Max(Math.Cos(rowCentrePitch * VectorMath.RadiansPerDegree), 0.02);
            int columns = Math.Max(1, (int)Math.Ceiling(360.0 * cosLatitude / CellDegrees));
            cellsPerRow[row] = columns;
            buckets[row] = new List<int>[columns];
            for (int column = 0; column < columns; column++)
                buckets[row][column] = new List<int>();
        }

        for (int i = 0; i < count; i++)
        {
            // Normalise defensively: the brief allows the incoming star to be un-unitised.
            directions[i] = VectorMath.Unit(starVectors[i]);
            double yaw, pitch;
            seatFrame.ToYawPitchDegrees(directions[i], out yaw, out pitch);
            yawDegrees[i] = yaw;
            pitchDegrees[i] = pitch;

            // Rays that hit nothing are indexed for lookup by index but never offered as a
            // target: they are not put in a bucket.
            if (!selectable[i]) continue;

            int row = RowIndex(pitch);
            int column = ColumnIndex(row, yaw);
            buckets[row][column].Add(i);
        }
    }

    public Vector3d Direction(int index) { return directions[index]; }
    public double YawDegrees(int index) { return yawDegrees[index]; }
    public double PitchDegrees(int index) { return pitchDegrees[index]; }

    public int Count { get { return directions.Length; } }
    public int SelectableCount { get { return selectableCount; } }
    public int IgnoredCount { get { return directions.Length - selectableCount; } }
    public bool IsSelectable(int index)
    {
        return index >= 0 && index < selectable.Length && selectable[index];
    }

    /// <summary>
    /// The selectable star entry closest to <paramref name="index"/>, or the index itself
    /// when it is already selectable. Used to repair a StartVectorIndex that points at a
    /// ray which hit nothing.
    /// </summary>
    public int NearestSelectableIndex(int index, SeatFrame seatFrame)
    {
        if (IsSelectable(index)) return index;
        if (selectableCount == 0) return index;
        if (index < 0 || index >= directions.Length) return firstSelectableIndex;
        return NearestIndex(directions[index], seatFrame);
    }

    private int RowIndex(double pitch)
    {
        int row = (int)((pitch + 90.0) / CellDegrees);
        if (row < 0) row = 0;
        if (row >= rowCount) row = rowCount - 1;
        return row;
    }

    private int ColumnIndex(int row, double yaw)
    {
        int columns = cellsPerRow[row];
        double normalised = (yaw + 180.0) / 360.0;
        int column = (int)(normalised * columns);
        if (column < 0) column = 0;
        if (column >= columns) column = columns - 1;
        return column;
    }

    /// <summary>
    /// Index of the star vector closest to <paramref name="target"/>. The search radius
    /// doubles until the best candidate found is provably inside the region scanned; a
    /// full scan is the last resort for a star with large empty regions.
    /// </summary>
    public int NearestIndex(Vector3d target, SeatFrame seatFrame)
    {
        double targetYaw, targetPitch;
        seatFrame.ToYawPitchDegrees(target, out targetYaw, out targetPitch);
        Vector3d unitTarget = VectorMath.Unit(target);

        for (double searchRadius = CellDegrees; searchRadius <= 180.0; searchRadius *= 2.0)
        {
            double window = searchRadius + CellDegrees;
            int bestIndex = -1;
            double bestDot = -2.0;

            int firstRow = RowIndex(targetPitch - window);
            int lastRow = RowIndex(targetPitch + window);
            for (int row = firstRow; row <= lastRow; row++)
            {
                double rowCentrePitch = -90.0 + (row + 0.5) * CellDegrees;
                double cosLatitude = Math.Max(Math.Cos(rowCentrePitch * VectorMath.RadiansPerDegree), 0.02);
                double yawHalfWidth = window / cosLatitude;

                int columns = cellsPerRow[row];
                int firstColumn, lastColumn;
                if (yawHalfWidth >= 180.0)
                {
                    firstColumn = 0;
                    lastColumn = columns - 1;
                }
                else
                {
                    firstColumn = (int)Math.Floor((targetYaw - yawHalfWidth + 180.0) / 360.0 * columns);
                    lastColumn = (int)Math.Ceiling((targetYaw + yawHalfWidth + 180.0) / 360.0 * columns);
                }

                for (int rawColumn = firstColumn; rawColumn <= lastColumn; rawColumn++)
                {
                    int column = ((rawColumn % columns) + columns) % columns;   // yaw wraps
                    List<int> bucket = buckets[row][column];
                    for (int k = 0; k < bucket.Count; k++)
                    {
                        int candidate = bucket[k];
                        double dot = VectorMath.Dot(unitTarget, directions[candidate]);
                        if (dot > bestDot)
                        {
                            bestDot = dot;
                            bestIndex = candidate;
                        }
                    }
                }
            }

            if (bestIndex >= 0)
            {
                double bestAngle = Math.Acos(VectorMath.Clamp(bestDot, -1.0, 1.0)) * VectorMath.DegreesPerRadian;
                if (bestAngle <= searchRadius) return bestIndex;
            }
        }

        return FullScanNearestIndex(unitTarget);
    }

    private int FullScanNearestIndex(Vector3d unitTarget)
    {
        int bestIndex = firstSelectableIndex;
        double bestDot = -2.0;
        for (int i = 0; i < directions.Length; i++)
        {
            if (!selectable[i]) continue;
            double dot = VectorMath.Dot(unitTarget, directions[i]);
            if (dot > bestDot) { bestDot = dot; bestIndex = i; }
        }
        return bestIndex;
    }
}

// -------------------------------------------------------------------------------------
//  One emitted sample - everything needed to write a CSV row and to run the self-check.
// -------------------------------------------------------------------------------------
public class GazeSample
{
    public double TimeSeconds;
    public int StarIndex;
    public int FixationIndex;          // -1 while transitioning between fixations
    public bool IsInvalidFrame;
    public bool IsBlinkFrame;

    public string ObjectName;
    public Point3d HitPoint;

    public Point3d PlayerPosition;
    public QuaternionXyzw PlayerRotation;

    public Vector3d LeftEyeGazeColumn;
    public Vector3d RightEyeGazeColumn;
    public QuaternionXyzw LeftEyeOrientation;
    public QuaternionXyzw RightEyeOrientation;

    public double LeftPupilDiametre;
    public double RightPupilDiametre;
    public double LeftEyeOpened;
    public double RightEyeOpened;

    // Diagnostics, not written to the CSV.
    public Vector3d LeftEyeInHeadDirection;
    public Vector3d RightEyeInHeadDirection;
    public Vector3d GazeDirectionWorld;
    public Vector3d HeadForwardWorld;
    public double HeadYawDegrees;
    public double HeadPitchDegrees;
    public double EyeInHeadYawDegrees;
    public double EyeInHeadPitchDegrees;
}

// -------------------------------------------------------------------------------------
//  Result handed back to RunScript.
// -------------------------------------------------------------------------------------
public class SimulationResult
{
    public bool Succeeded;
    public string FailureMessage = "";
    public string ReportText = "";
    public string CsvPath = "";
    public List<Point3d> FixationCentroids = new List<Point3d>();
    public List<double> FixationDurations = new List<double>();
    public List<Vector3d> HeadForwardPerFixation = new List<Vector3d>();

    // Per emitted sample, in chronological order - the reverse of the CSV row order.
    // Invalid frames repeat the previous hit point, exactly as the CSV does.
    public List<Point3d> HitPoints = new List<Point3d>();
    public List<Point3d> EyePositions = new List<Point3d>();

    // Also per emitted sample: the instant it was taken, and the left pupil as written
    // (so -1 on a blink frame). The self-check reads the pupil trace off these rather than
    // parsing its own CSV back.
    public List<double> SampleTimes = new List<double>();
    public List<double> LeftPupilDiametres = new List<double>();

    /// <summary>
    /// Which fixation each emitted sample belongs to, -1 between them. Parallel to the two
    /// lists above. It is the driven-fixation checks that need it: a reader of the CSV can
    /// only recover fixations with I-VT, but the bench has to judge the drive against the
    /// fixations the generator actually planned.
    /// </summary>
    public List<int> SampleFixationIndices = new List<int>();

    /// <summary>
    /// The baseline this session's rows produce under the reader's rule, and the two ends of
    /// the participant's anatomical range - the three numbers the driven fixations are
    /// defined against. Baseline is NaN when no window was asked for.
    /// </summary>
    public double PupilBaseline = double.NaN;
    public double PupilRangeMinimum;
    public double PupilRangeMaximum;
}

/// <summary>One planned and executed fixation.</summary>
public class FixationRecord
{
    public int StarIndex;
    public double StartSeconds;
    public double PlannedDurationSeconds;
    public double SaccadeAmplitudeDegrees;     // amplitude of the saccade that led into it
    public Vector3d HeadForwardWorld;
    public Point3d Centroid;
    public int SampleCount;
}

/// <summary>Everything one pass of the simulation produced.</summary>
public class SimulationRun
{
    public List<GazeSample> Samples = new List<GazeSample>();
    public List<FixationRecord> Fixations = new List<FixationRecord>();
    public HashSet<string> VisitedObjectNames = new HashSet<string>();
    public int BlinkEventCount;
    public int BlinkFrameCount;
    public int InvalidFrameCount;
    public int FreeScanningSamples;
    public int FreeScanningStalls;
    public int SaccadeHoldCount;                // corrective pauses inserted into transitions
    public int SaccadeHoldSampleCount;          // frames those pauses repeated the hit point

    /// <summary>
    /// The participant's pupil range, and how fast their pupil wandered. Carried on the run
    /// because the report needs both and neither is a property of the session: the range is
    /// the same in every file this person appears in.
    /// </summary>
    public PupilAnthropometry PupilAnatomy;
    public double PupilCyclesPerSecond;

    /// <summary>
    /// The baseline this session's own rows produce under the reader's rule - the median of
    /// the row-mean diametre over the baseline window. NaN when no window was asked for, or
    /// when the window held no valid pupil row. Every fixation after the window is driven
    /// away from this number; see PupilSignal.
    /// </summary>
    public double PupilBaseline = double.NaN;

    /// <summary>How the driven fixations split, and how many of their rows repeated an
    /// anatomical limit having reached it. See PupilSignal.</summary>
    public int DilatingFixationCount;
    public int ConstrictingFixationCount;
    public int FixationWallHoldCount;

    // When each pause ran. A pause holds the gaze perfectly still, so I-VT recovers it as a
    // fixation nobody asked for; knowing the exact spans lets the self-check subtract them
    // instead of widening its tolerance and going blind to real drift.
    public List<double> SaccadeHoldStartSeconds = new List<double>();
    public List<double> SaccadeHoldEndSeconds = new List<double>();

    // Angular size of every movement the walk actually made - the inter-fixation saccades
    // and the free-scanning hops alike. This, not the distance between detected fixation
    // centroids, is the saccade amplitude distribution once free scanning exists.
    public List<double> RealisedSaccadeAmplitudes = new List<double>();
    public int EyeBudgetViolationCount;
    public int UnreachableTargetFallbackCount;

    // How much of the incoming star was usable. Rays that hit nothing stay in the input
    // lists to keep them index-aligned, but are never offered as a gaze target.
    public int StarSelectableCount;
    public int StarIgnoredCount;
    public int DistinctObjectTargetLow;         // requested minimum, 0 when steering is off
    public int DistinctObjectTargetHigh;
    public List<string> Warnings = new List<string>();
}

// -------------------------------------------------------------------------------------
//  The simulation itself.
//
//  Stages 3, 4 and 5 of the brief are interleaved in one forward pass rather than run
//  one after another, because validating a saccade target (stage 3, step 5) needs the
//  live head state that only exists while samples are being emitted (stage 5).
// -------------------------------------------------------------------------------------
public class SessionSimulator
{
    // Transition shaping. These two are derived from the I-VT threshold rather than
    // guessed: intra-fixation micro-jitter must stay BELOW the 30 deg/s threshold or a
    // fixation would fragment, and inter-fixation samples must stay ABOVE it or two
    // fixations would merge into one.
    private const double IntraFixationStepSafetyFactor = 0.80;
    private const double TransitionStepSafetyFactor = 1.25;

    private const double VerticalCompressionFactor = 0.60;   // scanpaths favour horizontal ~2:1
    private const int MaximumTargetAttempts = 20;
    private const int CandidatePoolSize = 6;
    // The time between two fixations is spent in two states. The tail of it is the APPROACH:
    // the saccade itself plus its corrective movements, landing on the next fixation. Any
    // time before that is FREE SCANNING: the eye moving every sample along a random path,
    // never dwelling long enough to register as a fixation.
    //
    // When the requested fixations nearly fill the session the approach is the whole gap and
    // there is no free scanning, which is the classic behaviour. When few fixations are
    // requested over a long session, almost all of it is free scanning - which is what
    // "the eye moves a lot but only settles a few times" has to mean.
    private const double MinimumTransitionSeconds = 0.02;
    private const double MaximumApproachSeconds = 0.60;
    // Three related margins, and the ordering between them is what guarantees criterion 10.
    //   * the re-centering routine settles the eye at 85 % of its limit;
    //   * a saccade target is only accepted if it can be reached inside 92 % of the limit;
    //   * the 8 % that remains absorbs head roll, which keeps drifting after the target was
    //     chosen and slightly rotates the eye-in-head yaw/pitch decomposition.
    // Because the settle target is stricter than the acceptance margin, a head pinned at the
    // neck limit still leaves the eye inside its own limit.
    private const double EyeBudgetSettleFactor = 0.85;
    private const double ReachabilityMarginFactor = 0.92;
    private const int EyeBudgetIterations = 16;

    private SimulationSettings settings;
    private SeatFrame seatFrame;
    private StarDirectionIndex starIndex;
    private DeterministicRandom random;

    private double sampleIntervalSeconds;
    private double minimumSampleIntervalSeconds;
    private double maximumSampleIntervalSeconds;
    private double intraFixationMaximumStepDegrees;
    private double transitionMinimumStepDegrees;

    // Head state, in seat-frame degrees.
    private double headYawDegrees, headPitchDegrees, headRollDegrees;
    private double headTargetYawDegrees, headTargetPitchDegrees;

    // Seat sway, pupil and blink state.
    private double swayX, swayY, swayZ;
    private PupilAnthropometry pupilAnatomy;
    private PupilSignal pupils;
    private bool pupilBaselineAssigned;
    private double eyeGazeColumnScale;
    private double eyeRollDegrees;
    private double blinkStartSeconds, blinkEndSeconds, nextBlinkSeconds;
    private bool blinkActive;

    private double previousSampleTimeSeconds;
    private Point3d previousHitPoint;
    private bool hasPreviousHitPoint;
    private int distinctObjectsAvailable;

    private static string Fmt(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }

    public SimulationResult Generate(SimulationSettings inputSettings)
    {
        settings = inputSettings;
        SimulationResult result = new SimulationResult();

        string validationFailure = Validate();
        if (validationFailure != null)
        {
            result.Succeeded = false;
            result.FailureMessage = validationFailure;
            result.ReportText = "GENERATION FAILED\r\n" + validationFailure;
            return result;
        }

        if (settings.ConvertZUpToYUp) ApplyZUpToYUpConversion(settings);

        seatFrame = new SeatFrame(settings.NeutralForward);
        starIndex = new StarDirectionIndex(settings.StarVectors, settings.StarObjectNames, seatFrame);

        // Only rays that actually hit something count as objects the gaze could visit.
        HashSet<string> reachableObjectNames = new HashSet<string>();
        for (int i = 0; i < settings.StarObjectNames.Count; i++)
            if (starIndex.IsSelectable(i)) reachableObjectNames.Add(settings.StarObjectNames[i]);
        distinctObjectsAvailable = reachableObjectNames.Count;

        DateTime sessionStart;
        if (!DateTime.TryParseExact(settings.SessionStartText, "yyyy-MM-dd HH:mm:ss",
                CultureInfo.InvariantCulture, DateTimeStyles.None, out sessionStart) &&
            !DateTime.TryParse(settings.SessionStartText, CultureInfo.InvariantCulture,
                DateTimeStyles.None, out sessionStart))
        {
            result.Succeeded = false;
            result.FailureMessage =
                "SessionStartDateTime could not be parsed. Expected 'yyyy-MM-dd HH:mm:ss', got '" +
                settings.SessionStartText + "'.";
            result.ReportText = "GENERATION FAILED\r\n" + result.FailureMessage;
            return result;
        }

        SimulationRun run = SimulateOnce();
        string csvContent = GazeCsvWriter.BuildFileContent(run.Samples, settings, sessionStart);

        // Acceptance criterion 16: the same seed must produce a byte-identical file. The
        // cheapest honest way to report on that is to simulate a second time and compare.
        // It doubles the cost of a session, so a batch checks only its first one - the
        // generator is either deterministic or it is not, and one session settles it.
        bool? deterministic = null;
        if (!settings.SkipDeterminismCheck)
        {
            SimulationRun repeatRun = SimulateOnce();
            string repeatContent = GazeCsvWriter.BuildFileContent(repeatRun.Samples, settings, sessionStart);
            deterministic = string.Equals(csvContent, repeatContent, StringComparison.Ordinal);
        }

        string writeFailure = TryWriteFile(csvContent, settings.OutputFilePath);
        if (writeFailure != null)
        {
            result.Succeeded = false;
            result.FailureMessage = writeFailure;
            result.ReportText = SessionReport.Build(run, settings, sessionStart, csvContent, deterministic) +
                                "\r\n\r\nFILE NOT WRITTEN\r\n" + writeFailure;
            return result;
        }

        result.Succeeded = true;
        result.CsvPath = settings.OutputFilePath;
        result.ReportText = SessionReport.Build(run, settings, sessionStart, csvContent, deterministic);

        // The geometry outputs go back into the frame the caller supplied, so that the scan
        // path and the rays draw the right way up on the canvas. Only the CSV stays Y-up.
        bool back = settings.ConvertZUpToYUp;
        for (int i = 0; i < run.Fixations.Count; i++)
        {
            result.FixationCentroids.Add(back ? SwapYZ(run.Fixations[i].Centroid) : run.Fixations[i].Centroid);
            result.FixationDurations.Add(run.Fixations[i].PlannedDurationSeconds);
            result.HeadForwardPerFixation.Add(back
                ? SwapYZ(run.Fixations[i].HeadForwardWorld) : run.Fixations[i].HeadForwardWorld);
        }

        for (int i = 0; i < run.Samples.Count; i++)
        {
            result.HitPoints.Add(back ? SwapYZ(run.Samples[i].HitPoint) : run.Samples[i].HitPoint);
            result.EyePositions.Add(back ? SwapYZ(run.Samples[i].PlayerPosition) : run.Samples[i].PlayerPosition);
            result.SampleTimes.Add(run.Samples[i].TimeSeconds);
            result.LeftPupilDiametres.Add(run.Samples[i].LeftPupilDiametre);
            result.SampleFixationIndices.Add(run.Samples[i].FixationIndex);
        }
        result.PupilBaseline = run.PupilBaseline;
        result.PupilRangeMinimum = run.PupilAnatomy.Minimum;
        result.PupilRangeMaximum = run.PupilAnatomy.Maximum;
        return result;
    }

    /// <summary>
    /// Swaps Y and Z on every incoming point and vector, taking geometry from a Z-up frame
    /// (Rhino's default, and Revit's) into the Y-up frame the CSV must be written in.
    ///
    /// It is applied ONCE, here at the entrance, so that everything downstream - the seat
    /// frame, the neck limits, the quaternions, the CSV - lives in one consistent frame.
    /// The swap is its own inverse, and it is the exact inverse of what the Revit plugin
    /// does when it reads the file back (ConversorDeCoordenadas maps Unity (X,Y,Z) onto
    /// Revit (X, Z, Y)), so the full round trip returns the original point.
    ///
    /// The CSV itself is never converted. It has to stay Y-up: playerPosition_y in the real
    /// captures is the seated eye height, 1.18 m, and the plugin's reader already does the
    /// swap. Converting on the way out as well would place every hit point on its side.
    /// </summary>
    private static void ApplyZUpToYUpConversion(SimulationSettings settings)
    {
        List<Vector3d> convertedVectors = new List<Vector3d>(settings.StarVectors.Count);
        for (int i = 0; i < settings.StarVectors.Count; i++)
        {
            Vector3d v = settings.StarVectors[i];
            convertedVectors.Add(new Vector3d(v.X, v.Z, v.Y));
        }
        settings.StarVectors = convertedVectors;

        List<Point3d> convertedPoints = new List<Point3d>(settings.StarHitPoints.Count);
        for (int i = 0; i < settings.StarHitPoints.Count; i++)
        {
            Point3d p = settings.StarHitPoints[i];
            convertedPoints.Add(new Point3d(p.X, p.Z, p.Y));
        }
        settings.StarHitPoints = convertedPoints;

        settings.EyeOrigin = new Point3d(settings.EyeOrigin.X, settings.EyeOrigin.Z, settings.EyeOrigin.Y);
        settings.NeutralForward = new Vector3d(settings.NeutralForward.X, settings.NeutralForward.Z, settings.NeutralForward.Y);
    }

    private static Point3d SwapYZ(Point3d p) { return new Point3d(p.X, p.Z, p.Y); }
    private static Vector3d SwapYZ(Vector3d v) { return new Vector3d(v.X, v.Z, v.Y); }

    private string Validate()
    {
        if (settings.StarVectors == null || settings.StarVectors.Count == 0)
            return "StarVectors is empty. Supply the pre-processed star of direction vectors.";
        if (settings.StarHitPoints == null || settings.StarObjectNames == null)
            return "StarHitPoints and StarObjectNames must both be supplied.";
        if (settings.StarHitPoints.Count != settings.StarVectors.Count ||
            settings.StarObjectNames.Count != settings.StarVectors.Count)
            return "StarVectors (" + settings.StarVectors.Count + "), StarHitPoints (" +
                   settings.StarHitPoints.Count + ") and StarObjectNames (" +
                   settings.StarObjectNames.Count + ") must be parallel lists of identical length.";
        int selectableRayCount = StarDirectionIndex.CountSelectable(settings.StarObjectNames);
        if (selectableRayCount < 2)
            return "Only " + selectableRayCount + " of the " + settings.StarVectors.Count +
                   " star rays hit a named object; the rest are '" + SimulationDefaults.InvalidObjectName +
                   "' or blank and are never valid gaze targets. At least 2 are needed for the gaze to move.";
        if (settings.NeutralForward.Length < 1e-9)
            return "NeutralForward is a zero-length vector.";
        if (settings.SessionSeconds <= 1.0)
            return "SessionSeconds must be greater than 1.";
        if (settings.SampleRateHz <= 0.5)
            return "SampleRateHz must be greater than 0.5.";
        if (settings.FixationDurationMaxSec <= settings.FixationDurationMinSec)
            return "FixationDurationMaxSec must exceed FixationDurationMinSec.";
        if (settings.SaccadeAmplitudeMaxDeg <= settings.SaccadeAmplitudeMinDeg)
            return "SaccadeAmplitudeMaxDeg must exceed SaccadeAmplitudeMinDeg.";
        if (string.IsNullOrEmpty(settings.OutputFilePath))
            return "OutputFilePath is empty.";
        return null;
    }

    private static string TryWriteFile(string content, string path)
    {
        // Shared with the environment CSV, and it CREATES the folder rather than demanding
        // it: a run lays out a folder per round, per scene, per kind of file. See OutputFile.
        //
        // No BOM, and this one is not configurable: the real capture files have none, and a
        // BOM would land inside the first header cell and hide "timeStamp" from the reader.
        return OutputFile.Write(content, path, false);
    }

    // ---------------------------------------------------------------------------------
    //  One complete pass. Called twice with the same seed for the determinism check, so
    //  it must reset every piece of state it touches.
    // ---------------------------------------------------------------------------------
    private SimulationRun SimulateOnce()
    {
        SimulationRun run = new SimulationRun();
        random = new DeterministicRandom(settings.Seed);

        sampleIntervalSeconds = 1.0 / settings.SampleRateHz;
        minimumSampleIntervalSeconds = sampleIntervalSeconds * (1.0 - SimulationDefaults.SampleIntervalJitterFraction);
        maximumSampleIntervalSeconds = sampleIntervalSeconds * (1.0 + SimulationDefaults.SampleIntervalJitterFraction);
        intraFixationMaximumStepDegrees = IntraFixationStepSafetyFactor *
            SimulationDefaults.IvtVelocityThresholdDegPerSecond * minimumSampleIntervalSeconds;
        transitionMinimumStepDegrees = TransitionStepSafetyFactor *
            SimulationDefaults.IvtVelocityThresholdDegPerSecond * maximumSampleIntervalSeconds;

        // Session-constant draws.
        eyeGazeColumnScale = random.NextInRange(SimulationDefaults.EyeGazeScaleMin, SimulationDefaults.EyeGazeScaleMax);

        // The pupil runs on a stream of its own, seeded from the session seed but never
        // shared with it. That is what makes the curve independent of SampleRateHz: the
        // main stream is drawn from once per row, so sharing it would have the row rate
        // decide which numbers the pupil got.
        pupilAnatomy = PupilAnthropometry.ForParticipant(
            settings.ParticipantSeed != 0 ? settings.ParticipantSeed : settings.Seed,
            settings.PupilMinimumDomain, settings.PupilMaximumDomain);
        pupils = new PupilSignal(unchecked(settings.Seed ^ 0x5011D1A), pupilAnatomy,
            settings.BaselineSeconds);
        pupilBaselineAssigned = false;
        run.PupilAnatomy = pupilAnatomy;
        run.PupilCyclesPerSecond = pupils.CyclesPerSecond;
        run.PupilBaseline = double.NaN;
        run.DilatingFixationCount = 0;
        run.ConstrictingFixationCount = 0;
        run.FixationWallHoldCount = 0;

        swayX = 0; swayY = 0; swayZ = 0;
        headRollDegrees = 0;
        eyeRollDegrees = SimulationDefaults.EyeRollMeanDegrees;
        blinkActive = false;
        blinkStartSeconds = 0; blinkEndSeconds = 0;
        nextBlinkSeconds = DrawNextBlinkTime(0.0);
        previousSampleTimeSeconds = 0.0;
        hasPreviousHitPoint = false;
        previousHitPoint = new Point3d(0, 0, 0);

        int fixationCount = ResolveFixationCount(run);
        double gapSeconds;
        double[] fixationDurations = BuildFixationSchedule(fixationCount, out gapSeconds);

        if (settings.DistinctObjectsMin > 0 && settings.DistinctObjectsMax >= settings.DistinctObjectsMin)
        {
            // The steering cannot conjure objects that are not in the star, so the target is
            // capped at what the scene actually contains.
            run.DistinctObjectTargetLow = Math.Min(settings.DistinctObjectsMin, distinctObjectsAvailable);
            run.DistinctObjectTargetHigh = Math.Min(settings.DistinctObjectsMax, distinctObjectsAvailable);
            if (settings.DistinctObjectsMin > distinctObjectsAvailable)
                run.Warnings.Add(
                    "DISTINCT OBJECT TARGET CAPPED. DistinctObjectsMin/Max = " + settings.DistinctObjectsMin +
                    ".." + settings.DistinctObjectsMax + ", but StarObjectNames contains only " +
                    distinctObjectsAvailable + " distinct names. Target capped to " + distinctObjectsAvailable + ".");
        }

        run.StarSelectableCount = starIndex.SelectableCount;
        run.StarIgnoredCount = starIndex.IgnoredCount;

        // Start of the walk. The head begins aimed straight at the starting star vector,
        // so the eye starts centred.
        int currentStarIndex = Math.Max(0, Math.Min(settings.StarVectors.Count - 1, settings.StartVectorIndex));
        if (!starIndex.IsSelectable(currentStarIndex))
        {
            // The caller's start direction points at a ray that hit nothing. Every other
            // target goes through NearestIndex and is selectable by construction; this one
            // is handed in from outside, so it is the single place that needs repairing.
            int repairedStartIndex = starIndex.NearestSelectableIndex(currentStarIndex, seatFrame);
            run.Warnings.Add(
                "START VECTOR MOVED. StarVectors[" + currentStarIndex + "] is '" +
                settings.StarObjectNames[currentStarIndex] + "', a ray that hit nothing. Started at [" +
                repairedStartIndex + "] instead, " +
                Fmt(VectorMath.AngleDegrees(starIndex.Direction(currentStarIndex),
                                            starIndex.Direction(repairedStartIndex))) + " deg away.");
            currentStarIndex = repairedStartIndex;
        }
        Vector3d currentGazeDirection = starIndex.Direction(currentStarIndex);
        double startYaw, startPitch;
        seatFrame.ToYawPitchDegrees(currentGazeDirection, out startYaw, out startPitch);
        headYawDegrees = VectorMath.Clamp(startYaw, -settings.NeckYawLimitDeg, settings.NeckYawLimitDeg);
        headPitchDegrees = VectorMath.Clamp(startPitch, -settings.NeckPitchDownLimitDeg, settings.NeckPitchUpLimitDeg);
        headTargetYawDegrees = headYawDegrees;
        headTargetPitchDegrees = headPitchDegrees;

        List<int> fixationStarIndexHistory = new List<int>();

        double timeCursor = 0.0;
        double phaseStart = 0.0;

        for (int fixation = 0; fixation < fixationCount && timeCursor < settings.SessionSeconds; fixation++)
        {
            FixationRecord record = new FixationRecord();
            record.StarIndex = currentStarIndex;
            record.StartSeconds = phaseStart;
            record.PlannedDurationSeconds = fixationDurations[fixation];
            record.SaccadeAmplitudeDegrees = fixation == 0 ? 0.0 : lastSaccadeAmplitudeDegrees;

            fixationStarIndexHistory.Add(currentStarIndex);
            run.VisitedObjectNames.Add(settings.StarObjectNames[currentStarIndex]);

            // ---- fixation samples: the direction is HELD, only micro-jitter moves it ----
            // The last fixation absorbs the rounding tail, but never grows past the caller's
            // own maximum: a schedule that under-fills must not become one enormous stare.
            double fixationEnd = phaseStart + fixationDurations[fixation];
            if (fixation == fixationCount - 1)
                fixationEnd = Math.Min(settings.SessionSeconds, phaseStart + settings.FixationDurationMaxSec);

            Vector3d fixationCentreDirection = starIndex.Direction(currentStarIndex);
            int activeStarIndex = currentStarIndex;
            double jitterRightDegrees = 0.0, jitterUpDegrees = 0.0;
            double centroidX = 0, centroidY = 0, centroidZ = 0;
            int centroidCount = 0;

            while (timeCursor < fixationEnd && timeCursor < settings.SessionSeconds)
            {
                activeStarIndex = StepIntraFixationJitter(fixationCentreDirection, activeStarIndex,
                    ref jitterRightDegrees, ref jitterUpDegrees);

                // The jitter drives the eye columns, but the hit point is pinned to the
                // fixation's own star vector for the whole phase: one exact repeated point
                // per fixation, however coarse the star is.
                GazeSample sample = EmitSample(run, timeCursor, activeStarIndex, fixation, currentStarIndex);
                centroidX += sample.HitPoint.X; centroidY += sample.HitPoint.Y; centroidZ += sample.HitPoint.Z;
                centroidCount++;
                record.SampleCount++;
                timeCursor += DrawSampleInterval();
            }

            record.HeadForwardWorld = seatFrame.FromYawPitchDegrees(headYawDegrees, headPitchDegrees);
            record.Centroid = centroidCount > 0
                ? new Point3d(centroidX / centroidCount, centroidY / centroidCount, centroidZ / centroidCount)
                : settings.StarHitPoints[currentStarIndex];
            run.Fixations.Add(record);

            if (fixation == fixationCount - 1 || timeCursor >= settings.SessionSeconds) break;

            // ---- free scanning: the eye keeps moving and never settles ------------------
            double gapEnd = fixationEnd + gapSeconds;
            double approachSeconds = Math.Min(gapSeconds, MaximumApproachSeconds);
            double approachStart = gapEnd - approachSeconds;
            activeStarIndex = EmitFreeScanning(run, ref timeCursor, activeStarIndex, approachStart);

            if (timeCursor >= settings.SessionSeconds) break;

            // ---- the approach: one saccade onto the next fixation, plus corrections -----
            Vector3d previousGazeDirection = starIndex.Direction(activeStarIndex);
            int nextStarIndex = ChooseNextFixationTarget(run, previousGazeDirection, activeStarIndex,
                fixationStarIndexHistory, fixation, fixationCount);
            Vector3d nextGazeDirection = starIndex.Direction(nextStarIndex);
            lastSaccadeAmplitudeDegrees = VectorMath.AngleDegrees(previousGazeDirection, nextGazeDirection);
            run.RealisedSaccadeAmplitudes.Add(lastSaccadeAmplitudeDegrees);

            AimHeadAtNewTarget(previousGazeDirection, nextGazeDirection);

            // The saccade onto the next fixation, plus its corrections - each written as a
            // stretch where the gaze stands still on one star vector and the hit point
            // repeats verbatim. Every transition carries at least one, so the file always
            // holds more saccades than fixations, the way a real session does.
            int holdStarIndex = -1;
            double holdStartSeconds = 0.0;
            double holdSpanSeconds = 0.0;
            int holdsRemaining = SimulationDefaults.SaccadeHoldsPerTransitionMax > 0
                ? random.NextInteger(SimulationDefaults.SaccadeHoldsPerTransitionMin,
                                     SimulationDefaults.SaccadeHoldsPerTransitionMax)
                : 0;

            while (timeCursor < settings.SessionSeconds)
            {
                // Inclusive: the pause must SPAN the drawn duration, so a 0.05 s draw at
                // 20 Hz keeps the frame at +0.05 s and the group measures 50 ms.
                bool insideHold = holdStarIndex >= 0 &&
                                  timeCursor - holdStartSeconds <= holdSpanSeconds + 1e-9;

                // A dense schedule leaves a gap barely one frame wide, which is not enough
                // room for the saccades to exist at all. So the transition outlives gapEnd
                // until it has written them, and the next fixation starts that much later -
                // the eye really did spend the time getting there. Only what does not fit in
                // the gap is borrowed.
                if (!insideHold && holdsRemaining == 0 && timeCursor >= gapEnd) break;

                if (insideHold)
                {
                    EmitSample(run, timeCursor, holdStarIndex, -1);
                    activeStarIndex = holdStarIndex;
                    run.SaccadeHoldSampleCount++;
                    // The span that matters is the one frames were actually emitted over, not
                    // the drawn one: the drawn span ends mid-interval, and a window stretched
                    // to it would swallow the fixation that starts right after the pause.
                    run.SaccadeHoldEndSeconds[run.SaccadeHoldEndSeconds.Count - 1] = timeCursor;
                    timeCursor += DrawSampleInterval();
                    continue;
                }

                double progress = (timeCursor - approachStart) / Math.Max(1e-6, approachSeconds);
                int transitionStarIndex = PickTransitionStarIndex(previousGazeDirection, nextGazeDirection,
                    progress, activeStarIndex, nextStarIndex);
                EmitSample(run, timeCursor, transitionStarIndex, -1);
                activeStarIndex = transitionStarIndex;

                // The pause is measured from THIS sample, which is already on the held
                // vector, so a 0.05 s draw at 20 Hz yields a two-frame group of exactly
                // 50 ms - the shortest thing a saccade window can see. This sample is also
                // the moving frame that separates one pause from the next, so consecutive
                // saccades never merge into a single stretch.
                if (holdsRemaining > 0)
                {
                    holdsRemaining--;
                    holdStarIndex = transitionStarIndex;
                    holdStartSeconds = timeCursor;

                    // A stretch shorter than one sample interval leaves no second frame, and a
                    // single frame is not a stretch - nothing downstream can tell it from the
                    // moving samples around it. So the draw is floored at one interval plus its
                    // jitter: what the sample rate cannot resolve, the file must not pretend to
                    // carry. At 20 Hz that floor is 58 ms, still inside the 30-80 ms a saccade
                    // takes; a rate low enough to push the floor past 80 ms cannot represent a
                    // saccade at all, and the report's own numbers will show it.
                    double shortestResolvable = sampleIntervalSeconds *
                        (1.0 + SimulationDefaults.SampleIntervalJitterFraction);
                    holdSpanSeconds = Math.Max(shortestResolvable, random.NextInRange(
                        SimulationDefaults.SaccadeHoldMinSeconds, SimulationDefaults.SaccadeHoldMaxSeconds));
                    run.SaccadeHoldCount++;
                    run.SaccadeHoldStartSeconds.Add(holdStartSeconds);
                    run.SaccadeHoldEndSeconds.Add(holdStartSeconds);   // grown as frames are emitted
                }

                timeCursor += DrawSampleInterval();
            }

            currentStarIndex = nextStarIndex;
            phaseStart = Math.Max(gapEnd, timeCursor);   // a pause that overran pushes the next fixation
        }

        // A single-fixation schedule, or rounding, can leave time on the clock. Free
        // scanning fills it rather than letting the last fixation swell to cover it.
        if (run.Samples.Count > 0)
            EmitFreeScanning(run, ref timeCursor, run.Samples[run.Samples.Count - 1].StarIndex,
                settings.SessionSeconds);

        run.DilatingFixationCount = pupils.DilatingFixationCount;
        run.ConstrictingFixationCount = pupils.ConstrictingFixationCount;
        run.FixationWallHoldCount = pupils.FixationWallHoldCount;

        return run;
    }

    /// <summary>
    /// Emits free-scanning samples up to <paramref name="untilSeconds"/>: a random walk
    /// across the star, one hop per sample, with the head following each hop. Nothing here
    /// ever dwells, so none of it registers as a fixation.
    /// </summary>
    private int EmitFreeScanning(SimulationRun run, ref double timeCursor, int activeStarIndex, double untilSeconds)
    {
        while (timeCursor < untilSeconds && timeCursor < settings.SessionSeconds)
        {
            Vector3d beforeStep = starIndex.Direction(activeStarIndex);
            int wanderStarIndex = PickWanderStarIndex(run, activeStarIndex);
            AimHeadAtNewTarget(beforeStep, starIndex.Direction(wanderStarIndex));
            EmitSample(run, timeCursor, wanderStarIndex, -1);
            activeStarIndex = wanderStarIndex;
            run.FreeScanningSamples++;
            timeCursor += DrawSampleInterval();
        }
        return activeStarIndex;
    }

    /// <summary>
    /// One hop of the free-scanning random walk. The amplitude comes from the same saccade
    /// distribution as a real saccade, but floored at the I-VT step: a hop smaller than that
    /// would read as a fixation sample and two of them in a row would invent a fixation the
    /// caller never asked for.
    /// </summary>
    private int PickWanderStarIndex(SimulationRun run, int previousStarIndex)
    {
        Vector3d previousDirection = starIndex.Direction(previousStarIndex);
        int bestIndex = -1;
        double bestStep = -1.0;

        for (int attempt = 0; attempt < 12; attempt++)
        {
            double amplitudeDegrees = random.NextTruncatedLogNormal(
                settings.SaccadeAmplitudeLogMu, settings.SaccadeAmplitudeLogSigma,
                settings.SaccadeAmplitudeMinDeg, settings.SaccadeAmplitudeMaxDeg);
            amplitudeDegrees = Math.Max(amplitudeDegrees, transitionMinimumStepDegrees);

            Vector3d tangent = SampleTangentDirection(previousDirection);
            Vector3d candidateDirection = VectorMath.RotateInTangentPlane(previousDirection, tangent, amplitudeDegrees);
            int candidateIndex = starIndex.NearestIndex(candidateDirection, seatFrame);

            if (candidateIndex == previousStarIndex) continue;
            if (!IsTargetReachable(starIndex.Direction(candidateIndex))) continue;

            double stepDegrees = VectorMath.AngleDegrees(previousDirection, starIndex.Direction(candidateIndex));
            if (stepDegrees > bestStep) { bestStep = stepDegrees; bestIndex = candidateIndex; }
            if (stepDegrees >= transitionMinimumStepDegrees) return Accept(run, candidateIndex, stepDegrees);
        }

        if (bestIndex >= 0) return Accept(run, bestIndex, bestStep);

        // Everything outward was unreachable, which means the walk has arrived at the edge
        // of the neck-and-eye cone. Turn back toward the seat's neutral forward with a
        // shrinking amplitude: the boundary has to reflect the walk, because a hop that
        // cannot be made leaves the gaze parked, and two parked samples in a row would
        // invent a fixation the caller never asked for.
        Vector3d inwardTangent = seatFrame.Forward -
            VectorMath.Dot(seatFrame.Forward, previousDirection) * previousDirection;
        if (inwardTangent.Length < 1e-6) inwardTangent = seatFrame.Right;
        inwardTangent = VectorMath.Unit(inwardTangent);

        for (int attempt = 0; attempt < 12; attempt++)
        {
            double amplitudeDegrees = Math.Max(transitionMinimumStepDegrees,
                settings.SaccadeAmplitudeMaxDeg * Math.Pow(0.65, attempt));
            Vector3d tangent = VectorMath.Unit(inwardTangent + 0.4 * SampleTangentDirection(previousDirection));
            Vector3d candidateDirection = VectorMath.RotateInTangentPlane(previousDirection, tangent, amplitudeDegrees);
            int candidateIndex = starIndex.NearestIndex(candidateDirection, seatFrame);

            if (candidateIndex == previousStarIndex) continue;
            if (!IsTargetReachable(starIndex.Direction(candidateIndex))) continue;
            return Accept(run, candidateIndex,
                VectorMath.AngleDegrees(previousDirection, starIndex.Direction(candidateIndex)));
        }

        run.FreeScanningStalls++;
        return previousStarIndex;
    }

    private int Accept(SimulationRun run, int starIndexValue, double amplitudeDegrees)
    {
        run.RealisedSaccadeAmplitudes.Add(amplitudeDegrees);
        return starIndexValue;
    }

    private double lastSaccadeAmplitudeDegrees;

    // ---------------------------------------------------------------------------------
    //  Stage 2 - the fixation schedule.
    // ---------------------------------------------------------------------------------
    private int ResolveFixationCount(SimulationRun run)
    {
        // A zero or negative count range means "derive the count from the rate".
        int requested;
        string source;
        if (settings.FixationCountMin > 0 && settings.FixationCountMax >= settings.FixationCountMin)
        {
            requested = random.NextInteger(settings.FixationCountMin, settings.FixationCountMax);
            source = "FixationCountMin/Max = " + settings.FixationCountMin + ".." + settings.FixationCountMax;
        }
        else
        {
            requested = Math.Max(1, (int)Math.Round(settings.FixationRatePerSecond * settings.SessionSeconds));
            source = "FixationRatePerSecond = " +
                     settings.FixationRatePerSecond.ToString("0.###", CultureInfo.InvariantCulture) + " /s";
        }

        // A low count is NOT a problem: the time those fixations do not use becomes free
        // scanning. Only a count too HIGH to fit is impossible, and the shortest schedule
        // that many fixations can occupy is what decides it.
        int gapCount = Math.Max(1, requested - 1);
        double shortestSchedule = requested * settings.FixationDurationMinSec + gapCount * MinimumTransitionSeconds;
        if (settings.SessionSeconds >= shortestSchedule) return requested;

        int feasible = Math.Max(1, (int)Math.Floor(
            settings.SessionSeconds / (settings.FixationDurationMinSec + MinimumTransitionSeconds)));
        run.Warnings.Add(
            "FIXATION COUNT REDUCED. " + source + " asks for " + requested + " fixations, but even at the minimum " +
            Fmt(settings.FixationDurationMinSec) + " s each they would need " + Fmt(shortestSchedule) +
            " s, more than the " + Fmt(settings.SessionSeconds) + " s session." +
            "\r\n        Using " + feasible + " instead. Lower FixationDurationMinSec or lengthen the session.");
        return feasible;
    }

    /// <summary>
    /// Draws the fixation durations and works out how much time is left between them.
    ///
    /// The durations are NOT rescaled to fill the session. Stretching a handful of
    /// fixations across a long session is what produced 300-second "fixations" and a gaze
    /// that never moved; the distribution the caller asked for is kept intact and the
    /// leftover time becomes free scanning instead. Durations are only scaled DOWN, and
    /// only when they genuinely do not fit.
    /// </summary>
    private double[] BuildFixationSchedule(int fixationCount, out double gapSeconds)
    {
        double[] durations = new double[fixationCount];
        double sum = 0.0;
        for (int i = 0; i < fixationCount; i++)
        {
            durations[i] = random.NextTruncatedLogNormal(
                settings.FixationDurationLogMu, settings.FixationDurationLogSigma,
                settings.FixationDurationMinSec, settings.FixationDurationMaxSec);
            sum += durations[i];
        }

        int gapCount = Math.Max(1, fixationCount - 1);
        double timeAvailableForFixations = settings.SessionSeconds - gapCount * MinimumTransitionSeconds;
        if (sum > timeAvailableForFixations)
        {
            ScaleDurationsToTarget(durations, timeAvailableForFixations);
            sum = 0.0;
            for (int i = 0; i < fixationCount; i++) sum += durations[i];
        }

        gapSeconds = Math.Max(0.0, (settings.SessionSeconds - sum) / gapCount);
        return durations;
    }

    /// <summary>
    /// Scales the durations so they sum to the target WITHOUT pushing any of them outside
    /// the requested [min, max] range. Each pass redistributes the remainder over the
    /// durations that still have headroom; a single blind proportional factor is what would
    /// let a rescale violate the caller's own limits by two orders of magnitude.
    /// </summary>
    private void ScaleDurationsToTarget(double[] durations, double targetSum)
    {
        double minimum = settings.FixationDurationMinSec;
        double maximum = settings.FixationDurationMaxSec;

        for (int pass = 0; pass < 8; pass++)
        {
            double currentSum = 0.0, adjustableSum = 0.0;
            for (int i = 0; i < durations.Length; i++)
            {
                currentSum += durations[i];
                if (durations[i] > minimum + 1e-9 && durations[i] < maximum - 1e-9)
                    adjustableSum += durations[i];
            }

            if (Math.Abs(currentSum - targetSum) < 1e-6 || adjustableSum < 1e-9) return;

            double factor = (targetSum - (currentSum - adjustableSum)) / adjustableSum;
            if (factor <= 0.0) return;

            for (int i = 0; i < durations.Length; i++)
            {
                if (durations[i] <= minimum + 1e-9 || durations[i] >= maximum - 1e-9) continue;
                durations[i] = VectorMath.Clamp(durations[i] * factor, minimum, maximum);
            }
        }
    }

    private double DrawSampleInterval()
    {
        double jitter = random.NextInRange(
            -SimulationDefaults.SampleIntervalJitterFraction, SimulationDefaults.SampleIntervalJitterFraction);
        return sampleIntervalSeconds * (1.0 + jitter);
    }

    // ---------------------------------------------------------------------------------
    //  Stage 3 - walking the star.
    // ---------------------------------------------------------------------------------
    private int ChooseNextFixationTarget(SimulationRun run, Vector3d currentGazeDirection,
        int currentStarIndex, List<int> history, int fixationOrdinal, int fixationCount)
    {
        // Return bias: real scanpaths come back to where they just were about 30 % of the time.
        if (history.Count >= 2 && random.NextChance(settings.ReturnBiasProbability))
        {
            int returnIndex = history[history.Count - 2];
            if (returnIndex != currentStarIndex && IsTargetReachable(starIndex.Direction(returnIndex)))
                return returnIndex;
        }

        List<int> candidates = new List<int>(CandidatePoolSize);
        for (int attempt = 0; attempt < MaximumTargetAttempts && candidates.Count < CandidatePoolSize; attempt++)
        {
            double amplitudeDegrees = random.NextTruncatedLogNormal(
                settings.SaccadeAmplitudeLogMu, settings.SaccadeAmplitudeLogSigma,
                settings.SaccadeAmplitudeMinDeg, settings.SaccadeAmplitudeMaxDeg);

            Vector3d tangent = SampleTangentDirection(currentGazeDirection);
            Vector3d candidateDirection = VectorMath.RotateInTangentPlane(currentGazeDirection, tangent, amplitudeDegrees);
            int candidateIndex = starIndex.NearestIndex(candidateDirection, seatFrame);

            if (candidateIndex == currentStarIndex) continue;
            if (candidates.Contains(candidateIndex)) continue;
            if (!IsTargetReachable(starIndex.Direction(candidateIndex))) continue;
            candidates.Add(candidateIndex);
        }

        if (candidates.Count == 0)
        {
            // Fall back to progressively smaller amplitudes, which are the easiest to reach
            // inside the neck and eye budget, before giving up and holding position.
            double shrinkingAmplitude = Math.Exp(settings.SaccadeAmplitudeLogMu);
            for (int attempt = 0; attempt < 8; attempt++)
            {
                shrinkingAmplitude *= 0.6;
                Vector3d tangent = SampleTangentDirection(currentGazeDirection);
                Vector3d candidateDirection = VectorMath.RotateInTangentPlane(
                    currentGazeDirection, tangent, Math.Max(settings.SaccadeAmplitudeMinDeg, shrinkingAmplitude));
                int candidateIndex = starIndex.NearestIndex(candidateDirection, seatFrame);
                if (candidateIndex != currentStarIndex && IsTargetReachable(starIndex.Direction(candidateIndex)))
                    return candidateIndex;
            }
            run.UnreachableTargetFallbackCount++;
            return currentStarIndex;
        }

        return ApplyDistinctObjectSteering(run, candidates, fixationOrdinal, fixationCount);
    }

    /// <summary>
    /// Azimuth for the saccade, biased toward horizontal roughly 2:1 by compressing the
    /// vertical component of a uniformly drawn direction.
    /// </summary>
    private Vector3d SampleTangentDirection(Vector3d gazeDirection)
    {
        Vector3d worldUp = new Vector3d(0, 1, 0);
        Vector3d tangentRight = VectorMath.Cross(worldUp, gazeDirection);
        if (tangentRight.Length < 1e-6) tangentRight = seatFrame.Right;
        tangentRight = VectorMath.Unit(tangentRight);
        Vector3d tangentUp = VectorMath.Cross(gazeDirection, tangentRight);

        double azimuth = random.NextInRange(0.0, 2.0 * Math.PI);
        double dx = Math.Cos(azimuth);
        double dy = Math.Sin(azimuth) * VerticalCompressionFactor;
        double length = Math.Sqrt(dx * dx + dy * dy);
        if (length < 1e-9) { dx = 1.0; dy = 0.0; length = 1.0; }
        return VectorMath.Unit((dx / length) * tangentRight + (dy / length) * tangentUp);
    }

    /// <summary>
    /// Distinct-object steering (section 4.4). This only ever picks between candidates
    /// that already satisfy the amplitude distribution and the angular budget - it never
    /// overrides them, so the eye-movement statistics are left undistorted.
    /// </summary>
    private int ApplyDistinctObjectSteering(SimulationRun run, List<int> candidates,
        int fixationOrdinal, int fixationCount)
    {
        if (run.DistinctObjectTargetLow <= 0) return candidates[0];

        double desired = 0.5 * (run.DistinctObjectTargetLow + run.DistinctObjectTargetHigh);
        // Aim to have reached the target by 80 % of the way through the session.
        double progress = Math.Min(1.0, (fixationOrdinal + 1.0) / (0.8 * fixationCount));
        double desiredByNow = desired * progress;

        bool wantNewObject = run.VisitedObjectNames.Count < desiredByNow;
        for (int i = 0; i < candidates.Count; i++)
        {
            bool alreadyVisited = run.VisitedObjectNames.Contains(settings.StarObjectNames[candidates[i]]);
            if (wantNewObject != alreadyVisited) return candidates[i];
        }
        return candidates[0];
    }

    // ---------------------------------------------------------------------------------
    //  Stage 4 - head / eye decomposition.
    // ---------------------------------------------------------------------------------
    private void AimHeadAtNewTarget(Vector3d fromDirection, Vector3d toDirection)
    {
        double fromYaw, fromPitch, toYaw, toPitch;
        seatFrame.ToYawPitchDegrees(fromDirection, out fromYaw, out fromPitch);
        seatFrame.ToYawPitchDegrees(toDirection, out toYaw, out toPitch);

        double shiftDegrees = VectorMath.AngleDegrees(fromDirection, toDirection);

        // The head contributes to every shift, however small - there is no threshold.
        double headFraction = SimulationDefaults.HeadFractionBase +
                              SimulationDefaults.HeadFractionPerDegree * shiftDegrees;
        headFraction = VectorMath.Clamp(headFraction, SimulationDefaults.HeadFractionBase,
                                        SimulationDefaults.HeadFractionCeiling);
        headFraction += random.NextGaussian(0.0, SimulationDefaults.HeadFractionNoiseSd);
        headFraction = VectorMath.Clamp(headFraction, 0.15, 0.95);

        headTargetYawDegrees += headFraction * NormaliseYawDelta(toYaw - fromYaw);
        headTargetPitchDegrees += headFraction * (toPitch - fromPitch);
        ClampHeadTargetToNeck();

        // Eye re-centering: if the residual eye-in-head angle would break the eye budget,
        // the head absorbs the excess. This, not a clamp, is what keeps the eye inside its
        // measured range.
        EnforceEyeBudget(toDirection, ref headTargetYawDegrees, ref headTargetPitchDegrees, headRollDegrees);
    }

    /// <summary>
    /// Head roll as a slow Ornstein-Uhlenbeck wander about zero. It is deliberately NOT
    /// driven from the saccade code: during free scanning that fires every sample, and a
    /// roll target redrawn that often becomes fast noise rather than slow drift - which in
    /// turn rotates the eye-in-head decomposition enough to break the eye budget.
    /// </summary>
    private void UpdateHeadRoll(double deltaSeconds)
    {
        double decay = Math.Exp(-deltaSeconds / SimulationDefaults.HeadRollTimeConstantSeconds);
        headRollDegrees = headRollDegrees * decay +
            random.NextGaussian(0.0, SimulationDefaults.HeadRollSdDegrees * Math.Sqrt(1.0 - decay * decay));
    }

    private static double NormaliseYawDelta(double deltaDegrees)
    {
        while (deltaDegrees > 180.0) deltaDegrees -= 360.0;
        while (deltaDegrees < -180.0) deltaDegrees += 360.0;
        return deltaDegrees;
    }

    private void ClampHeadTargetToNeck()
    {
        headTargetYawDegrees = VectorMath.Clamp(headTargetYawDegrees,
            -settings.NeckYawLimitDeg, settings.NeckYawLimitDeg);
        headTargetPitchDegrees = VectorMath.Clamp(headTargetPitchDegrees,
            -settings.NeckPitchDownLimitDeg, settings.NeckPitchUpLimitDeg);
    }

    private QuaternionXyzw BuildHeadRotation(double yawDegrees, double pitchDegrees, double rollDegrees)
    {
        Vector3d forward = seatFrame.FromYawPitchDegrees(yawDegrees, pitchDegrees);
        QuaternionXyzw look = QuaternionXyzw.LookRotation(forward, seatFrame.Up);
        // Roll about the head's own forward axis, so rotating (0,0,1) still returns
        // exactly the head forward direction (the round-trip check of stage 6).
        QuaternionXyzw roll = QuaternionXyzw.FromAxisAngle(forward, rollDegrees * VectorMath.RadiansPerDegree);
        return QuaternionXyzw.Multiply(roll, look).Normalized();
    }

    private void EyeInHeadAngles(Vector3d gazeDirection, double yawDegrees, double pitchDegrees,
        double rollDegrees, out double eyeYawDegrees, out double eyePitchDegrees)
    {
        QuaternionXyzw head = BuildHeadRotation(yawDegrees, pitchDegrees, rollDegrees);
        Vector3d headForward = head.Rotate(new Vector3d(0, 0, 1));
        Vector3d headRight = head.Rotate(new Vector3d(1, 0, 0));
        Vector3d headUp = head.Rotate(new Vector3d(0, 1, 0));

        double x = VectorMath.Dot(gazeDirection, headRight);
        double y = VectorMath.Dot(gazeDirection, headUp);
        double z = VectorMath.Dot(gazeDirection, headForward);
        eyeYawDegrees = Math.Atan2(x, z) * VectorMath.DegreesPerRadian;
        eyePitchDegrees = Math.Asin(VectorMath.Clamp(y, -1.0, 1.0)) * VectorMath.DegreesPerRadian;
    }

    /// <summary>
    /// Pushes the head far enough that the eye-in-head angles fit inside the eye budget,
    /// re-clamping to the neck limits each iteration. Converges in two or three passes.
    /// </summary>
    private void EnforceEyeBudget(Vector3d gazeDirection, ref double yawDegrees, ref double pitchDegrees,
        double rollDegrees)
    {
        for (int iteration = 0; iteration < EyeBudgetIterations; iteration++)
        {
            double eyeYaw, eyePitch;
            EyeInHeadAngles(gazeDirection, yawDegrees, pitchDegrees, rollDegrees, out eyeYaw, out eyePitch);

            bool violated = false;
            if (Math.Abs(eyeYaw) > settings.EyeYawLimitDeg * EyeBudgetSettleFactor)
            {
                yawDegrees += Math.Sign(eyeYaw) * (Math.Abs(eyeYaw) - settings.EyeYawLimitDeg * EyeBudgetSettleFactor);
                violated = true;
            }
            if (Math.Abs(eyePitch) > settings.EyePitchLimitDeg * EyeBudgetSettleFactor)
            {
                pitchDegrees += Math.Sign(eyePitch) * (Math.Abs(eyePitch) - settings.EyePitchLimitDeg * EyeBudgetSettleFactor);
                violated = true;
            }

            yawDegrees = VectorMath.Clamp(yawDegrees, -settings.NeckYawLimitDeg, settings.NeckYawLimitDeg);
            pitchDegrees = VectorMath.Clamp(pitchDegrees, -settings.NeckPitchDownLimitDeg, settings.NeckPitchUpLimitDeg);

            if (!violated) return;
        }

        // The iteration corrects yaw and pitch independently, which is only approximate:
        // on a sphere the two are coupled, and the coupling is strong once the eye limits
        // are wide. When it fails to settle, aim the head straight at the gaze instead.
        // That leaves the eye centred whenever the neck can reach, and when it cannot, the
        // head sits at the neck limit and the eye covers the smallest possible remainder -
        // so if any feasible head orientation exists, this finds it.
        double gazeYaw, gazePitch;
        seatFrame.ToYawPitchDegrees(gazeDirection, out gazeYaw, out gazePitch);
        yawDegrees = VectorMath.Clamp(gazeYaw, -settings.NeckYawLimitDeg, settings.NeckYawLimitDeg);
        pitchDegrees = VectorMath.Clamp(gazePitch, -settings.NeckPitchDownLimitDeg, settings.NeckPitchUpLimitDeg);
    }

    /// <summary>
    /// A target is reachable when some head orientation inside the neck limits leaves the
    /// eye inside its own limits. Tested by running the real re-centering routine.
    /// </summary>
    private bool IsTargetReachable(Vector3d gazeDirection)
    {
        double yaw = headTargetYawDegrees;
        double pitch = headTargetPitchDegrees;
        EnforceEyeBudget(gazeDirection, ref yaw, ref pitch, headRollDegrees);

        double eyeYaw, eyePitch;
        EyeInHeadAngles(gazeDirection, yaw, pitch, headRollDegrees, out eyeYaw, out eyePitch);
        return Math.Abs(eyeYaw) <= settings.EyeYawLimitDeg * ReachabilityMarginFactor &&
               Math.Abs(eyePitch) <= settings.EyePitchLimitDeg * ReachabilityMarginFactor;
    }

    // ---------------------------------------------------------------------------------
    //  Stage 5 - sample-level movement.
    // ---------------------------------------------------------------------------------

    /// <summary>
    /// Micro-jitter inside a fixation: a slow random walk in the tangent plane, bounded by
    /// the measured intra-fixation dispersion radius. Any hop that would move the gaze
    /// further than the I-VT threshold allows in one sample is refused, so a fixation
    /// never fragments into two.
    /// </summary>
    private int StepIntraFixationJitter(Vector3d fixationCentreDirection, int activeStarIndex,
        ref double jitterRightDegrees, ref double jitterUpDegrees)
    {
        double stepSd = settings.IntraFixationDispersionDeg * 0.22;
        double proposedRight = jitterRightDegrees + random.NextGaussian(0.0, stepSd);
        double proposedUp = jitterUpDegrees + random.NextGaussian(0.0, stepSd);

        double radius = Math.Sqrt(proposedRight * proposedRight + proposedUp * proposedUp);
        if (radius > settings.IntraFixationDispersionDeg)
        {
            double shrink = settings.IntraFixationDispersionDeg / radius;
            proposedRight *= shrink;
            proposedUp *= shrink;
            radius = settings.IntraFixationDispersionDeg;
        }
        if (radius < 1e-6) return activeStarIndex;

        Vector3d worldUp = new Vector3d(0, 1, 0);
        Vector3d tangentRight = VectorMath.Cross(worldUp, fixationCentreDirection);
        if (tangentRight.Length < 1e-6) tangentRight = seatFrame.Right;
        tangentRight = VectorMath.Unit(tangentRight);
        Vector3d tangentUp = VectorMath.Cross(fixationCentreDirection, tangentRight);

        Vector3d offsetDirection = VectorMath.Unit(
            (proposedRight / radius) * tangentRight + (proposedUp / radius) * tangentUp);
        Vector3d jittered = VectorMath.RotateInTangentPlane(fixationCentreDirection, offsetDirection, radius);

        int candidateIndex = starIndex.NearestIndex(jittered, seatFrame);
        double stepDegrees = VectorMath.AngleDegrees(starIndex.Direction(activeStarIndex),
                                                     starIndex.Direction(candidateIndex));
        if (stepDegrees > intraFixationMaximumStepDegrees) return activeStarIndex;

        // Even a jitter of one degree can cross the edge of the reachable cone when the
        // fixation already sits against the neck limit, so it is validated like any other
        // target rather than trusted for being small.
        if (!IsTargetReachable(starIndex.Direction(candidateIndex))) return activeStarIndex;

        jitterRightDegrees = proposedRight;
        jitterUpDegrees = proposedUp;
        return candidateIndex;
    }

    /// <summary>
    /// A sample landing between two fixations. It travels along the great circle toward
    /// the new target with a perpendicular scatter, and the scatter is re-drawn until the
    /// step from the previous sample is unambiguously above the I-VT threshold - these are
    /// the short corrective movements that make the real saccade rate exceed the fixation
    /// rate, and they must not be mistaken for part of a fixation.
    /// </summary>
    private int PickTransitionStarIndex(Vector3d fromDirection, Vector3d toDirection,
        double progress, int previousStarIndex, int fallbackStarIndex)
    {
        progress = VectorMath.Clamp(progress, 0.0, 1.0);
        Vector3d baseDirection = SlerpDirection(fromDirection, toDirection, progress);
        Vector3d previousDirection = starIndex.Direction(previousStarIndex);

        int bestIndex = -1;
        double bestStep = -1.0;
        for (int attempt = 0; attempt < 8; attempt++)
        {
            Vector3d tangent = SampleTangentDirection(baseDirection);
            double scatterDegrees = transitionMinimumStepDegrees * random.NextInRange(0.6, 1.4);
            Vector3d candidateDirection = VectorMath.RotateInTangentPlane(baseDirection, tangent, scatterDegrees);
            int candidateIndex = starIndex.NearestIndex(candidateDirection, seatFrame);

            // The scatter is what keeps these samples above the I-VT threshold, but near the
            // neck limit it can throw the gaze outside the reachable cone, so candidates are
            // validated here too.
            if (!IsTargetReachable(starIndex.Direction(candidateIndex))) continue;

            double stepDegrees = VectorMath.AngleDegrees(previousDirection, starIndex.Direction(candidateIndex));
            if (stepDegrees > bestStep) { bestStep = stepDegrees; bestIndex = candidateIndex; }
            if (stepDegrees >= transitionMinimumStepDegrees) return candidateIndex;
        }
        // Nothing scattered was reachable: go straight to the fixation target, which was
        // validated when it was chosen.
        return bestIndex >= 0 ? bestIndex : fallbackStarIndex;
    }

    private static Vector3d SlerpDirection(Vector3d from, Vector3d to, double t)
    {
        double dot = VectorMath.Clamp(VectorMath.Dot(from, to), -1.0, 1.0);
        double theta = Math.Acos(dot);
        if (theta < 1e-6) return from;
        double sinTheta = Math.Sin(theta);
        double a = Math.Sin((1.0 - t) * theta) / sinTheta;
        double b = Math.Sin(t * theta) / sinTheta;
        return VectorMath.Unit(a * from + b * to);
    }

    // ---------------------------------------------------------------------------------
    /// <summary>
    /// The pupil baseline of a session, computed the way the READER computes it, because the
    /// whole point is that the two agree: PluginCAISM's CalcularBaselinePupilar takes the
    /// MEDIAN of LinhaDeSessao.DiametroPupilaMedio - the mean of whichever of the two eyes is
    /// positive - over the rows whose elapsed time falls in the window, and drops the rows
    /// where neither eye is. Blink rows write -1 in both columns, so they fall out here for
    /// the same reason they fall out there.
    /// </summary>
    private static double MedianRowMeanPupil(List<GazeSample> samples, double windowSeconds)
    {
        List<double> diametres = new List<double>();
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].TimeSeconds > windowSeconds) break;

            double left = samples[i].LeftPupilDiametre, right = samples[i].RightPupilDiametre;
            bool leftValid = left > 0.0, rightValid = right > 0.0;
            if (leftValid && rightValid) diametres.Add(0.5 * (left + right));
            else if (leftValid) diametres.Add(left);
            else if (rightValid) diametres.Add(right);
        }

        if (diametres.Count == 0) return double.NaN;
        diametres.Sort();
        int middle = diametres.Count / 2;
        return diametres.Count % 2 == 1
            ? diametres[middle]
            : 0.5 * (diametres[middle - 1] + diametres[middle]);
    }

    // ---------------------------------------------------------------------------------
    //  Stage 6 - assemble one sample.
    // ---------------------------------------------------------------------------------
    /// <param name="hitPointStarIndex">
    /// Star vector the nomeDoObjeto and the hit point columns are read from. Pass -1 to use
    /// <paramref name="activeStarIndex"/>. Passing a fixed index for a whole phase is what
    /// pins the hit point: the eye keeps micro-moving in the EyeGaze and EyeOrientation
    /// columns, while the raycast result stays byte-identical from frame to frame, the way a
    /// logger reports it when the gaze never leaves one patch of one surface.
    /// </param>
    private GazeSample EmitSample(SimulationRun run, double timeSeconds, int activeStarIndex, int fixationIndex,
        int hitPointStarIndex = -1)
    {
        int hitStarIndex = hitPointStarIndex >= 0 ? hitPointStarIndex : activeStarIndex;

        // The baseline, the first time this session steps past the window. It is measured off
        // the rows already written, with the reader's own rule, so the number the fixations
        // are driven away from is exactly the number PluginCAISM will subtract - not an
        // internal value that happens to be close to it.
        if (!pupilBaselineAssigned && settings.BaselineSeconds > 0.0 && timeSeconds > settings.BaselineSeconds)
        {
            pupilBaselineAssigned = true;
            run.PupilBaseline = MedianRowMeanPupil(run.Samples, settings.BaselineSeconds);
            if (!double.IsNaN(run.PupilBaseline)) pupils.SetBaseline(run.PupilBaseline);
        }

        double deltaSeconds = run.Samples.Count == 0 ? sampleIntervalSeconds : timeSeconds - previousSampleTimeSeconds;
        if (deltaSeconds <= 0.0) deltaSeconds = sampleIntervalSeconds;
        previousSampleTimeSeconds = timeSeconds;

        Vector3d gazeDirection = starIndex.Direction(activeStarIndex);

        // The head never jumps: exponential approach toward its target.
        double approach = 1.0 - Math.Exp(-deltaSeconds / Math.Max(1e-3, settings.HeadTimeConstantSec));
        headYawDegrees += NormaliseYawDelta(headTargetYawDegrees - headYawDegrees) * approach;
        headPitchDegrees += (headTargetPitchDegrees - headPitchDegrees) * approach;
        UpdateHeadRoll(deltaSeconds);
        ClampHeadToNeck();

        // While the head lags behind its target the eye carries the difference; the eye
        // budget still has to hold on every emitted sample, so the head is dragged the
        // last few degrees when necessary.
        EnforceEyeBudget(gazeDirection, ref headYawDegrees, ref headPitchDegrees, headRollDegrees);

        QuaternionXyzw headRotation = BuildHeadRotation(headYawDegrees, headPitchDegrees, headRollDegrees);
        Vector3d headForward = headRotation.Rotate(new Vector3d(0, 0, 1));
        Vector3d headRight = headRotation.Rotate(new Vector3d(1, 0, 0));
        Vector3d headUp = headRotation.Rotate(new Vector3d(0, 1, 0));

        double eyeYawDegrees, eyePitchDegrees;
        EyeInHeadAngles(gazeDirection, headYawDegrees, headPitchDegrees, headRollDegrees,
            out eyeYawDegrees, out eyePitchDegrees);
        if (Math.Abs(eyeYawDegrees) > settings.EyeYawLimitDeg + 1e-6 ||
            Math.Abs(eyePitchDegrees) > settings.EyePitchLimitDeg + 1e-6)
            run.EyeBudgetViolationCount++;

        // Eye direction expressed in the head's own frame - the frame both the EyeGaze and
        // the EyeOrientation columns live in.
        Vector3d eyeInHead = VectorMath.Unit(new Vector3d(
            VectorMath.Dot(gazeDirection, headRight),
            VectorMath.Dot(gazeDirection, headUp),
            VectorMath.Dot(gazeDirection, headForward)));

        UpdateSeatSway(deltaSeconds);
        Point3d playerPosition = new Point3d(
            settings.EyeOrigin.X + swayX, settings.EyeOrigin.Y + swayY, settings.EyeOrigin.Z + swayZ);

        bool isBlink = UpdateBlinkState(run, timeSeconds);
        double leftOpened, rightOpened;
        ComputeOpenness(isBlink, timeSeconds, out leftOpened, out rightOpened);
        pupils.AdvanceTo(timeSeconds, fixationIndex);

        // Invalid frames. Blinks always produce one - the eye is shut, there is no gaze -
        // but they also occur on their own, so the independent probability is whatever the
        // blinks have not already used up.
        //
        // How many frames a blink covers depends on its duration AND on the sample rate, so
        // the blink share is measured as the session runs rather than predicted from a
        // formula. A prior worth a couple of hundred samples keeps the first seconds stable.
        if (isBlink) run.BlinkFrameCount++;
        const double PriorSamples = 200.0;
        double priorBlinkFrames = PriorSamples * settings.BlinkRatePerSecond *
            (0.5 * (SimulationDefaults.BlinkDurationMinSeconds + SimulationDefaults.BlinkDurationMaxSeconds)
             + 0.5 * sampleIntervalSeconds);
        double blinkFrameFraction = (run.BlinkFrameCount + priorBlinkFrames) / (run.Samples.Count + PriorSamples);
        double independentInvalidProbability = blinkFrameFraction >= 1.0
            ? 0.0
            : VectorMath.Clamp((settings.InvalidFrameRate - blinkFrameFraction) / (1.0 - blinkFrameFraction), 0.0, 1.0);
        bool isInvalid = isBlink || random.NextChance(independentInvalidProbability);

        GazeSample sample = new GazeSample();
        sample.TimeSeconds = timeSeconds;
        sample.StarIndex = activeStarIndex;
        sample.FixationIndex = fixationIndex;
        sample.IsBlinkFrame = isBlink;
        sample.IsInvalidFrame = isInvalid;

        if (isInvalid)
        {
            // The Unity logger writes this name and repeats the previous hit point verbatim
            // when the raycast fails; that stale value is what makes these frames spottable.
            sample.ObjectName = SimulationDefaults.InvalidObjectName;
            sample.HitPoint = hasPreviousHitPoint ? previousHitPoint : settings.StarHitPoints[hitStarIndex];
            run.InvalidFrameCount++;
        }
        else
        {
            sample.ObjectName = settings.StarObjectNames[hitStarIndex];
            sample.HitPoint = settings.StarHitPoints[hitStarIndex];
            previousHitPoint = sample.HitPoint;
            hasPreviousHitPoint = true;
        }
        if (!hasPreviousHitPoint) { previousHitPoint = sample.HitPoint; hasPreviousHitPoint = true; }

        sample.PlayerPosition = playerPosition;
        sample.PlayerRotation = headRotation;

        // The EyeGaze columns are a partially broken column in the source data: they are
        // not unit vectors, their magnitude is session-dependent and no permutation of
        // axes, frames or quaternions reconciles them with the recorded hit points. They
        // are reproduced here as a plausible, self-consistent, deliberately non-unit
        // version of the eye-in-head direction. Do not read meaning into them.
        // The 4-6 deg divergence between the eyes lives in THIS column, not in the
        // orientation quaternions: measured median 4.69 and 4.06 deg.
        Vector3d leftGazeColumnDirection = PerturbDirection(eyeInHead, SimulationDefaults.EyeGazeJitterDegrees);
        Vector3d rightGazeColumnDirection = PerturbDirection(leftGazeColumnDirection,
            SimulationDefaults.InterEyeGazeDivergenceDegrees);
        sample.LeftEyeGazeColumn = ScaleWithNoise(leftGazeColumnDirection, eyeGazeColumnScale);
        sample.RightEyeGazeColumn = ScaleWithNoise(rightGazeColumnDirection, eyeGazeColumnScale);

        // The two EyeOrientation quaternions are effectively one value in the real files,
        // so both eyes share the direction and differ only by a hundredth of a degree.
        UpdateEyeRoll(deltaSeconds);
        Vector3d rightOrientationDirection = PerturbDirection(eyeInHead,
            SimulationDefaults.InterEyeOrientationDivergenceDegrees);
        sample.LeftEyeOrientation = BuildEyeOrientation(eyeInHead, eyeRollDegrees);
        sample.RightEyeOrientation = BuildEyeOrientation(rightOrientationDirection,
            eyeRollDegrees + random.NextGaussian(0.0, 0.05));
        sample.LeftEyeInHeadDirection = eyeInHead;
        sample.RightEyeInHeadDirection = rightOrientationDirection;

        sample.LeftPupilDiametre = isBlink ? SimulationDefaults.PupilInvalidMarker : pupils.Left;
        sample.RightPupilDiametre = isBlink ? SimulationDefaults.PupilInvalidMarker : pupils.Right;
        sample.LeftEyeOpened = leftOpened;
        sample.RightEyeOpened = rightOpened;

        sample.GazeDirectionWorld = gazeDirection;
        sample.HeadForwardWorld = headForward;
        sample.HeadYawDegrees = headYawDegrees;
        sample.HeadPitchDegrees = headPitchDegrees;
        sample.EyeInHeadYawDegrees = eyeYawDegrees;
        sample.EyeInHeadPitchDegrees = eyePitchDegrees;

        run.Samples.Add(sample);
        return sample;
    }

    private void ClampHeadToNeck()
    {
        headYawDegrees = VectorMath.Clamp(headYawDegrees, -settings.NeckYawLimitDeg, settings.NeckYawLimitDeg);
        headPitchDegrees = VectorMath.Clamp(headPitchDegrees, -settings.NeckPitchDownLimitDeg, settings.NeckPitchUpLimitDeg);
    }

    /// <summary>
    /// Rotates a direction by a random angle in a random azimuth, scaled so that the
    /// MEDIAN angular displacement equals <paramref name="medianDegrees"/> - the half
    /// normal has median 0.6745 sigma, hence the factor.
    /// </summary>
    private Vector3d PerturbDirection(Vector3d direction, double medianDegrees)
    {
        Vector3d tangent = SampleTangentDirection(direction);
        double angle = Math.Abs(random.NextGaussian(0.0, medianDegrees / 0.6745));
        return VectorMath.RotateInTangentPlane(direction, tangent, angle);
    }

    /// <summary>Smooth Ornstein-Uhlenbeck wander of the eye's roll about its own axis.</summary>
    private void UpdateEyeRoll(double deltaSeconds)
    {
        double decay = Math.Exp(-deltaSeconds / SimulationDefaults.EyeRollTimeConstantSeconds);
        eyeRollDegrees = SimulationDefaults.EyeRollMeanDegrees +
                         (eyeRollDegrees - SimulationDefaults.EyeRollMeanDegrees) * decay +
                         random.NextGaussian(0.0, SimulationDefaults.EyeRollSdDegrees * Math.Sqrt(1.0 - decay * decay));
    }

    /// <summary>
    /// The eye quaternion. Verified against both real capture files: the usable forward
    /// axis is POSITIVE Z in the head's local frame - q * (0,0,1) sits 8 deg (session 1)
    /// and 16 deg (session 2) from the head-local gaze direction, while q * (0,0,-1) sits
    /// 172 deg and 164 deg away from it. Section 3.10 of the brief states the opposite;
    /// the measurement is what is implemented here.
    /// </summary>
    private QuaternionXyzw BuildEyeOrientation(Vector3d eyeInHeadDirection, double rollDegrees)
    {
        QuaternionXyzw aim = QuaternionXyzw.FromToRotation(new Vector3d(0, 0, 1), eyeInHeadDirection);
        // Roll about the eye's own forward axis leaves that axis fixed, so the round-trip
        // q * (0,0,1) == eyeInHeadDirection still holds exactly.
        QuaternionXyzw roll = QuaternionXyzw.FromAxisAngle(eyeInHeadDirection, rollDegrees * VectorMath.RadiansPerDegree);
        QuaternionXyzw combined = QuaternionXyzw.Multiply(roll, aim).Normalized();

        // The logger writes the negative-W representation of the rotation (median W was
        // -0.797 and -0.978 in the real files). q and -q are the same rotation, so this is
        // cosmetic - but a file full of positive W would look wrong to anyone eyeballing it.
        if (combined.W > 0.0)
            combined = new QuaternionXyzw(-combined.X, -combined.Y, -combined.Z, -combined.W);
        return combined;
    }

    private Vector3d ScaleWithNoise(Vector3d direction, double scale)
    {
        return new Vector3d(
            direction.X * scale + random.NextGaussian(0.0, SimulationDefaults.EyeGazeNoiseSd),
            direction.Y * scale + random.NextGaussian(0.0, SimulationDefaults.EyeGazeNoiseSd),
            direction.Z * scale + random.NextGaussian(0.0, SimulationDefaults.EyeGazeNoiseSd));
    }

    /// <summary>Slow postural sway as an Ornstein-Uhlenbeck walk, not white noise.</summary>
    private void UpdateSeatSway(double deltaSeconds)
    {
        double decay = Math.Exp(-deltaSeconds / SimulationDefaults.PlayerPositionDriftTimeConstantSeconds);
        double innovation = SimulationDefaults.PlayerPositionDriftSdMetres * Math.Sqrt(1.0 - decay * decay);
        swayX = swayX * decay + random.NextGaussian(0.0, innovation);
        swayY = swayY * decay + random.NextGaussian(0.0, innovation);
        swayZ = swayZ * decay + random.NextGaussian(0.0, innovation);
    }

    private double DrawNextBlinkTime(double fromSeconds)
    {
        if (settings.BlinkRatePerSecond <= 0.0) return double.MaxValue;
        double uniform = Math.Max(1e-9, random.NextDouble());
        return fromSeconds - Math.Log(uniform) / settings.BlinkRatePerSecond;   // Poisson process
    }

    private bool UpdateBlinkState(SimulationRun run, double timeSeconds)
    {
        if (!blinkActive && timeSeconds >= nextBlinkSeconds)
        {
            blinkActive = true;
            blinkStartSeconds = timeSeconds;
            blinkEndSeconds = timeSeconds + random.NextInRange(
                SimulationDefaults.BlinkDurationMinSeconds, SimulationDefaults.BlinkDurationMaxSeconds);
            run.BlinkEventCount++;
        }
        if (blinkActive && timeSeconds > blinkEndSeconds)
        {
            blinkActive = false;
            nextBlinkSeconds = DrawNextBlinkTime(timeSeconds);
        }
        return blinkActive;
    }

    private void ComputeOpenness(bool isBlink, double timeSeconds, out double leftOpened, out double rightOpened)
    {
        if (isBlink)
        {
            // Ramp down to zero and back. Every blink sample stays below 0.5 so that a
            // blink event is unambiguous to any consumer counting openness < 0.5.
            double span = Math.Max(1e-6, blinkEndSeconds - blinkStartSeconds);
            double phase = VectorMath.Clamp((timeSeconds - blinkStartSeconds) / span, 0.0, 1.0);
            double value = SimulationDefaults.BlinkPeakOpenness * Math.Abs(2.0 * phase - 1.0);
            leftOpened = value;
            rightOpened = VectorMath.Clamp(
                value + random.NextGaussian(0.0, SimulationDefaults.BlinkRightEyeDifferenceSd), 0.0, 0.49);
            return;
        }

        double draw = random.NextDouble();
        if (draw < SimulationDefaults.OpennessExactlyOneFraction)
        {
            leftOpened = 1.0;
        }
        else if (draw < SimulationDefaults.OpennessExactlyOneFraction + SimulationDefaults.OpennessNearOneFraction)
        {
            leftOpened = random.NextInRange(0.90, 0.9999);
        }
        else
        {
            leftOpened = random.NextInRange(SimulationDefaults.OpennessPartialMin, SimulationDefaults.OpennessPartialMax);
        }

        // The eyes are correlated but not locked: identical on 55-90 % of real samples.
        rightOpened = random.NextChance(0.70)
            ? leftOpened
            : VectorMath.Clamp(leftOpened + random.NextGaussian(0.0, 0.03), 0.5, 1.0);
    }

}

// -------------------------------------------------------------------------------------
//  Stage 7 - the writer.
//
//  The format is plain RFC 4180, matching the real captures field for field:
//    * the header line is unquoted, CRLF terminated;
//    * on a DATA line only three fields carry quotes - timeStamp, nomeDoObjeto and
//      htcDeviceName - and they are quoted on every row, even when the value holds no
//      comma. Every other field is written bare. The row is NOT wrapped in an extra
//      pair of quotes: doing that collapses all 38 columns into one field, and any
//      reader splitting on unquoted commas then sees the whole line as the timestamp;
//    * rows are written NEWEST FIRST, so the session is generated forward in time and
//      the list is reversed here;
//    * UTF-8 without BOM, because the object names carry accents ("Chao", "Parede").
// -------------------------------------------------------------------------------------
public static class GazeCsvWriter
{
    public const string HeaderLine =
        "timeStamp,identificacaoManual,nomeDaSessao,selectedScene,selectedSeat,nomeDoObjeto," +
        "left_EyeGaze_X,right_EyeGaze_X,left_EyeGaze_Y,right_EyeGaze_Y,left_EyeGaze_Z,right_EyeGaze_Z," +
        "left_EyeOrientation_X,right_EyeOrientation_X,left_EyeOrientation_Y,right_EyeOrientation_Y," +
        "left_EyeOrientation_Z,right_EyeOrientation_Z,left_EyeOrientation_W,right_EyeOrientation_W," +
        "left_PupilDiametre,right_PupilDiametre,left_EyeOpened,right_EyeOpened," +
        "playerPosition_x,playerPosition_y,playerPosition_z," +
        "playerRotation_x,playerRotation_y,playerRotation_z,playerRotation_w," +
        "leftHitPoint_X,leftHitPoint_Y,leftHitPoint_Z,rightHitPoint_X,rightHitPoint_Y,rightHitPoint_Z," +
        "htcDeviceName";

    public const string LineTerminator = "\r\n";
    public const int ColumnCount = 38;

    // The logger rounds to 4 decimals and drops trailing zeros, so a whole number is written
    // as "1", not "1.0000". That is not cosmetic: left_EyeOpened / right_EyeOpened are read
    // with int.TryParse downstream, which rejects "1.0000" and yields 0 - an eye reported as
    // shut on every single row, so no frame is ever eligible for event detection.
    private const string FloatFormat = "0.####";

    public static string BuildFileContent(List<GazeSample> samples, SimulationSettings settings, DateTime sessionStart)
    {
        StringBuilder builder = new StringBuilder(samples.Count * 420 + 512);
        builder.Append(HeaderLine).Append(LineTerminator);
        for (int i = samples.Count - 1; i >= 0; i--)          // newest first
            builder.Append(BuildDataLine(samples[i], settings, sessionStart)).Append(LineTerminator);
        return builder.ToString();
    }

    public static string BuildDataLine(GazeSample sample, SimulationSettings settings, DateTime sessionStart)
    {
        string[] fields = new string[ColumnCount];
        int c = 0;

        fields[c++] = Quote(sessionStart.AddSeconds(sample.TimeSeconds)
            .ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        fields[c++] = settings.ParticipantId;
        fields[c++] = settings.SessionName;
        fields[c++] = settings.SceneName;
        fields[c++] = settings.SeatName;
        fields[c++] = Quote(sample.ObjectName);

        // Interleaved left/right, exactly as in the source files.
        fields[c++] = F(sample.LeftEyeGazeColumn.X);
        fields[c++] = F(sample.RightEyeGazeColumn.X);
        fields[c++] = F(sample.LeftEyeGazeColumn.Y);
        fields[c++] = F(sample.RightEyeGazeColumn.Y);
        fields[c++] = F(sample.LeftEyeGazeColumn.Z);
        fields[c++] = F(sample.RightEyeGazeColumn.Z);

        fields[c++] = F(sample.LeftEyeOrientation.X);
        fields[c++] = F(sample.RightEyeOrientation.X);
        fields[c++] = F(sample.LeftEyeOrientation.Y);
        fields[c++] = F(sample.RightEyeOrientation.Y);
        fields[c++] = F(sample.LeftEyeOrientation.Z);
        fields[c++] = F(sample.RightEyeOrientation.Z);
        fields[c++] = F(sample.LeftEyeOrientation.W);
        fields[c++] = F(sample.RightEyeOrientation.W);

        fields[c++] = F(sample.LeftPupilDiametre);            // -1.0000 while the eye is shut
        fields[c++] = F(sample.RightPupilDiametre);
        fields[c++] = F(sample.LeftEyeOpened);
        fields[c++] = F(sample.RightEyeOpened);

        fields[c++] = F(sample.PlayerPosition.X);
        fields[c++] = F(sample.PlayerPosition.Y);
        fields[c++] = F(sample.PlayerPosition.Z);

        fields[c++] = F(sample.PlayerRotation.X);
        fields[c++] = F(sample.PlayerRotation.Y);
        fields[c++] = F(sample.PlayerRotation.Z);
        fields[c++] = F(sample.PlayerRotation.W);

        // One ray is cast in Unity and written to both column groups, so the left and
        // right hit points are identical on 100 % of real rows. Do the same.
        fields[c++] = F(sample.HitPoint.X);
        fields[c++] = F(sample.HitPoint.Y);
        fields[c++] = F(sample.HitPoint.Z);
        fields[c++] = F(sample.HitPoint.X);
        fields[c++] = F(sample.HitPoint.Y);
        fields[c++] = F(sample.HitPoint.Z);

        fields[c] = Quote(settings.DeviceName);

        return string.Join(",", fields);
    }

    /// <summary>
    /// Wraps a text field in quotes, doubling any quote inside it as RFC 4180 requires.
    /// </summary>
    private static string Quote(string text)
    {
        return "\"" + (text ?? "").Replace("\"", "\"\"") + "\"";
    }

    private static string F(double value)
    {
        // Collapse -0 to 0: the real files never carry a signed zero.
        if (Math.Abs(value) < 0.00005) return "0";
        return value.ToString(FloatFormat, CultureInfo.InvariantCulture);
    }
}

// -------------------------------------------------------------------------------------
//  A fixation recovered from the generated file by re-running I-VT, exactly as a
//  downstream consumer of the real data would.
// -------------------------------------------------------------------------------------
public class DetectedFixation
{
    public double StartSeconds;
    public double EndSeconds;
    public Vector3d CentroidDirection;
    public double DurationSeconds { get { return EndSeconds - StartSeconds; } }
}

// -------------------------------------------------------------------------------------
//  Report and self-check. Every acceptance criterion of section 7 is evaluated here on
//  the material that was actually produced, and failures are reported rather than hidden.
// -------------------------------------------------------------------------------------
public static class SessionReport
{
    public static string Build(SimulationRun run, SimulationSettings settings, DateTime sessionStart,
        string csvContent, bool? deterministic)
    {
        List<GazeSample> samples = run.Samples;
        StringBuilder text = new StringBuilder();

        double firstTime = samples.Count > 0 ? samples[0].TimeSeconds : 0.0;
        double lastTime = samples.Count > 0 ? samples[samples.Count - 1].TimeSeconds : 0.0;
        double spanSeconds = lastTime - firstTime;

        List<double> plannedDurations = new List<double>();
        for (int i = 0; i < run.Fixations.Count; i++) plannedDurations.Add(run.Fixations[i].PlannedDurationSeconds);

        List<DetectedFixation> detected = DetectFixations(samples);
        List<double> detectedDurations = new List<double>();
        for (int i = 0; i < detected.Count; i++) detectedDurations.Add(detected[i].DurationSeconds);
        List<double> detectedAmplitudes = new List<double>();
        for (int i = 1; i < detected.Count; i++)
            detectedAmplitudes.Add(VectorMath.AngleDegrees(detected[i - 1].CentroidDirection, detected[i].CentroidDirection));

        double headYawMin = double.MaxValue, headYawMax = double.MinValue;
        double headPitchMin = double.MaxValue, headPitchMax = double.MinValue;
        double eyeYawMin = double.MaxValue, eyeYawMax = double.MinValue;
        double eyePitchMin = double.MaxValue, eyePitchMax = double.MinValue;
        int headInMotionSamples = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            GazeSample s = samples[i];
            if (s.HeadYawDegrees < headYawMin) headYawMin = s.HeadYawDegrees;
            if (s.HeadYawDegrees > headYawMax) headYawMax = s.HeadYawDegrees;
            if (s.HeadPitchDegrees < headPitchMin) headPitchMin = s.HeadPitchDegrees;
            if (s.HeadPitchDegrees > headPitchMax) headPitchMax = s.HeadPitchDegrees;
            if (s.EyeInHeadYawDegrees < eyeYawMin) eyeYawMin = s.EyeInHeadYawDegrees;
            if (s.EyeInHeadYawDegrees > eyeYawMax) eyeYawMax = s.EyeInHeadYawDegrees;
            if (s.EyeInHeadPitchDegrees < eyePitchMin) eyePitchMin = s.EyeInHeadPitchDegrees;
            if (s.EyeInHeadPitchDegrees > eyePitchMax) eyePitchMax = s.EyeInHeadPitchDegrees;
            if (i > 0)
            {
                double dt = samples[i].TimeSeconds - samples[i - 1].TimeSeconds;
                if (dt > 1e-6 &&
                    Math.Abs(samples[i].HeadYawDegrees - samples[i - 1].HeadYawDegrees) / dt > 10.0)
                    headInMotionSamples++;
            }
        }

        double invalidPercent = samples.Count > 0 ? 100.0 * run.InvalidFrameCount / samples.Count : 0.0;

        text.AppendLine("GAZE SESSION SIMULATION - REPORT");
        text.AppendLine("---------------------------------------------------------------");
        if (run.Warnings.Count > 0)
        {
            for (int i = 0; i < run.Warnings.Count; i++)
                text.AppendLine("!!  " + run.Warnings[i]);
            text.AppendLine("---------------------------------------------------------------");
        }
        text.AppendLine("Rows written ............... " + samples.Count +
                        "  (header + " + samples.Count + " data lines, newest first)");
        text.AppendLine("Session start .............. " + sessionStart.ToString("yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture));
        text.AppendLine("Session span ............... " + Fmt(spanSeconds) + " s (requested " + Fmt(settings.SessionSeconds) + " s)");
        text.AppendLine("Effective sample rate ...... " + Fmt(spanSeconds > 0 ? (samples.Count - 1) / spanSeconds : 0) + " Hz");
        text.AppendLine();
        text.AppendLine("Fixations requested ........ " + run.Fixations.Count +
                        "   median " + Fmt(Median(plannedDurations)) + " s   mean " + Fmt(Mean(plannedDurations)) + " s");
        text.AppendLine("Fixations recovered (I-VT) . " + detected.Count +
                        "   median " + Fmt(Median(detectedDurations)) + " s   mean " + Fmt(Mean(detectedDurations)) + " s");
        text.AppendLine("Movements made ............. " + run.RealisedSaccadeAmplitudes.Count +
                        "   median " + Fmt(Median(run.RealisedSaccadeAmplitudes)) + " deg   mean " +
                        Fmt(Mean(run.RealisedSaccadeAmplitudes)) + " deg");
        text.AppendLine("Fixation-to-fixation ....... " + detectedAmplitudes.Count +
                        "   median " + Fmt(Median(detectedAmplitudes)) + " deg (equals the saccade amplitude only" +
                        " when there is no free scanning)");
        double plannedFixationTime = 0.0;
        for (int i = 0; i < plannedDurations.Count; i++) plannedFixationTime += plannedDurations[i];
        double freeScanningPercent = samples.Count > 0 ? 100.0 * run.FreeScanningSamples / samples.Count : 0.0;
        text.AppendLine("Session composition ........ fixating " + Fmt(plannedFixationTime) + " s (" +
                        Fmt(settings.SessionSeconds > 0 ? 100.0 * plannedFixationTime / settings.SessionSeconds : 0) +
                        " %); free scanning " + run.FreeScanningSamples + " samples (" + Fmt(freeScanningPercent) +
                        " %); the rest is saccades and their corrections");
        if (run.FreeScanningStalls > 0)
            text.AppendLine("  Free-scanning stalls ..... " + run.FreeScanningStalls +
                            " samples could not move at all (gaze cornered against the neck and eye limits)");
        text.AppendLine("Saccades written ........... " + run.SaccadeHoldCount + " stretches, " +
                        Fmt(SimulationDefaults.SaccadeHoldMinSeconds) + "-" +
                        Fmt(SimulationDefaults.SaccadeHoldMaxSeconds) + " s each (" +
                        SimulationDefaults.SaccadeHoldsPerTransitionMin + "-" +
                        SimulationDefaults.SaccadeHoldsPerTransitionMax + " per transition), over " +
                        run.SaccadeHoldSampleCount + " frames that repeat the hit point verbatim; " +
                        Fmt(run.Fixations.Count > 0 ? run.SaccadeHoldCount / (double)run.Fixations.Count : 0) +
                        " per fixation");
        text.AppendLine();
        text.AppendLine("Star rays usable ........... " + run.StarSelectableCount + " of " +
                        (run.StarSelectableCount + run.StarIgnoredCount) + "   " + run.StarIgnoredCount +
                        " hit nothing ('" + SimulationDefaults.InvalidObjectName +
                        "' or blank) and were never offered as a gaze target");
        text.AppendLine("Distinct objects visited ... " + run.VisitedObjectNames.Count +
                        (run.DistinctObjectTargetLow > 0
                            ? "   target [" + run.DistinctObjectTargetLow + ", " + run.DistinctObjectTargetHigh + "] - " +
                              (run.VisitedObjectNames.Count >= run.DistinctObjectTargetLow &&
                               run.VisitedObjectNames.Count <= run.DistinctObjectTargetHigh ? "MET" : "MISSED")
                            : "   (steering disabled)"));
        text.AppendLine("Blink events ............... " + run.BlinkEventCount +
                        "   expected ~" + Fmt(settings.BlinkRatePerSecond * settings.SessionSeconds));

        // The pupil range belongs to the PARTICIPANT, so it is worth showing next to the
        // session numbers: it is the one line of the report that is the same in every file
        // this person appears in.
        double pupilLow = double.MaxValue, pupilHigh = double.MinValue;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].IsBlinkFrame) continue;
            if (samples[i].LeftPupilDiametre < pupilLow) pupilLow = samples[i].LeftPupilDiametre;
            if (samples[i].LeftPupilDiametre > pupilHigh) pupilHigh = samples[i].LeftPupilDiametre;
        }
        text.AppendLine("Pupil range of '" + settings.ParticipantId + "'" +
                        new string('.', Math.Max(1, 11 - settings.ParticipantId.Length)) + " " +
                        Fmt(run.PupilAnatomy.Minimum) + " .. " + Fmt(run.PupilAnatomy.Maximum) +
                        " mm anatomical (drawn once for the person, not for this session)" +
                        (pupilHigh >= pupilLow
                            ? ";\r\n                             used " + Fmt(pupilLow) + " .. " + Fmt(pupilHigh) +
                              " mm here, wandering at " + Fmt(run.PupilCyclesPerSecond) + " Hz"
                            : ""));
        // Stated outright, and on its own line, because a hold that silently did not happen
        // produces a file that looks entirely normal until the baseline it was written for
        // turns out to be a coin toss.
        text.AppendLine("Pupil baseline hold ........ " + (settings.BaselineSeconds > 0.0
            ? "first " + Fmt(settings.BaselineSeconds) + " s held within " +
              Fmt(SimulationDefaults.PupilBaselineExcursionMillimetres) + " mm of the opening value"
            : "OFF - the pupil wanders freely from the very first sample, and no fixation is driven"));
        if (settings.BaselineSeconds > 0.0)
            text.AppendLine("Pupil baseline (reader) .... " +
                (double.IsNaN(run.PupilBaseline) ? "none - the window held no valid pupil row"
                    : Fmt(run.PupilBaseline) + " mm   median row-mean over the window, the same" +
                      " number PluginCAISM will subtract") +
                ";\r\n                             " + run.DilatingFixationCount + " fixation(s) driven up from it, " +
                run.ConstrictingFixationCount + " driven down" +
                (run.FixationWallHoldCount > 0
                    ? ", " + run.FixationWallHoldCount + " row(s) repeating an anatomical limit having reached it"
                    : ""));
        text.AppendLine("Invalid frames ............. " + run.InvalidFrameCount + " (" + Fmt(invalidPercent) + " %)" +
                        "   requested " + Fmt(settings.InvalidFrameRate * 100.0) + " %");
        int blinkFramesForNote = 0;
        for (int i = 0; i < samples.Count; i++) if (samples[i].IsBlinkFrame) blinkFramesForNote++;
        if (samples.Count > 0 && blinkFramesForNote / (double)samples.Count > settings.InvalidFrameRate)
            text.AppendLine("  NOTE: blinks alone account for " + Fmt(100.0 * blinkFramesForNote / samples.Count) +
                            " % of frames, which is above the requested invalid rate. A shut eye" +
                            "\r\n        produces no gaze, so that is the floor; lower BlinkRatePerSecond to go below it.");
        text.AppendLine();
        text.AppendLine("Head yaw range ............. " + Fmt(headYawMin) + " .. " + Fmt(headYawMax) + " deg" +
                        "   (limit +/-" + Fmt(settings.NeckYawLimitDeg) + ")");
        text.AppendLine("Head pitch range ........... " + Fmt(headPitchMin) + " .. " + Fmt(headPitchMax) + " deg" +
                        "   (limits -" + Fmt(settings.NeckPitchDownLimitDeg) + " .. +" + Fmt(settings.NeckPitchUpLimitDeg) + ")");
        text.AppendLine("Head in motion (>10 deg/s) . " + Fmt(samples.Count > 1 ? 100.0 * headInMotionSamples / (samples.Count - 1) : 0) +
                        " %   (real sessions ~29 %)");
        text.AppendLine("Eye-in-head yaw range ...... " + Fmt(eyeYawMin) + " .. " + Fmt(eyeYawMax) + " deg" +
                        "   (limit +/-" + Fmt(settings.EyeYawLimitDeg) + ")");
        text.AppendLine("Eye-in-head pitch range .... " + Fmt(eyePitchMin) + " .. " + Fmt(eyePitchMax) + " deg" +
                        "   (limit +/-" + Fmt(settings.EyePitchLimitDeg) + ")");
        if (run.UnreachableTargetFallbackCount > 0)
            text.AppendLine("Saccade targets abandoned .. " + run.UnreachableTargetFallbackCount +
                            " (no reachable candidate inside the neck/eye budget)");
        text.AppendLine();
        text.AppendLine("SELF-CHECK (section 7)");
        text.AppendLine("---------------------------------------------------------------");

        List<string> lines = new List<string>();
        int failures = 0;

        // 1. Row count.
        int expectedRows = (int)Math.Round(settings.SessionSeconds * settings.SampleRateHz);
        double rowError = expectedRows > 0 ? Math.Abs(samples.Count - expectedRows) / (double)expectedRows : 1.0;
        Record(lines, ref failures, rowError <= 0.02,
            "1  row count " + samples.Count + " vs expected " + expectedRows + " (" + Fmt(rowError * 100.0) + " % off, limit 2 %)");

        // 2. Header, character for character.
        string[] allLines = csvContent.Split(new string[] { GazeCsvWriter.LineTerminator }, StringSplitOptions.None);
        bool headerOk = allLines.Length > 0 && string.Equals(allLines[0], GazeCsvWriter.HeaderLine, StringComparison.Ordinal);
        Record(lines, ref failures, headerOk, "2  header line matches the 38 declared columns exactly");

        // 3. Every data row parses back into 38 fields.
        int badFieldCountRows = 0;
        int parsedRows = 0;
        for (int i = 1; i < allLines.Length; i++)
        {
            if (allLines[i].Length == 0) continue;
            parsedRows++;
            if (ParseQuotedRow(allLines[i]).Length != GazeCsvWriter.ColumnCount) badFieldCountRows++;
        }
        Record(lines, ref failures, badFieldCountRows == 0 && parsedRows == samples.Count,
            "3  all " + parsedRows + " data rows parse back into 38 fields (" + badFieldCountRows + " bad)");

        // 4. Timestamps strictly descending through the file.
        int timestampOrderFailures = 0;
        string previousStamp = null;
        for (int i = 1; i < allLines.Length; i++)
        {
            if (allLines[i].Length == 0) continue;
            string stamp = ParseQuotedRow(allLines[i])[0];
            if (previousStamp != null && string.CompareOrdinal(previousStamp, stamp) <= 0) timestampOrderFailures++;
            previousStamp = stamp;
        }
        Record(lines, ref failures, timestampOrderFailures == 0,
            "4  timestamps strictly descending (" + timestampOrderFailures + " violations)");

        // 5. Time span.
        double spanError = settings.SessionSeconds > 0 ? Math.Abs(spanSeconds - settings.SessionSeconds) / settings.SessionSeconds : 1.0;
        Record(lines, ref failures, spanError <= 0.01,
            "5  first-to-last span " + Fmt(spanSeconds) + " s vs " + Fmt(settings.SessionSeconds) + " s (" + Fmt(spanError * 100.0) + " % off, limit 1 %)");

        // 6. Left and right hit points identical.
        int hitPointMismatches = 0;
        for (int i = 1; i < allLines.Length; i++)
        {
            if (allLines[i].Length == 0) continue;
            string[] f = ParseQuotedRow(allLines[i]);
            if (f.Length != GazeCsvWriter.ColumnCount) { hitPointMismatches++; continue; }
            for (int k = 0; k < 3; k++)
                if (!string.Equals(f[31 + k], f[34 + k], StringComparison.Ordinal)) { hitPointMismatches++; break; }
        }
        Record(lines, ref failures, hitPointMismatches == 0,
            "6  leftHitPoint == rightHitPoint on every row (" + hitPointMismatches + " mismatches)");

        // 7. Eye orientation quaternions are unit.
        double worstQuaternionError = 0.0;
        for (int i = 0; i < samples.Count; i++)
        {
            worstQuaternionError = Math.Max(worstQuaternionError, Math.Abs(samples[i].LeftEyeOrientation.Norm - 1.0));
            worstQuaternionError = Math.Max(worstQuaternionError, Math.Abs(samples[i].RightEyeOrientation.Norm - 1.0));
        }
        Record(lines, ref failures, worstQuaternionError <= 0.0005,
            "7  eye orientation quaternions unit (worst |norm-1| = " + worstQuaternionError.ToString("F6", CultureInfo.InvariantCulture) + ")");

        // 8. Player position statistics.
        double sdX = StandardDeviation(samples, 0), sdY = StandardDeviation(samples, 1), sdZ = StandardDeviation(samples, 2);
        double meanOffset = MeanPositionOffset(samples, settings.EyeOrigin);
        bool positionOk = InRange(sdX, 0.01, 0.12) && InRange(sdY, 0.01, 0.12) && InRange(sdZ, 0.01, 0.12) && meanOffset <= 0.15;
        Record(lines, ref failures, positionOk,
            "8  player position sd " + Fmt(sdX) + " / " + Fmt(sdY) + " / " + Fmt(sdZ) + " m (1-12 cm), mean offset " + Fmt(meanOffset) + " m (<= 0.15)");

        // 9. Neck limits.
        bool neckOk = headYawMax <= settings.NeckYawLimitDeg + 1e-6 && headYawMin >= -settings.NeckYawLimitDeg - 1e-6 &&
                      headPitchMax <= settings.NeckPitchUpLimitDeg + 1e-6 && headPitchMin >= -settings.NeckPitchDownLimitDeg - 1e-6;
        Record(lines, ref failures, neckOk, "9  head stayed inside the neck limits on every sample");

        // 10. Eye budget.
        Record(lines, ref failures, run.EyeBudgetViolationCount == 0,
            "10 eye-in-head stayed inside its limits (" + run.EyeBudgetViolationCount + " violations)");

        // 11. I-VT recovery - the most important check. The 20 % band is relative, so it
        // collapses to nothing when only a couple of fixations were asked for; two
        // fixations of absolute slack keep it meaningful there without loosening it at all
        // for a normal session, where 20 % of several hundred dominates.
        //
        // A corrective pause holds the gaze perfectly still, so I-VT recovers it as a fixation
        // that was never requested. Those are subtracted by their recorded time spans - exactly,
        // not by a widened tolerance - so this check keeps the same power over the fixations
        // that WERE requested as it had before pauses existed.
        List<double> genuineDurations;
        List<DetectedFixation> genuine = ExcludeCorrectivePauses(detected, run, out genuineDurations);
        if (genuineDurations.Count == 0)
            for (int i = 0; i < genuine.Count; i++) genuineDurations.Add(genuine[i].DurationSeconds);

        int countTolerance = Math.Max(2, (int)Math.Ceiling(0.20 * run.Fixations.Count));
        double countError = run.Fixations.Count > 0
            ? Math.Abs(genuine.Count - run.Fixations.Count) / (double)run.Fixations.Count : 1.0;
        double medianRequested = Median(plannedDurations);
        double medianDetected = Median(genuineDurations);
        double medianError = medianRequested > 0 ? Math.Abs(medianDetected - medianRequested) / medianRequested : 1.0;
        // The COUNT above gets absolute slack when few fixations were asked for; the median
        // needs the same treatment for the same reason, and the percentage band alone does
        // not give it. A median of n log-normal draws has a standard error of about
        // 1.253 sigma / sqrt(n) relative to itself: 4 % of the median over a few hundred
        // fixations, but 50 % over three. So a 25 % band is a five sigma line on a normal
        // session and a coin toss on a chaotic one. Passing on EITHER gate keeps the 25 %
        // band exactly as strict as it was wherever the sample can support it, and stops a
        // three-fixation session from failing on noise alone. (The [min, max] truncation
        // narrows the real spread, so the sigma here is an over-estimate - it errs toward
        // slack only where slack is already warranted.)
        double medianStandardError = genuineDurations.Count > 0
            ? 1.253 * settings.FixationDurationLogSigma / Math.Sqrt(genuineDurations.Count)
            : double.MaxValue;
        double medianSigmas = medianStandardError > 0 ? medianError / medianStandardError : 0.0;
        Record(lines, ref failures,
            Math.Abs(genuine.Count - run.Fixations.Count) <= countTolerance &&
            (medianError <= 0.25 || medianSigmas <= 2.5),
            "11 I-VT recovers " + genuine.Count + " fixations vs " + run.Fixations.Count + " requested (" +
            Fmt(countError * 100.0) + " % off, limit 20 %), after setting aside " +
            (detected.Count - genuine.Count) + " of " + run.SaccadeHoldCount +
            " corrective pauses; median over the " + genuineDurations.Count + " that touched no pause is " +
            Fmt(medianDetected) + " s vs " + Fmt(medianRequested) + " s (" +
            Fmt(medianError * 100.0) + " % off, limit 25 %; that is " + Fmt(medianSigmas) +
            " sampling sigma, limit 2.5)");

        // 12. Saccade amplitude. The reference is exp(mu) only while the [min, max] range
        // does not bite; setting SaccadeAmplitudeMinDeg above exp(mu) truncates the
        // distribution and moves its median, so the target is the median of the TRUNCATED
        // distribution the caller actually asked for. Both numbers are reported.
        double untruncatedMedian = Math.Exp(settings.SaccadeAmplitudeLogMu);
        double expectedMedianAmplitude = EstimateTruncatedLogNormalMedian(
            settings.SaccadeAmplitudeLogMu, settings.SaccadeAmplitudeLogSigma,
            settings.SaccadeAmplitudeMinDeg, settings.SaccadeAmplitudeMaxDeg, settings.Seed);
        // Measured over every movement the walk actually made. The distance between detected
        // fixation centroids is only the same thing while the gaps are short: once free
        // scanning fills them, consecutive fixations are separated by a whole random walk,
        // not by one saccade, and that distance says nothing about the amplitude used.
        double amplitudeMedian = Median(run.RealisedSaccadeAmplitudes);
        double amplitudeError = Math.Abs(amplitudeMedian - expectedMedianAmplitude) / expectedMedianAmplitude;
        string amplitudeNote = Math.Abs(expectedMedianAmplitude - untruncatedMedian) > 0.05 * untruncatedMedian
            ? "; exp(mu) alone would be " + Fmt(untruncatedMedian) + " deg, raised by the [" +
              Fmt(settings.SaccadeAmplitudeMinDeg) + ", " + Fmt(settings.SaccadeAmplitudeMaxDeg) + "] truncation"
            : "";
        Record(lines, ref failures, amplitudeError <= 0.30,
            "12 median saccade amplitude " + Fmt(amplitudeMedian) + " deg over " +
            run.RealisedSaccadeAmplitudes.Count + " movements vs requested " + Fmt(expectedMedianAmplitude) +
            " deg (" + Fmt(amplitudeError * 100.0) + " % off, limit 30 %)" + amplitudeNote);

        // 13. Pupils. Four separate things have to hold: the value stays inside the
        // participant's own anatomical range, the trace is SMOOTH rather than jittery, it
        // stays next to its opening value for as long as the consumer's baseline window
        // lasts, and after that window the distribution of how far it moves in one second is
        // the one that was asked for.
        double anatomicalMin = run.PupilAnatomy.Minimum, anatomicalMax = run.PupilAnatomy.Maximum;
        int pupilRangeFailures = 0, pupilMarkerFailures = 0;
        List<double> perSampleSteps = new List<double>();
        int sameDirection = 0, directionPairs = 0;
        double previousValidLeft = double.NaN, previousStep = double.NaN;
        double lowestSeen = double.MaxValue, highestSeen = double.MinValue;
        double widestEyeGap = 0.0;
        double baselineOpeningValue = double.NaN, widestBaselineExcursion = 0.0;
        int baselineSampleCount = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            GazeSample s = samples[i];
            if (s.IsBlinkFrame)
            {
                if (s.LeftPupilDiametre != -1.0 || s.RightPupilDiametre != -1.0) pupilMarkerFailures++;
                previousValidLeft = double.NaN;
                previousStep = double.NaN;
                continue;
            }
            if (!InRange(s.LeftPupilDiametre, anatomicalMin - 1e-9, anatomicalMax + 1e-9) ||
                !InRange(s.RightPupilDiametre, anatomicalMin - 1e-9, anatomicalMax + 1e-9)) pupilRangeFailures++;
            if (s.LeftPupilDiametre < lowestSeen) lowestSeen = s.LeftPupilDiametre;
            if (s.LeftPupilDiametre > highestSeen) highestSeen = s.LeftPupilDiametre;
            // The baseline hold, measured the way the consumer would: against the first
            // diametre in the file, over the window it calls its baseline.
            if (settings.BaselineSeconds > 0.0 && s.TimeSeconds <= settings.BaselineSeconds)
            {
                if (double.IsNaN(baselineOpeningValue)) baselineOpeningValue = s.LeftPupilDiametre;
                double excursion = Math.Abs(s.LeftPupilDiametre - baselineOpeningValue);
                if (excursion > widestBaselineExcursion) widestBaselineExcursion = excursion;
                baselineSampleCount++;
            }
            double eyeGap = Math.Abs(s.RightPupilDiametre - s.LeftPupilDiametre);
            if (eyeGap > widestEyeGap) widestEyeGap = eyeGap;
            if (!double.IsNaN(previousValidLeft))
            {
                double step = s.LeftPupilDiametre - previousValidLeft;
                perSampleSteps.Add(Math.Abs(step));
                if (!double.IsNaN(previousStep))
                {
                    directionPairs++;
                    if (step * previousStep > 0.0) sameDirection++;
                }
                previousStep = step;
            }
            previousValidLeft = s.LeftPupilDiametre;
        }

        // Smoothness, and it is the whole point of the model. Between two adjacent rows the
        // pupil may only move ONE ROW INTERVAL's worth of its per-second pace - so the
        // faster the rows, the smaller the step. The old AR(1) model stepped about 0.14 mm
        // between rows no matter how close together they were, which is what made the trace
        // jagged and what tied it to SampleRateHz; at any rate at all it fails this line.
        double medianStep = Median(perSampleSteps);
        double fastestLadderSpeed =
            SimulationDefaults.PupilSpeedLadder[SimulationDefaults.PupilSpeedLadder.Length - 1];
        double stepCeiling = 1.5 * (1.0 / settings.SampleRateHz) * fastestLadderSpeed + 1e-6;
        double directionShare = directionPairs > 0 ? 100.0 * sameDirection / directionPairs : 0.0;

        // Measured only over the FREE part of the session. Inside the baseline window the
        // pupil is deliberately held next to its opening value, so a pair taken from there
        // says how hard the squeeze is, not whether the ladder is right; counting the two
        // together would report a distribution nobody asked for and hide both.
        double freeFromSeconds = PupilFreeFromSeconds(settings);
        int bandPairCount;
        double[] bandShares = OneSecondChangeBands(samples, freeFromSeconds, out bandPairCount);
        StringBuilder bandText = new StringBuilder();
        double worstBandError = 0.0;
        for (int b = 0; b < bandShares.Length; b++)
        {
            double error = Math.Abs(bandShares[b] - SimulationDefaults.PupilChangeBandTargets[b]);
            if (error > worstBandError) worstBandError = error;
            if (b > 0) bandText.Append(" / ");
            bandText.Append(Fmt(bandShares[b]));
        }
        StringBuilder targetText = new StringBuilder();
        for (int b = 0; b < SimulationDefaults.PupilChangeBandTargets.Length; b++)
        {
            if (b > 0) targetText.Append(" / ");
            targetText.Append(Fmt(SimulationDefaults.PupilChangeBandTargets[b]));
        }

        // The band split is reported per session but gated LOOSELY, for the same reason the
        // blink count is: one 300 s session holds only a dozen independent stretches of
        // arousal, so its split rattles around by ten points or more however right the
        // generator is. What decides whether the distribution is actually on target is the
        // pooled measurement over twenty participants in the bench's PUPIL SIGNAL section;
        // this line is here to catch a session that has gone somewhere else entirely.
        //
        // A session no longer than the baseline window has no free part at all, so there is
        // nothing to measure and the gate stands down rather than reporting an empty split as
        // a failure. Fifty pairs is roughly the point below which the split is meaningless.
        //
        // The gate also stands down on any session whose fixations are DRIVEN, which is every
        // session with a baseline window. The ladder's split describes a free random walk;
        // once a fixation's rows all move the same way the second's worth of movement is
        // coherent instead of cancelling, and near a wall it is repeated instead of moving at
        // all. Neither is a defect - both are what the drive is for - so the split is reported
        // as a description of the file and judged against the ladder only where the ladder
        // still applies. What polices the ladder now is the bench's PUPIL SIGNAL section,
        // which measures it undriven, and the per-row step ceiling above, which the drive
        // cannot inflate because it never changes |dp|.
        bool bandGateApplies = bandPairCount >= 50 && settings.BaselineSeconds <= 0.0;

        // The baseline hold. The band is +/- PupilBaselineExcursionMillimetres around the
        // first sample; the slack on top of it covers a session that opens on a blink, where
        // the first diametre in the FILE is a fraction of a second later than the one the
        // squeeze is anchored to.
        double baselineCeiling = SimulationDefaults.PupilBaselineExcursionMillimetres + 0.05;
        bool baselineHolds = baselineSampleCount == 0 || widestBaselineExcursion <= baselineCeiling;
        string baselineText = settings.BaselineSeconds > 0.0
            ? (baselineSampleCount > 0
                ? "; over the " + Fmt(settings.BaselineSeconds) + " s baseline window it stayed within " +
                  Fmt(widestBaselineExcursion) + " mm of the " + Fmt(baselineOpeningValue) +
                  " mm it opened on (ceiling " + Fmt(baselineCeiling) + ", over " + baselineSampleCount + " rows)"
                : "; the baseline window holds no valid row")
            : "; no baseline window requested";
        string bandGateText = bandGateApplies
            ? " % (worst band " + Fmt(worstBandError) + " off, session-scale limit 20 - the bench" +
              " pools 20 participants to hold this to 6)"
            : bandPairCount < 50
                ? " % (only " + bandPairCount + " pairs outside the baseline window, too few to gate on)"
                : " % (worst band " + Fmt(worstBandError) + " off, NOT gated: the fixations are driven," +
                  " so this describes the file rather than the ladder - see the bench)";

        // Every fixation that started after the baseline was measured has to move ONE WAY
        // ONLY, away from that baseline, from its own first row to its last. This is exact -
        // it is a property of the emitted numbers, not a statistic - so it is gated hard, and
        // the offending fixation is named rather than just counted.
        int drivenFixations = 0, wrongWayFixations = 0, firstWrongFixation = -1;
        double widestWrongWayStep = 0.0;
        List<double> drivenExcursions = new List<double>();
        if (!double.IsNaN(run.PupilBaseline))
        {
            int index = 0;
            while (index < samples.Count)
            {
                int fixation = samples[index].FixationIndex;
                if (fixation < 0) { index++; continue; }

                // The WHOLE fixation is found first, then judged. Walking one sample at a
                // time and skipping the early ones would treat the tail of a fixation that
                // straddles the window as a fixation of its own - and that one is not driven,
                // because it started while there was still no baseline to be driven away from.
                int end = index;
                while (end + 1 < samples.Count && samples[end + 1].FixationIndex == fixation) end++;

                if (samples[index].TimeSeconds > settings.BaselineSeconds)
                {
                    double opening = double.NaN, previous = double.NaN, worstAgainst = 0.0;
                    for (int i = index; i <= end; i++)
                    {
                        if (samples[i].IsBlinkFrame) continue;
                        double value = samples[i].LeftPupilDiametre;
                        if (double.IsNaN(opening)) opening = value;
                        else
                        {
                            double against = (opening >= run.PupilBaseline ? -1.0 : 1.0) * (value - previous);
                            if (against > worstAgainst) worstAgainst = against;
                        }
                        previous = value;
                    }

                    if (!double.IsNaN(opening))
                    {
                        drivenFixations++;
                        drivenExcursions.Add((opening >= run.PupilBaseline ? 1.0 : -1.0) * (previous - opening));
                        if (worstAgainst > 1e-9)
                        {
                            wrongWayFixations++;
                            if (firstWrongFixation < 0) firstWrongFixation = fixation;
                            if (worstAgainst > widestWrongWayStep) widestWrongWayStep = worstAgainst;
                        }
                    }
                }
                index = end + 1;
            }
        }
        string drivenText = double.IsNaN(run.PupilBaseline)
            ? "; no baseline, so no fixation was driven"
            : "; all " + drivenFixations + " fixation(s) after the " + Fmt(run.PupilBaseline) +
              " mm baseline moved away from it and never back" +
              (wrongWayFixations > 0
                  ? " EXCEPT " + wrongWayFixations + ", first at index " + firstWrongFixation +
                    ", worst step the wrong way " + Fmt(widestWrongWayStep) + " mm"
                  : ", by a median of " + Fmt(Median(drivenExcursions)) + " mm each");

        double eyeGapCeiling = SimulationDefaults.PupilRightEyeJitterMillimetres + 1e-9;
        Record(lines, ref failures,
            pupilRangeFailures == 0 && pupilMarkerFailures == 0 && widestEyeGap <= eyeGapCeiling &&
            medianStep <= stepCeiling && directionShare >= 60.0 && baselineHolds &&
            wrongWayFixations == 0 &&
            (!bandGateApplies || worstBandError <= 20.0),
            "13 pupils inside the participant's own " + Fmt(anatomicalMin) + " .. " + Fmt(anatomicalMax) +
            " mm (" + pupilRangeFailures + " outside, reached " + Fmt(lowestSeen) + " .. " + Fmt(highestSeen) +
            "), -1 on every blink frame (" + pupilMarkerFailures + " bad); the two eyes never differ by more than " +
            Fmt(widestEyeGap) + " mm (ceiling " + Fmt(eyeGapCeiling) + "); median step between rows " +
            Fmt(medianStep) + " mm (ceiling " + Fmt(stepCeiling) + "), " + Fmt(directionShare) +
            " % of steps continue the previous one (>= 60)" + baselineText + drivenText +
            "; change over 1 s after the window falls " + bandText +
            " % across [0, 0.3, 0.7, 1.0, 1.3+] against the requested " + targetText + bandGateText);

        // 14. Blink count. Blinks are a Poisson process, so the count has a standard
        // deviation of sqrt(expected) no matter how correct the generator is. The deviation
        // is therefore also reported in sigmas: at the default rate the +/-40 % band is only
        // about +/-2 sigma, so an occasional honest failure here is sampling noise, not a
        // defect. Below about 25 expected blinks the band is narrower than the noise floor.
        double expectedBlinks = settings.BlinkRatePerSecond * settings.SessionSeconds;
        double blinkError = expectedBlinks > 0 ? Math.Abs(run.BlinkEventCount - expectedBlinks) / expectedBlinks : 0.0;
        double blinkSigmas = expectedBlinks > 0
            ? Math.Abs(run.BlinkEventCount - expectedBlinks) / Math.Sqrt(expectedBlinks) : 0.0;
        // The percentage band alone fails honest sessions whenever it is tighter than the
        // noise, which the comment above says happens below ~25 expected blinks - a 60 s
        // session at the default rate expects 5.4, where +/-40 % is under one sigma. Passing
        // on either gate makes this a statistical test at every session length instead of
        // only at long ones.
        //
        // The sigma gate sits at 3.5 rather than 3 because this bench runs a FIXED seed list:
        // a 3 sigma line has a ~0.2 % tail, the sweep samples it 20-odd times, and any seed
        // that lands past it then fails on every run for ever. Whether the generator is
        // BIASED is not decided here at all - the 40-seed study at the end of the bench is
        // what answers that, and it compares the mean against its standard error.
        Record(lines, ref failures, blinkError <= 0.40 || blinkSigmas <= 3.5,
            "14 blink events " + run.BlinkEventCount + " vs expected " + Fmt(expectedBlinks) + " (" +
            Fmt(blinkError * 100.0) + " % off, limit 40 %; that is " + Fmt(blinkSigmas) +
            " Poisson sigma, limit 3.5)");

        // 15. Invalid frame percentage. A shut eye cannot produce a gaze, so blink frames are
        // always invalid - that puts a hard floor under the achievable rate. When the
        // requested rate sits below the floor the check is made against the floor, and the
        // report says so, rather than failing on a target that is physically unreachable.
        int blinkFrameCount = 0;
        for (int i = 0; i < samples.Count; i++) if (samples[i].IsBlinkFrame) blinkFrameCount++;
        double blinkFrameFraction = samples.Count > 0 ? blinkFrameCount / (double)samples.Count : 0.0;
        double invalidFraction = samples.Count > 0 ? run.InvalidFrameCount / (double)samples.Count : 0.0;
        double effectiveInvalidTarget = Math.Max(settings.InvalidFrameRate, blinkFrameFraction);
        double invalidError = effectiveInvalidTarget > 0
            ? Math.Abs(invalidFraction - effectiveInvalidTarget) / effectiveInvalidTarget : invalidFraction;
        // Same reasoning as check 14: this is a binomial count over the emitted frames, so a
        // short session is judged against the sampling noise rather than a flat percentage.
        double invalidSigma = samples.Count > 0 && effectiveInvalidTarget > 0
            ? Math.Sqrt(effectiveInvalidTarget * (1.0 - effectiveInvalidTarget) / samples.Count) : 0.0;
        double invalidSigmas = invalidSigma > 0
            ? Math.Abs(invalidFraction - effectiveInvalidTarget) / invalidSigma : 0.0;
        Record(lines, ref failures, invalidError <= 0.30 || invalidSigmas <= 3.0,
            "15 invalid frames " + Fmt(invalidFraction * 100.0) + " % vs requested " +
            Fmt(settings.InvalidFrameRate * 100.0) + " %" +
            (effectiveInvalidTarget > settings.InvalidFrameRate + 1e-9
                ? ", raised to the blink floor " + Fmt(effectiveInvalidTarget * 100.0) + " %"
                : "") +
            " (" + Fmt(invalidError * 100.0) + " % off, limit 30 %; that is " + Fmt(invalidSigmas) +
            " binomial sigma, limit 3)");

        // 16. Determinism. Null when a batch skipped the second run; saying so beats
        // reporting a PASS nobody checked.
        if (deterministic.HasValue)
            Record(lines, ref failures, deterministic.Value,
                "16 re-running with the same seed produced a byte-identical file");
        else
            lines.Add("SKIP  16 re-running with the same seed - not checked on this session " +
                      "(a batch checks its first session only)");

        // Extra: the stage 6 round-trip assertion on the head quaternion.
        double worstRoundTripDegrees = 0.0;
        for (int i = 0; i < samples.Count; i++)
        {
            Vector3d rebuilt = samples[i].PlayerRotation.Rotate(new Vector3d(0, 0, 1));
            worstRoundTripDegrees = Math.Max(worstRoundTripDegrees,
                VectorMath.AngleDegrees(rebuilt, samples[i].HeadForwardWorld));
        }
        Record(lines, ref failures, worstRoundTripDegrees < 0.01,
            "+  playerRotation * (0,0,1) reproduces head forward (worst " +
            worstRoundTripDegrees.ToString("F5", CultureInfo.InvariantCulture) + " deg, limit 0.01)");

        // Extra: the eye quaternion's forward axis, measured from the real files as +Z.
        double worstEyeRoundTripDegrees = 0.0;
        List<double> orientationDivergence = new List<double>();
        List<double> gazeColumnDivergence = new List<double>();
        int negativeWCount = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            GazeSample s = samples[i];
            worstEyeRoundTripDegrees = Math.Max(worstEyeRoundTripDegrees,
                VectorMath.AngleDegrees(s.LeftEyeOrientation.Rotate(new Vector3d(0, 0, 1)), s.LeftEyeInHeadDirection));
            worstEyeRoundTripDegrees = Math.Max(worstEyeRoundTripDegrees,
                VectorMath.AngleDegrees(s.RightEyeOrientation.Rotate(new Vector3d(0, 0, 1)), s.RightEyeInHeadDirection));
            orientationDivergence.Add(VectorMath.AngleDegrees(
                s.LeftEyeOrientation.Rotate(new Vector3d(0, 0, 1)), s.RightEyeOrientation.Rotate(new Vector3d(0, 0, 1))));
            gazeColumnDivergence.Add(VectorMath.AngleDegrees(s.LeftEyeGazeColumn, s.RightEyeGazeColumn));
            if (s.LeftEyeOrientation.W <= 0.0) negativeWCount++;
        }
        Record(lines, ref failures, worstEyeRoundTripDegrees < 0.01,
            "+  eyeOrientation * (0,0,1) reproduces the eye-in-head direction (worst " +
            worstEyeRoundTripDegrees.ToString("F5", CultureInfo.InvariantCulture) + " deg, limit 0.01)");
        Record(lines, ref failures,
            Median(gazeColumnDivergence) > 3.0 && Median(gazeColumnDivergence) < 6.5 &&
            Median(orientationDivergence) < 0.10,
            "+  inter-eye divergence: EyeGaze columns " + Fmt(Median(gazeColumnDivergence)) +
            " deg (real 4.1-4.7), EyeOrientation " + Fmt(Median(orientationDivergence)) + " deg (real 0.008-0.016)");
        Record(lines, ref failures, negativeWCount == samples.Count,
            "+  eye orientation written in the logger's negative-W convention (" +
            negativeWCount + "/" + samples.Count + " rows)");

        // Extra: the schedule must respect the caller's own duration limits. Without this,
        // an impossible fixation count is absorbed by rescaling the durations, and the
        // session silently turns into a handful of enormous, motionless stares.
        double longestPlanned = 0.0, shortestPlanned = double.MaxValue;
        for (int i = 0; i < plannedDurations.Count; i++)
        {
            longestPlanned = Math.Max(longestPlanned, plannedDurations[i]);
            shortestPlanned = Math.Min(shortestPlanned, plannedDurations[i]);
        }
        if (plannedDurations.Count == 0) shortestPlanned = 0.0;
        Record(lines, ref failures,
            longestPlanned <= settings.FixationDurationMaxSec + 1e-6 &&
            shortestPlanned >= settings.FixationDurationMinSec - 1e-6,
            "+  planned fixation durations inside the requested range: " + Fmt(shortestPlanned) + " .. " +
            Fmt(longestPlanned) + " s (requested " + Fmt(settings.FixationDurationMinSec) + " .. " +
            Fmt(settings.FixationDurationMaxSec) + " s)");

        for (int i = 0; i < lines.Count; i++) text.AppendLine(lines[i]);
        text.AppendLine("---------------------------------------------------------------");
        text.AppendLine(failures == 0 ? "ALL CHECKS PASSED" : failures + " CHECK(S) FAILED - see the lines marked FAIL above");

        return text.ToString();
    }

    private static void Record(List<string> lines, ref int failures, bool passed, string description)
    {
        lines.Add((passed ? "PASS  " : "FAIL  ") + description);
        if (!passed) failures++;
    }

    /// <summary>
    /// I-VT fixation detection on the gaze ray a downstream consumer would reconstruct:
    /// the direction from the recorded player position to the recorded hit point.
    ///
    /// Invalid frames are DROPPED, and the series is closed up rather than split. They carry
    /// no gaze - the logger repeats the previous hit point - so keeping them would invent a
    /// perfectly still fixation out of every blink, and splitting on them would shred a
    /// session like the real 27.5 %-invalid one into fragments too short to classify.
    /// Dropping them leaves a slightly longer dt across the gap, which the velocity test
    /// handles correctly. They are identifiable in the file by the "Objeto sem nome" name,
    /// so any careful consumer of the real data would do the same.
    /// </summary>
    public static List<DetectedFixation> DetectFixations(List<GazeSample> samples)
    {
        List<GazeSample> valid = new List<GazeSample>(samples.Count);
        for (int i = 0; i < samples.Count; i++)
            if (!samples[i].IsInvalidFrame) valid.Add(samples[i]);

        List<DetectedFixation> fixations = new List<DetectedFixation>();
        DetectSegment(valid, 0, valid.Count, fixations);
        return fixations;
    }

    private static void DetectSegment(List<GazeSample> samples, int start, int end, List<DetectedFixation> output)
    {
        int count = end - start;
        if (count < 2) return;

        Vector3d[] directions = new Vector3d[count];
        for (int i = 0; i < count; i++)
        {
            GazeSample s = samples[start + i];
            directions[i] = VectorMath.Unit(new Vector3d(
                s.HitPoint.X - s.PlayerPosition.X,
                s.HitPoint.Y - s.PlayerPosition.Y,
                s.HitPoint.Z - s.PlayerPosition.Z));
        }

        int runStart = -1;
        for (int i = 1; i <= count; i++)
        {
            bool slowLink = false;
            if (i < count)
            {
                double dt = samples[start + i].TimeSeconds - samples[start + i - 1].TimeSeconds;
                if (dt > 1e-9)
                    slowLink = VectorMath.AngleDegrees(directions[i - 1], directions[i]) / dt <
                               SimulationDefaults.IvtVelocityThresholdDegPerSecond;
            }

            if (slowLink)
            {
                if (runStart < 0) runStart = i - 1;
            }
            else if (runStart >= 0)
            {
                EmitDetectedFixation(samples, directions, start, runStart, i - 1, output);
                runStart = -1;
            }
        }
    }

    private static void EmitDetectedFixation(List<GazeSample> samples, Vector3d[] directions,
        int offset, int firstLocal, int lastLocal, List<DetectedFixation> output)
    {
        double duration = samples[offset + lastLocal].TimeSeconds - samples[offset + firstLocal].TimeSeconds;
        if (duration < SimulationDefaults.IvtMinimumFixationSeconds) return;

        double x = 0, y = 0, z = 0;
        for (int i = firstLocal; i <= lastLocal; i++) { x += directions[i].X; y += directions[i].Y; z += directions[i].Z; }

        DetectedFixation fixation = new DetectedFixation();
        fixation.StartSeconds = samples[offset + firstLocal].TimeSeconds;
        fixation.EndSeconds = samples[offset + lastLocal].TimeSeconds;
        fixation.CentroidDirection = VectorMath.Unit(new Vector3d(x, y, z));
        output.Add(fixation);
    }

    /// <summary>
    /// Splits an RFC 4180 row: commas separate fields unless they sit inside a quoted
    /// field, and a doubled quote inside a quoted field is one literal quote. Reads both
    /// the generated files and the real HTC captures.
    /// </summary>
    public static string[] ParseQuotedRow(string line)
    {
        if (line.Length == 0) return new string[0];

        List<string> fields = new List<string>(GazeCsvWriter.ColumnCount);
        StringBuilder current = new StringBuilder();
        bool insideQuotes = false;
        for (int i = 0; i < line.Length; i++)
        {
            char c = line[i];
            if (c == '"')
            {
                if (insideQuotes && i + 1 < line.Length && line[i + 1] == '"') { current.Append('"'); i++; }
                else insideQuotes = !insideQuotes;
                continue;
            }
            if (c == ',' && !insideQuotes) { fields.Add(current.ToString()); current.Length = 0; continue; }
            current.Append(c);
        }
        fields.Add(current.ToString());
        return fields.ToArray();
    }

    /// <summary>
    /// Median of a log-normal after truncation to [minimum, maximum], by sampling. Uses its
    /// own generator so that measuring the target cannot disturb the simulation's stream.
    /// </summary>
    private static double EstimateTruncatedLogNormalMedian(double logMu, double logSigma,
        double minimum, double maximum, int seed)
    {
        DeterministicRandom probe = new DeterministicRandom(seed ^ 0x5ACCADE);
        const int probeCount = 4096;
        List<double> draws = new List<double>(probeCount);
        for (int i = 0; i < probeCount; i++)
            draws.Add(probe.NextTruncatedLogNormal(logMu, logSigma, minimum, maximum));
        return Median(draws);
    }

    private static bool InRange(double value, double minimum, double maximum)
    {
        return value >= minimum && value <= maximum;
    }

    /// <summary>
    /// The instant the pupil stops being held: the end of the consumer's baseline window.
    /// Everything before it is squeezed toward the session's opening value on purpose and
    /// says nothing about the speed ladder. Zero when no baseline window was asked for.
    /// </summary>
    public static double PupilFreeFromSeconds(SimulationSettings settings)
    {
        return settings.BaselineSeconds > 0.0 ? settings.BaselineSeconds : 0.0;
    }

    /// <summary>
    /// How far the left pupil moved over ONE SECOND, split across the requested bands and
    /// returned as percentages. Measured from every valid sample to the valid sample
    /// closest to a second later, which is the same thing a reader of the CSV would do -
    /// and, unlike a per-row difference, it does not change meaning when the row rate does.
    /// Pairs that would straddle a blink are dropped rather than measured across the gap, and
    /// so are the ones that start before <paramref name="fromSeconds"/>.
    /// </summary>
    private static double[] OneSecondChangeBands(List<GazeSample> samples, double fromSeconds,
        out int pairCount)
    {
        double[] edges = SimulationDefaults.PupilChangeBandEdges;
        double[] counts = new double[edges.Length + 1];
        int total = 0;

        int j = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            if (samples[i].IsBlinkFrame) continue;
            if (samples[i].TimeSeconds < fromSeconds) continue;
            double wanted = samples[i].TimeSeconds + 1.0;

            if (j < i) j = i;
            while (j + 1 < samples.Count &&
                   Math.Abs(samples[j + 1].TimeSeconds - wanted) < Math.Abs(samples[j].TimeSeconds - wanted)) j++;
            if (samples[j].IsBlinkFrame) continue;
            if (Math.Abs(samples[j].TimeSeconds - wanted) > 0.15) continue;

            double change = Math.Abs(samples[j].LeftPupilDiametre - samples[i].LeftPupilDiametre);
            int band = edges.Length;
            for (int b = 0; b < edges.Length; b++)
                if (change < edges[b]) { band = b; break; }
            counts[band]++;
            total++;
        }

        pairCount = total;
        double[] shares = new double[counts.Length];
        if (total == 0) return shares;
        for (int b = 0; b < counts.Length; b++) shares[b] = 100.0 * counts[b] / total;
        return shares;
    }

    private static double StandardDeviation(List<GazeSample> samples, int axis)
    {
        if (samples.Count < 2) return 0.0;
        double sum = 0.0, sumSquares = 0.0;
        for (int i = 0; i < samples.Count; i++)
        {
            double v = axis == 0 ? samples[i].PlayerPosition.X
                     : axis == 1 ? samples[i].PlayerPosition.Y
                                 : samples[i].PlayerPosition.Z;
            sum += v; sumSquares += v * v;
        }
        double mean = sum / samples.Count;
        double variance = sumSquares / samples.Count - mean * mean;
        return variance > 0 ? Math.Sqrt(variance) : 0.0;
    }

    private static double MeanPositionOffset(List<GazeSample> samples, Point3d origin)
    {
        if (samples.Count == 0) return 0.0;
        double x = 0, y = 0, z = 0;
        for (int i = 0; i < samples.Count; i++)
        {
            x += samples[i].PlayerPosition.X; y += samples[i].PlayerPosition.Y; z += samples[i].PlayerPosition.Z;
        }
        x = x / samples.Count - origin.X;
        y = y / samples.Count - origin.Y;
        z = z / samples.Count - origin.Z;
        return Math.Sqrt(x * x + y * y + z * z);
    }

    /// <summary>
    /// Drops the recovered fixations that are really the corrective pauses the simulation
    /// inserted, matched against the span each pause emitted frames over.
    ///
    /// The test is on the recovery's MIDPOINT, not on any overlap. A pause that lands next to
    /// a fixation is sometimes swallowed into it, and that merged recovery still corresponds
    /// to a requested fixation - discarding it on overlap alone lost about one fixation in
    /// five. Its midpoint sits inside the fixation, since a fixation outlasts a pause, so the
    /// midpoint keeps the merge and drops only what a pause produced on its own.
    /// </summary>
    /// <param name="untouchedDurations">
    /// Durations of the kept recoveries that do not touch a pause at all. A recovery that
    /// absorbed a pause is still one fixation and still counts, but its DURATION carries the
    /// pause inside it, so measuring the duration distribution on those would report the
    /// pause length as fixation length - visibly so when a session's fixations are as short
    /// as the pauses themselves.
    /// </param>
    private static List<DetectedFixation> ExcludeCorrectivePauses(List<DetectedFixation> detected, SimulationRun run,
        out List<double> untouchedDurations)
    {
        untouchedDurations = new List<double>(detected.Count);
        if (run.SaccadeHoldCount == 0)
        {
            for (int i = 0; i < detected.Count; i++) untouchedDurations.Add(detected[i].DurationSeconds);
            return detected;
        }

        List<DetectedFixation> kept = new List<DetectedFixation>(detected.Count);
        for (int i = 0; i < detected.Count; i++)
        {
            double midpoint = 0.5 * (detected[i].StartSeconds + detected[i].EndSeconds);
            bool fromPause = false;
            bool touchesPause = false;
            for (int h = 0; h < run.SaccadeHoldStartSeconds.Count; h++)
            {
                if (midpoint >= run.SaccadeHoldStartSeconds[h] && midpoint <= run.SaccadeHoldEndSeconds[h])
                    fromPause = true;
                if (detected[i].StartSeconds < run.SaccadeHoldEndSeconds[h] &&
                    detected[i].EndSeconds > run.SaccadeHoldStartSeconds[h])
                    touchesPause = true;
            }
            if (fromPause) continue;
            kept.Add(detected[i]);
            if (!touchesPause) untouchedDurations.Add(detected[i].DurationSeconds);
        }
        return kept;
    }

    private static double Median(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        List<double> sorted = values.OrderBy(v => v).ToList();
        int middle = sorted.Count / 2;
        return sorted.Count % 2 == 1 ? sorted[middle] : 0.5 * (sorted[middle - 1] + sorted[middle]);
    }

    private static double Mean(List<double> values)
    {
        if (values.Count == 0) return 0.0;
        double sum = 0.0;
        for (int i = 0; i < values.Count; i++) sum += values[i];
        return sum / values.Count;
    }

    private static string Fmt(double value)
    {
        return value.ToString("0.###", CultureInfo.InvariantCulture);
    }
}

#endregion
