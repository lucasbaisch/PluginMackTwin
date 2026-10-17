// Offline test bench for the Grasshopper component's simulation core.
//
// It builds a synthetic auditorium and a dense star of direction vectors (the work that
// Grasshopper does upstream of the component), runs the generator, prints the report and
// then compares the produced file against the real capture files' format.
//
// This file is NOT part of the Grasshopper deliverable.
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text;
using Rhino.Geometry;
using GazeSimulatorCore;

internal static class Program
{
    private static int Main(string[] args)
    {
        string outputPath = args.Length > 0
            ? args[0]
            : Path.Combine(Path.GetTempPath(), "SimulatedEyeGazingData.csv");

        int starVectorCount = args.Length > 1 ? int.Parse(args[1], CultureInfo.InvariantCulture) : 60000;

        Console.WriteLine("Building a synthetic auditorium star of " + starVectorCount + " vectors...");
        Auditorium room = new Auditorium();
        Point3d eyeOrigin = new Point3d(0.0, 1.20, -2.50);

        List<Vector3d> starVectors = new List<Vector3d>(starVectorCount);
        List<Point3d> starHitPoints = new List<Point3d>(starVectorCount);
        List<string> starObjectNames = new List<string>(starVectorCount);

        double minimumDistance = double.MaxValue, maximumDistance = 0.0;
        foreach (Vector3d direction in FibonacciSphere(starVectorCount))
        {
            Point3d hit;
            string name;
            double distance = room.Raycast(eyeOrigin, direction, out hit, out name);
            starVectors.Add(direction);
            starHitPoints.Add(hit);
            starObjectNames.Add(name);
            if (distance < minimumDistance) minimumDistance = distance;
            if (distance > maximumDistance) maximumDistance = distance;
        }
        Console.WriteLine("Hit distances " + minimumDistance.ToString("F2") + " - " +
                          maximumDistance.ToString("F2") + " m over " +
                          starObjectNames.Distinct().Count() + " distinct objects.");
        Console.WriteLine();

        SimulationSettings settings = BuildSettings(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);

        // Start looking straight ahead.
        settings.StartVectorIndex = NearestStarIndex(starVectors, new Vector3d(0, 0, 1));

        DateTime started = DateTime.UtcNow;
        SessionSimulator simulator = new SessionSimulator();
        SimulationResult result = simulator.Generate(settings);
        Console.WriteLine("Generated in " + (DateTime.UtcNow - started).TotalSeconds.ToString("F1") + " s");
        Console.WriteLine();
        Console.WriteLine(result.ReportText);

        if (!result.Succeeded)
        {
            Console.WriteLine("FAILURE: " + result.FailureMessage);
            return 1;
        }

        Console.WriteLine("Wrote " + result.CsvPath);
        Console.WriteLine();

        int formatFailures = CompareFormatWithRealFiles(result.CsvPath);

        bool reportHasFailures = result.ReportText.Contains("FAIL  ");
        int sweepFailures = RunSweep(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);

        return (reportHasFailures || formatFailures > 0 || sweepFailures > 0) ? 1 : 0;
    }

    /// <summary>
    /// Repeats the generation across seeds and parameter variations, so a green run cannot
    /// be a lucky seed.
    /// </summary>
    private static int RunSweep(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("SEED AND PARAMETER SWEEP");
        Console.WriteLine("---------------------------------------------------------------");

        int totalFailures = 0;
        int[] seeds = { 1, 2, 7, 42, 1234, 99999, -5, 20260725 };

        foreach (int seed in seeds)
            totalFailures += RunCase("seed " + seed, starVectors, starHitPoints, starObjectNames, eyeOrigin,
                outputPath, s => { s.Seed = seed; });

        totalFailures += RunCase("60 s session", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s => { s.Seed = 11; s.SessionSeconds = 60.0; });
        totalFailures += RunCase("900 s session", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s => { s.Seed = 12; s.SessionSeconds = 900.0; });
        totalFailures += RunCase("explicit count 200-260", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s => { s.Seed = 13; s.FixationCountMin = 200; s.FixationCountMax = 260; });
        totalFailures += RunCase("session 1 stats", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s =>
            {
                s.Seed = 14;
                s.FixationRatePerSecond = 1.63; s.FixationDurationLogMu = -0.96; s.FixationDurationLogSigma = 0.83;
                s.SaccadeAmplitudeLogMu = 1.27; s.SaccadeAmplitudeLogSigma = 0.54;
                s.IntraFixationDispersionDeg = 1.63; s.InvalidFrameRate = 0.0075;
            });
        totalFailures += RunCase("session 2 stats", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s =>
            {
                s.Seed = 15;
                s.FixationRatePerSecond = 1.81; s.FixationDurationLogMu = -1.50; s.FixationDurationLogSigma = 0.61;
                s.SaccadeAmplitudeLogMu = 1.51; s.SaccadeAmplitudeLogSigma = 0.71;
                s.IntraFixationDispersionDeg = 1.01; s.InvalidFrameRate = 0.275; s.SampleRateHz = 16.0;
            });
        totalFailures += RunCase("no blinks, no invalid frames", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s => { s.Seed = 16; s.BlinkRatePerSecond = 0.0; s.InvalidFrameRate = 0.0; });
        totalFailures += RunCase("steering off, no return bias", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s => { s.Seed = 17; s.DistinctObjectsMin = 0; s.DistinctObjectsMax = 0; s.ReturnBiasProbability = 0.0; });
        foreach (int tightSeed in new[] { 18, 181, 182, 183 })
        {
            int captured = tightSeed;
            totalFailures += RunCase("tight neck (30 deg) and eye (12 deg), seed " + captured,
                starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
                s =>
                {
                    s.Seed = captured; s.NeckYawLimitDeg = 30.0; s.NeckPitchUpLimitDeg = 20.0;
                    s.NeckPitchDownLimitDeg = 20.0; s.EyeYawLimitDeg = 12.0; s.EyePitchLimitDeg = 12.0;
                });
        }
        totalFailures += RunCase("30 Hz sampling", starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s => { s.Seed = 19; s.SampleRateHz = 30.0; });

        // The user's own "chaotic session" settings: a 1000 s session asked for 2-3
        // fixations, which used to be absorbed by rescaling the durations 600-fold.
        totalFailures += RunCase("chaotic-session request (2-3 fixations over 1000 s)",
            starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath,
            s =>
            {
                s.Seed = 44; s.SessionSeconds = 1000.0; s.SampleRateHz = 17.0;
                s.FixationCountMin = 2; s.FixationCountMax = 3; s.FixationRatePerSecond = 0.08;
                s.FixationDurationMinSec = 0.10; s.FixationDurationMaxSec = 1.00;
                s.SaccadeAmplitudeMinDeg = 10.0; s.SaccadeAmplitudeMaxDeg = 70.0;
                s.ReturnBiasProbability = 0.0;
                s.DistinctObjectsMin = 50; s.DistinctObjectsMax = 500;
                s.NeckYawLimitDeg = 85; s.NeckPitchUpLimitDeg = 53; s.NeckPitchDownLimitDeg = 53;
                s.EyeYawLimitDeg = 45; s.EyePitchLimitDeg = 45;
                s.InvalidFrameRate = 0.0; s.BlinkRatePerSecond = 0.06;
            });

        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine(totalFailures == 0
            ? "SWEEP PASSED - every case satisfied all self-checks"
            : totalFailures + " SWEEP CASE(S) REPORTED A FAILED CHECK");

        totalFailures += CheckPupilSignal(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);
        totalFailures += CheckZUpConversion(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);
        totalFailures += CheckMissedRaysAreNeverTargeted(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);
        totalFailures += CheckSeatJsonReader(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);
        totalFailures += CheckBatchRunner(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);
        totalFailures += CheckEnvironmentShape();
        CheckBlinkBias(starVectors, starHitPoints, starObjectNames, eyeOrigin, outputPath);
        return totalFailures;
    }

    // -----------------------------------------------------------------------------
    //  The pupil signal: smoothness, sample-rate independence, and the distribution of
    //  how far it moves in one second.
    // -----------------------------------------------------------------------------
    private static int CheckPupilSignal(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("PUPIL SIGNAL");
        Console.WriteLine("---------------------------------------------------------------");

        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        int failures = 0;

        // A tenth of the star keeps twenty-odd sessions quick; the pupil does not care what
        // the gaze is doing.
        List<Vector3d> vectors = new List<Vector3d>();
        List<Point3d> points = new List<Point3d>();
        List<string> names = new List<string>();
        for (int i = 0; i < starVectors.Count; i += 10)
        {
            vectors.Add(starVectors[i]); points.Add(starHitPoints[i]); names.Add(starObjectNames[i]);
        }

        Func<int, int, double, List<double>> deltasFor = (seed, participantSeed, sampleHz) =>
        {
            SimulationSettings s = BuildSettings(vectors, points, names, eyeOrigin,
                Path.Combine(directory, "pupil-probe.csv"));
            s.Seed = seed;
            s.ParticipantSeed = participantSeed;
            s.SampleRateHz = sampleHz;
            s.SessionSeconds = 300.0;
            s.BlinkRatePerSecond = 0.0;          // blink gaps only shrink the sample of pairs
            s.InvalidFrameRate = 0.0;
            // No baseline window, so no hold and no driven fixations: this is the FREE model,
            // which is what the speed ladder was fitted to and what these three checks are
            // about. Measuring them on a driven session would be measuring the driving - the
            // emitted trace is coherent inside every fixation by design, and no longer the
            // random walk these bands describe. What the driven signal does is checked below,
            // on its own terms.
            s.BaselineSeconds = 0.0;
            SimulationResult result = new SessionSimulator().Generate(s);
            // Only the free part of the session: the baseline window holds the pupil next to
            // its opening value on purpose, so its one-second changes measure the squeeze
            // rather than the speed ladder these bands are about.
            return OneSecondPupilChanges(result.LeftPupilDiametres, result.SampleTimes,
                SessionReport.PupilFreeFromSeconds(s));
        };

        // 1. The distribution of the one-second change, pooled over twenty participants.
        List<double> pooled = new List<double>();
        for (int p = 1; p <= 20; p++) pooled.AddRange(deltasFor(4000 + p, 7000 + p, 17.0));
        double[] shares = PupilBands(pooled);
        double[] target = { 45.0, 25.0, 15.0, 10.0, 5.0 };
        double worst = 0;
        for (int b = 0; b < shares.Length; b++) worst = Math.Max(worst, Math.Abs(shares[b] - target[b]));
        failures += Report(worst <= 6.0,
            "change over 1 s across 20 participants is " + Bands(shares) + " % vs the requested " +
            Bands(target) + " % (worst band " + worst.ToString("F1") + " off, limit 6)");

        // 2. Sample rate must not change the curve, only how finely it is traced.
        //
        // Pooled over six participants at every rate, and it has to be: two single sessions
        // are two different realisations of the same process, so their splits differ by the
        // ten-odd points a session rattles by however rate-independent the generator is. The
        // pooling is what makes a drift here mean the ROW RATE moved the distribution rather
        // than that the two draws landed differently.
        Func<double, double[]> pooledAt = hz =>
        {
            List<double> atRate = new List<double>();
            for (int p = 1; p <= 6; p++) atRate.AddRange(deltasFor(4200 + p, 7200 + p, hz));
            return PupilBands(atRate);
        };

        double[] atSeventeen = pooledAt(17.0);
        bool rateStable = true;
        StringBuilder rateText = new StringBuilder();
        foreach (double hz in new[] { 8.0, 30.0, 60.0, 120.0 })
        {
            double[] other = pooledAt(hz);
            double drift = 0;
            for (int b = 0; b < other.Length; b++) drift = Math.Max(drift, Math.Abs(other[b] - atSeventeen[b]));
            rateStable &= drift <= 8.0;
            rateText.Append("  " + hz.ToString("F0") + " Hz " + drift.ToString("F1"));
        }
        failures += Report(rateStable,
            "the one-second distribution barely moves when the row rate does, over six participants each" +
            " (worst band shift vs 17 Hz:" +
            rateText + ", limit 8)");

        // 3. ... while the step BETWEEN rows shrinks in proportion, which is what "smoother"
        // means. The old AR(1) model stepped the same distance however close the rows were.
        double stepAtSeventeen = MedianStep(deltasFor2(vectors, points, names, eyeOrigin, directory, 17.0));
        double stepAtSixty = MedianStep(deltasFor2(vectors, points, names, eyeOrigin, directory, 60.0));
        double ratio = stepAtSeventeen > 0 ? stepAtSixty / stepAtSeventeen : 0;
        failures += Report(ratio > 0.15 && ratio < 0.55,
            "the step between adjacent rows shrinks with the interval: " + stepAtSeventeen.ToString("F4") +
            " mm at 17 Hz vs " + stepAtSixty.ToString("F4") + " mm at 60 Hz (ratio " + ratio.ToString("F2") +
            ", the interval ratio is 0.28)");

        // 4. The baseline window. For the whole of it the pupil has to stay next to the value
        // the session opened on - that is what makes the consumer's median a baseline rather
        // than a coin toss between the two ends of a free swing - and once it closes the
        // trace has to open back up into its full range, or the squeeze never let go.
        double excursionCeiling = SimulationDefaults.PupilBaselineExcursionMillimetres + 0.01;
        double worstHold = 0.0;
        int openedUp = 0;
        List<double> afterRanges = new List<double>();
        for (int p = 1; p <= 20; p++)
        {
            SimulationSettings s = BuildSettings(vectors, points, names, eyeOrigin,
                Path.Combine(directory, "pupil-probe.csv"));
            s.Seed = 5000 + p;
            s.ParticipantSeed = 8000 + p;
            s.SessionSeconds = 300.0;
            s.BlinkRatePerSecond = 0.0;          // so row 0 is the value the squeeze is anchored to
            s.InvalidFrameRate = 0.0;
            SimulationResult r = new SessionSimulator().Generate(s);

            double opening = r.LeftPupilDiametres[0];
            double insideWindow = 0.0, afterWindow = 0.0;
            for (int i = 0; i < r.LeftPupilDiametres.Count; i++)
            {
                double distance = Math.Abs(r.LeftPupilDiametres[i] - opening);
                if (r.SampleTimes[i] <= s.BaselineSeconds) insideWindow = Math.Max(insideWindow, distance);
                else afterWindow = Math.Max(afterWindow, distance);
            }
            worstHold = Math.Max(worstHold, insideWindow);
            afterRanges.Add(afterWindow);
            if (afterWindow > 2.0 * SimulationDefaults.PupilBaselineExcursionMillimetres) openedUp++;
        }
        failures += Report(worstHold <= excursionCeiling,
            "over the " + SimulationDefaults.BaselineSeconds.ToString("F0") +
            " s baseline window no participant moved more than " + worstHold.ToString("F3") +
            " mm from the value they opened on (ceiling " + excursionCeiling.ToString("F2") + ")");
        // 5. The driven fixations, pooled over twenty participants. Two things: every fixation
        // that started after the baseline moved one way only - away from it - from its own
        // first row to its last, and the excursion it produced is big enough to be the signal
        // the dataset is being built for rather than a rounding error.
        int drivenTotal = 0, drivenWrongWay = 0, drivenUp = 0, pinnedAtWall = 0;
        List<double> excursions = new List<double>();
        for (int p = 1; p <= 20; p++)
        {
            SimulationSettings s = BuildSettings(vectors, points, names, eyeOrigin,
                Path.Combine(directory, "pupil-probe.csv"));
            s.Seed = 5300 + p;
            s.ParticipantSeed = 8300 + p;
            s.SessionSeconds = 300.0;
            s.InvalidFrameRate = 0.0;
            SimulationResult r = new SessionSimulator().Generate(s);

            foreach (List<int> group in FixationRowGroups(r))
            {
                if (r.SampleTimes[group[0]] <= s.BaselineSeconds) continue;

                double opening = r.LeftPupilDiametres[group[0]];
                double away = opening >= r.PupilBaseline ? 1.0 : -1.0;
                double previous = opening, last = opening, worstAgainst = 0.0;
                for (int k = 1; k < group.Count; k++)
                {
                    double value = r.LeftPupilDiametres[group[k]];
                    double against = -away * (value - previous);
                    if (against > worstAgainst) worstAgainst = against;
                    previous = value;
                    last = value;
                }

                drivenTotal++;
                if (away > 0.0) drivenUp++;
                if (worstAgainst > 1e-9) drivenWrongWay++;
                if (group.Count > 1) excursions.Add(away * (last - opening));
                if (Math.Abs(last - (away > 0.0 ? r.PupilRangeMaximum : r.PupilRangeMinimum)) < 1e-6) pinnedAtWall++;
            }
        }
        failures += Report(drivenWrongWay == 0 && drivenTotal > 500,
            drivenTotal + " driven fixations over 20 participants, " + drivenWrongWay +
            " of which ever stepped back toward the baseline (must be 0); " + drivenUp +
            " dilated, " + (drivenTotal - drivenUp) + " constricted, " + pinnedAtWall +
            " ended pinned at an anatomical limit");
        double medianExcursion = MedianStep(excursions);
        failures += Report(medianExcursion > 0.0 && excursions.Count > 500,
            "the median fixation moves " + medianExcursion.ToString("F3") +
            " mm away from the baseline over its length, and none of them moves toward it (" +
            excursions.Count + " measured; negative would mean the drive is not being applied)");

        // Zero means zero. BaselineSeconds is taken literally, so a component whose input is
        // unwired or deliberately set to 0 gets no window, no hold and no driven fixations -
        // the pupil the model had before any of this existed.
        SimulationSettings off = BuildSettings(vectors, points, names, eyeOrigin,
            Path.Combine(directory, "pupil-probe.csv"));
        off.Seed = 6101;
        off.ParticipantSeed = 9101;
        off.SessionSeconds = 300.0;
        off.BlinkRatePerSecond = 0.0;
        off.InvalidFrameRate = 0.0;
        off.BaselineSeconds = 0.0;
        SimulationResult offResult = new SessionSimulator().Generate(off);
        double offSwing = 0.0;
        for (int i = 0; i < offResult.LeftPupilDiametres.Count; i++)
        {
            if (offResult.SampleTimes[i] > SimulationDefaults.BaselineSeconds) break;
            offSwing = Math.Max(offSwing,
                Math.Abs(offResult.LeftPupilDiametres[i] - offResult.LeftPupilDiametres[0]));
        }
        failures += Report(offSwing > excursionCeiling,
            "BaselineSeconds = 0 means 0: no hold at all (the first minute swung " +
            offSwing.ToString("F2") + " mm, which a hold would have stopped)");

        failures += Report(openedUp >= 18,
            openedUp + " of 20 participants then wandered further than " +
            (2.0 * SimulationDefaults.PupilBaselineExcursionMillimetres).ToString("F2") +
            " mm from it once the window closed (median " + MedianStep(afterRanges).ToString("F2") +
            " mm, limit 18 of 20)");

        // 5. The participant's range is the person's, not the session's.
        PupilAnthropometry first = PupilAnthropometry.ForParticipant(
            BatchRunner.ParticipantSeed(99, "P07"),
            new Interval(1.6, 2.0), new Interval(4.5, 6.0));
        PupilAnthropometry again = PupilAnthropometry.ForParticipant(
            BatchRunner.ParticipantSeed(99, "P07"),
            new Interval(1.6, 2.0), new Interval(4.5, 6.0));
        PupilAnthropometry someoneElse = PupilAnthropometry.ForParticipant(
            BatchRunner.ParticipantSeed(99, "P08"),
            new Interval(1.6, 2.0), new Interval(4.5, 6.0));
        failures += Report(first.Minimum == again.Minimum && first.Maximum == again.Maximum,
            "the same participant id and batch seed always give the same eye (" +
            first.Minimum.ToString("F3") + " .. " + first.Maximum.ToString("F3") + " mm)");
        failures += Report(Math.Abs(first.Minimum - someoneElse.Minimum) > 1e-9,
            "a different participant gets a different one (" + someoneElse.Minimum.ToString("F3") + " .. " +
            someoneElse.Maximum.ToString("F3") + " mm)");
        failures += Report(first.Minimum >= 1.6 && first.Minimum <= 2.0 &&
                           first.Maximum >= 4.5 && first.Maximum <= 6.0,
            "both draws land inside the domains they were given");

        Console.WriteLine(failures == 0 ? "PUPIL SIGNAL PASSED" : "PUPIL SIGNAL FAILED");
        return failures;
    }

    private static List<double> deltasFor2(List<Vector3d> vectors, List<Point3d> points,
        List<string> names, Point3d eyeOrigin, string directory, double sampleHz)
    {
        SimulationSettings s = BuildSettings(vectors, points, names, eyeOrigin,
            Path.Combine(directory, "pupil-probe.csv"));
        s.Seed = 4242; s.ParticipantSeed = 7242; s.SampleRateHz = sampleHz;
        s.BlinkRatePerSecond = 0.0; s.InvalidFrameRate = 0.0;
        s.SessionSeconds = 300.0;
        s.BaselineSeconds = 0.0;                 // the free model, for the same reason as above
        SimulationResult result = new SessionSimulator().Generate(s);

        List<double> steps = new List<double>();
        for (int i = 1; i < result.LeftPupilDiametres.Count; i++)
            steps.Add(Math.Abs(result.LeftPupilDiametres[i] - result.LeftPupilDiametres[i - 1]));
        return steps;
    }

    /// <summary>
    /// The rows of each planned fixation, in order, with the blink rows (which write -1
    /// rather than a diametre) left out. A group is returned only if it has a row at all.
    /// </summary>
    private static IEnumerable<List<int>> FixationRowGroups(SimulationResult result)
    {
        int i = 0;
        while (i < result.SampleFixationIndices.Count)
        {
            int fixation = result.SampleFixationIndices[i];
            if (fixation < 0) { i++; continue; }

            int end = i;
            while (end + 1 < result.SampleFixationIndices.Count &&
                   result.SampleFixationIndices[end + 1] == fixation) end++;

            List<int> rows = new List<int>();
            for (int k = i; k <= end; k++)
                if (result.LeftPupilDiametres[k] > 0.0) rows.Add(k);

            if (rows.Count > 0) yield return rows;
            i = end + 1;
        }
    }

    private static double MedianStep(List<double> values)
    {
        if (values.Count == 0) return 0;
        List<double> sorted = new List<double>(values);
        sorted.Sort();
        return sorted[sorted.Count / 2];
    }

    private static List<double> OneSecondPupilChanges(List<double> values, List<double> times,
        double fromSeconds)
    {
        List<double> changes = new List<double>();
        int j = 0;
        for (int i = 0; i < values.Count; i++)
        {
            if (values[i] < 0) continue;
            if (times[i] < fromSeconds) continue;
            double wanted = times[i] + 1.0;
            if (j < i) j = i;
            while (j + 1 < values.Count && Math.Abs(times[j + 1] - wanted) < Math.Abs(times[j] - wanted)) j++;
            if (values[j] < 0 || Math.Abs(times[j] - wanted) > 0.15) continue;
            changes.Add(Math.Abs(values[j] - values[i]));
        }
        return changes;
    }

    private static double[] PupilBands(List<double> changes)
    {
        double[] edges = { 0.3, 0.7, 1.0, 1.3 };
        double[] counts = new double[edges.Length + 1];
        foreach (double c in changes)
        {
            int band = edges.Length;
            for (int b = 0; b < edges.Length; b++) if (c < edges[b]) { band = b; break; }
            counts[band]++;
        }
        double[] shares = new double[counts.Length];
        if (changes.Count == 0) return shares;
        for (int b = 0; b < counts.Length; b++) shares[b] = 100.0 * counts[b] / changes.Count;
        return shares;
    }

    private static string Bands(double[] shares)
    {
        StringBuilder text = new StringBuilder();
        for (int b = 0; b < shares.Length; b++)
        {
            if (b > 0) text.Append(" / ");
            text.Append(shares[b].ToString("F1"));
        }
        return text.ToString();
    }

    /// <summary>
    /// The batch: two seat files, five participants, two periods a day. Checks the count of
    /// files, that no two sessions overlap in time, that every session sits inside its
    /// window, that the file names are unique and that slicing the batch does not change
    /// what any session produces.
    /// </summary>
    private static int CheckBatchRunner(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("STUDY PLAN");
        Console.WriteLine("---------------------------------------------------------------");

        int failures = 0;

        // -------------------------------------------------------------------------------
        //  The plan on its own. It is a pure function of StudyConfiguration, so every
        //  invariant about WHEN a session happens can be checked here without simulating
        //  a single sample.
        // -------------------------------------------------------------------------------
        string planFailure;
        List<PlannedSession> plan = BatchRunner.PlanStudy(out planFailure);
        if (plan == null)
        {
            Console.WriteLine("FAIL  " + planFailure);
            return 1;
        }

        int expectedPlan = StudyConfiguration.RoundCount * StudyConfiguration.Scenes.Length *
                           StudyConfiguration.Roster.Length;
        failures += Report(plan.Count == expectedPlan,
            StudyConfiguration.RoundCount + " rounds x " + StudyConfiguration.Scenes.Length +
            " scenes x " + StudyConfiguration.Roster.Length + " participants = " + expectedPlan +
            " planned sessions (got " + plan.Count + ")");

        // ---- the roster itself --------------------------------------------------------
        failures += Report(StudyConfiguration.Roster.Select(p => p.Order).Distinct().Count() ==
                           StudyConfiguration.Roster.Length,
            "every participant has an Order of their own");
        failures += Report(StudyConfiguration.Roster.Select(p => p.Id).Distinct().Count() ==
                           StudyConfiguration.Roster.Length,
            "no participant is listed twice");

        int morning = StudyConfiguration.Roster.Count(p => p.DayPeriod == "Dia");
        int night = StudyConfiguration.Roster.Count(p => p.DayPeriod == "Noite");
        failures += Report(morning == night,
            "the roster splits evenly between the periods (" + morning + " Dia / " + night + " Noite)");

        Dictionary<string, int> perSeat = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (ParticipantAssignment person in StudyConfiguration.Roster)
        {
            if (!perSeat.ContainsKey(person.Seat)) perSeat.Add(person.Seat, 0);
            perSeat[person.Seat]++;
        }
        failures += Report(perSeat.Values.All(c => c == 1 || c == 2) &&
                           perSeat.Values.Count(c => c == 1) == 2,
            "every seat carries two participants except exactly two, which carry one " +
            "(" + perSeat.Count + " seats in use)");

        // Each seat that carries two must carry one of each period, so no seat is measured
        // only in the morning or only at night.
        bool seatsAreMixed = true;
        foreach (KeyValuePair<string, int> entry in perSeat)
        {
            if (entry.Value != 2) continue;
            string[] periods = StudyConfiguration.Roster
                .Where(p => p.Seat == entry.Key).Select(p => p.DayPeriod).ToArray();
            if (periods[0] == periods[1]) seatsAreMixed = false;
        }
        failures += Report(seatsAreMixed,
            "a doubled seat is measured once in the morning and once at night, never twice in one period");

        // ---- timing -------------------------------------------------------------------
        // One participant at a time: NO two sessions of the whole study may overlap, ever.
        List<PlannedSession> byTime = plan.OrderBy(s => s.StartsAt).ToList();
        bool noOverlap = true;
        double smallestGapSeconds = double.MaxValue;
        string overlapWhere = "";
        for (int i = 1; i < byTime.Count; i++)
        {
            double gap = (byTime[i].StartsAt -
                          byTime[i - 1].StartsAt.AddSeconds(StudyConfiguration.SessionSeconds)).TotalSeconds;
            if (gap < -1e-9)
            {
                noOverlap = false;
                if (overlapWhere.Length == 0)
                    overlapWhere = byTime[i - 1].ParticipantId + " and " + byTime[i].ParticipantId +
                        " at " + byTime[i].StartsAt.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture);
            }
            if (byTime[i].StartsAt.Date == byTime[i - 1].StartsAt.Date && gap < smallestGapSeconds)
                smallestGapSeconds = gap;
        }
        failures += Report(noOverlap,
            "no two sessions of the whole study overlap - only one participant is ever in the room" +
            (overlapWhere.Length > 0 ? " (" + overlapWhere + ")" : ""));
        failures += Report(smallestGapSeconds >= StudyConfiguration.IntervalMinutes * 60.0 - 1e-6,
            "consecutive sessions of a day are at least " + StudyConfiguration.IntervalMinutes +
            " min apart (smallest was " + (smallestGapSeconds / 60.0).ToString("F1") + " min)");

        bool insideWindows = true;
        bool periodMatchesWindow = true;
        foreach (PlannedSession session in plan)
        {
            DateTime end = session.StartsAt.AddSeconds(StudyConfiguration.SessionSeconds);
            DayPeriodWindow window;
            if (!StudyConfiguration.TryFindPeriod(session.DayPeriod, out window)) { periodMatchesWindow = false; continue; }
            if (session.StartsAt.Hour < window.StartHour) insideWindows = false;
            if (end > session.StartsAt.Date.AddHours(window.EndHour)) insideWindows = false;
        }
        failures += Report(insideWindows && periodMatchesWindow,
            "every session starts and ends inside the window of its own period");

        // ---- the participant's place in the queue is fixed --------------------------
        // Within one scene of one round, the roster order IS the order of the day.
        bool orderHolds = true;
        foreach (IGrouping<string, PlannedSession> group in plan
            .GroupBy(s => s.Round + "|" + s.Scene + "|" + s.DayPeriod))
        {
            List<PlannedSession> ordered = group.OrderBy(s => s.Person.Order).ToList();
            for (int i = 1; i < ordered.Count; i++)
                if (ordered[i].StartsAt <= ordered[i - 1].StartsAt) orderHolds = false;
        }
        failures += Report(orderHolds,
            "inside a scene, a lower Order always starts earlier - the queue is the roster order");

        // ---- a scene change opens a new day -----------------------------------------
        bool scenesDoNotShareDays = true;
        string sharedDay = "";
        foreach (IGrouping<int, PlannedSession> round in plan.GroupBy(s => s.Round))
        {
            Dictionary<string, string> sceneOfDay = new Dictionary<string, string>(StringComparer.Ordinal);
            foreach (PlannedSession session in round)
            {
                string day = session.StartsAt.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture);
                if (!sceneOfDay.ContainsKey(day)) { sceneOfDay.Add(day, session.Scene); continue; }
                if (sceneOfDay[day] != session.Scene)
                {
                    scenesDoNotShareDays = false;
                    if (sharedDay.Length == 0)
                        sharedDay = day + " holds both '" + sceneOfDay[day] + "' and '" + session.Scene + "'";
                }
            }
        }
        failures += Report(scenesDoNotShareDays,
            "a scene change starts a new day - no day carries two scenes" +
            (sharedDay.Length > 0 ? " (" + sharedDay + ")" : ""));

        // ---- rounds are a month apart and never interleave --------------------------
        bool roundsSeparated = true;
        bool roundsOneMonthApart = true;
        DateTime previousRoundEnd = DateTime.MinValue;
        DateTime firstRoundStart = plan.Where(s => s.Round == 0).Min(s => s.StartsAt).Date;
        for (int round = 0; round < StudyConfiguration.RoundCount; round++)
        {
            DateTime start = plan.Where(s => s.Round == round).Min(s => s.StartsAt).Date;
            DateTime end = plan.Where(s => s.Round == round).Max(s => s.StartsAt).Date;
            if (start <= previousRoundEnd) roundsSeparated = false;
            if (start != firstRoundStart.AddMonths(round * StudyConfiguration.MonthsBetweenRounds))
                roundsOneMonthApart = false;
            previousRoundEnd = end;
        }
        failures += Report(roundsSeparated, "no round starts before the one before it has finished");
        failures += Report(roundsOneMonthApart,
            "round n opens exactly " + StudyConfiguration.MonthsBetweenRounds +
            " month(s) after round n-1, on the same day of the month");

        // Every participant runs every scene exactly once per round.
        bool everyoneRunsEverything = plan
            .GroupBy(s => s.ParticipantId + "|" + s.Scene + "|" + s.Round)
            .All(g => g.Count() == 1) &&
            plan.GroupBy(s => s.ParticipantId).All(g =>
                g.Count() == StudyConfiguration.RoundCount * StudyConfiguration.Scenes.Length);
        failures += Report(everyoneRunsEverything,
            "each participant runs each scene once per round, " +
            (StudyConfiguration.RoundCount * StudyConfiguration.Scenes.Length) + " sessions each");

        // -------------------------------------------------------------------------------
        //  Now the files. Only a handful of sessions are actually simulated - the point
        //  here is the layout, the environment CSV and reproducibility, not throughput.
        // -------------------------------------------------------------------------------
        Console.WriteLine();
        Console.WriteLine("BATCH RUNNER");
        Console.WriteLine("---------------------------------------------------------------");

        string root = Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)), "batch-test");
        if (Directory.Exists(root)) Directory.Delete(root, true);
        // NOT created: the runner has to make it itself.

        // A tenth of the star: the sessions are the point of this check, not how long
        // each one takes.
        List<Vector3d> vectors = new List<Vector3d>();
        List<Point3d> points = new List<Point3d>();
        List<string> names = new List<string>();
        for (int i = 0; i < starVectors.Count; i += 10)
        {
            vectors.Add(starVectors[i]);
            points.Add(starHitPoints[i]);
            names.Add(starObjectNames[i]);
        }

        // One seat file per (scene, seat) of the study, named the way the raycast writes
        // them, plus a file that is not a seat of the study and must never be read.
        Vector3d forward = new Vector3d(0, 0, 1);
        string seatFolder = Path.Combine(root, "seats");
        Directory.CreateDirectory(seatFolder);
        int nearest = NearestStarIndex(vectors, forward);
        foreach (string scene in StudyConfiguration.Scenes)
            foreach (string seat in StudyConfiguration.Roster.Select(p => p.Seat).Distinct())
                WriteSeatJson(Path.Combine(seatFolder, StudyConfiguration.SeatFileName(scene, seat)),
                    scene, seat, eyeOrigin, forward, 30.0, vectors, points, names, nearest);
        WriteSeatJson(Path.Combine(seatFolder, "99_Z9.json"), "99", "Z9", eyeOrigin, forward, 30.0,
            vectors, points, names, nearest);

        List<string> climate = BuildClimateRows();

        // Two participants of the same seat - one morning, one night - so the pair that
        // shares a seat is covered, in one scene of one round.
        string sharedSeat = perSeat.First(e => e.Value == 2).Key;
        List<string> pair = StudyConfiguration.Roster.Where(p => p.Seat == sharedSeat)
            .OrderBy(p => p.Order).Select(p => p.Id).ToList();

        BatchResult batch = RunBatch(seatFolder, root, climate, pair,
            new List<string> { StudyConfiguration.Scenes[0] }, new List<int> { 1 });

        if (!batch.Succeeded)
        {
            Console.WriteLine("FAIL  " + batch.FailureMessage);
            Console.WriteLine(batch.ReportText);
            return failures + 1;
        }

        failures += Report(batch.CsvPaths.Count == 2 && batch.EnvironmentCsvPaths.Count == 2,
            "2 participants x 1 scene x 1 round = 2 gaze CSVs and 2 environment CSVs " +
            "(got " + batch.CsvPaths.Count + " and " + batch.EnvironmentCsvPaths.Count + ")");
        failures += Report(batch.CsvPaths.All(File.Exists) && batch.EnvironmentCsvPaths.All(File.Exists),
            "the runner created every folder it needed - none had to exist beforehand");
        failures += Report(!batch.ReportText.Contains("99_Z9"),
            "a JSON in the folder that is not a seat of the study is never read");

        // Folder layout: OutputFolder / "RODADA 01" / "CENÁRIO 01" / "HTC" or "AMBIENTE".
        string expectedGazeFolder = SessionNaming.BuildGazeFolder(root, 0, StudyConfiguration.Scenes[0]);
        string expectedEnvironmentFolder = SessionNaming.BuildEnvironmentFolder(root, 0, StudyConfiguration.Scenes[0]);
        failures += Report(
            batch.CsvPaths.All(p => Path.GetDirectoryName(p) == expectedGazeFolder) &&
            batch.EnvironmentCsvPaths.All(p => Path.GetDirectoryName(p) == expectedEnvironmentFolder),
            "the gaze files land in " + StudyConfiguration.RoundFolderName(0) + "/" +
            StudyConfiguration.SceneFolderName(StudyConfiguration.Scenes[0]) + "/" +
            StudyConfiguration.GazeFolderName + " and the environment files in .../" +
            StudyConfiguration.EnvironmentFolderName);
        failures += Report(Directory.Exists(expectedGazeFolder) && Directory.Exists(expectedEnvironmentFolder),
            "all four levels of the tree were created by the run, none of them by hand");

        // ---- the headline requirement -------------------------------------------------
        // Running ONE participant must reproduce, byte for byte, the file the two-participant
        // run produced - same path, same timestamp, same content.
        BatchResult single = RunBatch(seatFolder, root, climate, new List<string> { pair[1] },
            new List<string> { StudyConfiguration.Scenes[0] }, new List<int> { 1 });
        int inPair = batch.Sessions.FindIndex(s => s.Session.ParticipantId == pair[1]);
        bool reproduced = single.Succeeded && single.CsvPaths.Count == 1 && inPair >= 0 &&
            single.CsvPaths[0] == batch.CsvPaths[inPair] &&
            single.TimeStamps[0] == batch.TimeStamps[inPair];
        failures += Report(reproduced,
            "re-running one participant on its own keeps the path and the timestamp it had " +
            "in the full run - the second of the pair does not slide into the first slot");

        // ---- the environment CSV ------------------------------------------------------
        failures += CheckEnvironmentCsv(batch.CsvPaths[0], batch.EnvironmentCsvPaths[0]);

        // Two participants of the same seat must not be the same walk with a different name.
        failures += Report(!string.Equals(File.ReadAllText(batch.CsvPaths[0]),
                                          File.ReadAllText(batch.CsvPaths[1]), StringComparison.Ordinal),
            "two participants on the same seat get different gaze sessions");
        failures += Report(!string.Equals(File.ReadAllText(batch.EnvironmentCsvPaths[0]),
                                          File.ReadAllText(batch.EnvironmentCsvPaths[1]), StringComparison.Ordinal),
            "two participants on the same seat get different environment readings");

        // A round is a different measurement, not a copy of the first one.
        BatchResult secondRound = RunBatch(seatFolder, root, climate, new List<string> { pair[0] },
            new List<string> { StudyConfiguration.Scenes[0] }, new List<int> { 2 });
        failures += Report(secondRound.Succeeded &&
            !string.Equals(File.ReadAllText(secondRound.CsvPaths[0]),
                           File.ReadAllText(batch.CsvPaths[0]), StringComparison.Ordinal),
            "round 2 is a fresh simulation, not round 1 written to another folder");
        failures += Report(secondRound.Succeeded &&
            Path.GetDirectoryName(secondRound.CsvPaths[0]) ==
                SessionNaming.BuildGazeFolder(root, 1, StudyConfiguration.Scenes[0]),
            "round 2 writes into " + StudyConfiguration.RoundFolderName(1));

        // ---- the pupil range still belongs to the person ------------------------------
        PupilAnthropometry first = PupilAnthropometry.ForParticipant(
            BatchRunner.ParticipantSeed(4711, pair[0]),
            new Interval(SimulationDefaults.PupilMinimumDomainLow, SimulationDefaults.PupilMinimumDomainHigh),
            new Interval(SimulationDefaults.PupilMaximumDomainLow, SimulationDefaults.PupilMaximumDomainHigh));
        PupilAnthropometry second = PupilAnthropometry.ForParticipant(
            BatchRunner.ParticipantSeed(4711, pair[1]),
            new Interval(SimulationDefaults.PupilMinimumDomainLow, SimulationDefaults.PupilMinimumDomainHigh),
            new Interval(SimulationDefaults.PupilMaximumDomainLow, SimulationDefaults.PupilMaximumDomainHigh));
        failures += Report(Math.Abs(first.Minimum - second.Minimum) > 1e-9,
            "two participants draw two different pupil ranges, not one shared default");

        // ---- bad selections say what is wrong -----------------------------------------
        BatchResult unknown = RunBatch(seatFolder, root, climate, new List<string> { "0000" },
            new List<string> { StudyConfiguration.Scenes[0] }, new List<int> { 1 });
        failures += Report(!unknown.Succeeded && unknown.FailureMessage.Contains("0000"),
            "a participant who is not in the roster is named in the failure, not silently ignored");

        BatchResult noClimate = RunBatch(seatFolder, root, new List<string>(), pair,
            new List<string> { StudyConfiguration.Scenes[0] }, new List<int> { 1 });
        failures += Report(!noClimate.Succeeded && noClimate.FailureMessage.Contains("ClimateTable"),
            "an unwired climate table fails the batch instead of writing half of it");

        // The determinism check costs a second full simulation, so a batch runs it on its
        // first session only. The report carries the full text of the PREVIEWED session, so
        // proving the skip means previewing a session that is not the first.
        failures += Report(batch.ReportText.Contains("16 re-running with the same seed produced"),
            "the first session of a batch really is checked for determinism");
        BatchResult previewLater = RunBatch(seatFolder, root, climate, pair,
            new List<string> { StudyConfiguration.Scenes[0] }, new List<int> { 1 },
            batch.Sessions[1].Session.Index);
        failures += Report(previewLater.ReportText.Contains("SKIP  16"),
            "a later session reports the determinism check as skipped, not as passed");

        Console.WriteLine(failures == 0 ? "BATCH RUNNER PASSED" : "BATCH RUNNER FAILED");
        return failures;
    }

    /// <summary>
    /// The environment file of a session: it has to line up with the gaze file row for row,
    /// and every signal has to stay inside the limits StudyConfiguration states.
    /// </summary>
    private static int CheckEnvironmentCsv(string gazePath, string environmentPath)
    {
        int failures = 0;

        string[] gazeRows = File.ReadAllLines(gazePath).Where(r => r.Length > 0).ToArray();
        string[] environmentRows = File.ReadAllLines(environmentPath).Where(r => r.Length > 0).ToArray();

        failures += Report(environmentRows.Length == gazeRows.Length,
            "the environment CSV has one row per gaze row (" + (environmentRows.Length - 1) +
            " vs " + (gazeRows.Length - 1) + " data rows)");
        failures += Report(environmentRows[0] == EnvironmentCsvWriter.HeaderLine,
            "the environment header is the agreed one, accents included");

        // Row i of one file and row i of the other describe the same instant.
        bool timestampsAlign = true;
        List<DateTime> instants = new List<DateTime>();
        List<double> temperatures = new List<double>();
        List<double> humidities = new List<double>();
        List<double> noises = new List<double>();
        bool coIsBinary = true, decimalsAreTwo = true, fixedColumnsHold = true;

        for (int i = 1; i < environmentRows.Length && i < gazeRows.Length; i++)
        {
            string[] environment = EnvironmentCsvWriter.SplitDataLine(environmentRows[i]);
            string[] gaze = SessionReport.ParseQuotedRow(gazeRows[i]);
            if (environment.Length != EnvironmentCsvWriter.ColumnCount) { fixedColumnsHold = false; continue; }
            if (environment[0] != gaze[0]) timestampsAlign = false;

            if (environment[1] != StudyConfiguration.EnvironmentDeviceName ||
                environment[2] != StudyConfiguration.EnvironmentNetworkDns ||
                environment[3] != "" ||
                environment[4] != StudyConfiguration.EnvironmentMacAddress ||
                environment[5] != "" ||
                environment[6] != StudyConfiguration.EnvironmentPowerState) fixedColumnsHold = false;

            instants.Add(DateTime.ParseExact(environment[0], "yyyy-MM-dd HH:mm:ss.fff",
                CultureInfo.InvariantCulture));
            temperatures.Add(ReadMeasurement(environment[7]));
            humidities.Add(ReadMeasurement(environment[8]));
            noises.Add(ReadMeasurement(environment[10]));

            if (environment[9] != "0" && environment[9] != "1") coIsBinary = false;
            foreach (int column in new[] { 7, 8, 10 })
            {
                int point = environment[column].IndexOf(StudyConfiguration.EnvironmentDecimalSeparator,
                    StringComparison.Ordinal);
                if (point < 0 || environment[column].Length - point -
                    StudyConfiguration.EnvironmentDecimalSeparator.Length != 2) decimalsAreTwo = false;
            }
        }

        // The whole point of the dialect: a reader that splits on the configured delimiter
        // and parses with the configured decimal separator must see 11 typed cells. Quoting
        // a measurement would make it text in Excel however the locale is set.
        string[] firstDataRow = EnvironmentCsvWriter.SplitDataLine(environmentRows[1]);
        string rawFirstDataRow = environmentRows[1];
        failures += Report(firstDataRow.Length == EnvironmentCsvWriter.ColumnCount,
            "a reader splitting on '" + EnvironmentCsvWriter.Delimiter + "' gets " +
            EnvironmentCsvWriter.ColumnCount + " cells");
        failures += Report(
            rawFirstDataRow.Split(new[] { EnvironmentCsvWriter.Delimiter }, StringSplitOptions.None)
                .Skip(7).All(cell => cell.IndexOf('"') < 0),
            "the four measurement columns are written bare, so a spreadsheet types them as numbers");
        failures += Report(
            rawFirstDataRow.Split(new[] { EnvironmentCsvWriter.Delimiter }, StringSplitOptions.None)
                .Take(7).All(cell => cell.StartsWith("\"", StringComparison.Ordinal)),
            "the seven textual columns are quoted, so a MAC address is never read as a time");

        byte[] leadingBytes = File.ReadAllBytes(environmentPath).Take(3).ToArray();
        bool hasByteOrderMark = leadingBytes.Length == 3 &&
            leadingBytes[0] == 0xEF && leadingBytes[1] == 0xBB && leadingBytes[2] == 0xBF;
        failures += Report(hasByteOrderMark == StudyConfiguration.EnvironmentByteOrderMark,
            "the environment CSV " + (StudyConfiguration.EnvironmentByteOrderMark ? "opens with" : "has no") +
            " a UTF-8 byte order mark, so Excel reads ALIMENTACAO and RUIDO with their accents");

        byte[] gazeLeadingBytes = File.ReadAllBytes(gazePath).Take(3).ToArray();
        failures += Report(!(gazeLeadingBytes.Length == 3 && gazeLeadingBytes[0] == 0xEF &&
                             gazeLeadingBytes[1] == 0xBB && gazeLeadingBytes[2] == 0xBF),
            "the gaze CSV still has NO byte order mark - a real capture has none and the " +
            "reader looks for 'timeStamp' in the first cell");

        failures += Report(timestampsAlign,
            "row i of the environment CSV carries the same timestamp as row i of the gaze CSV");
        failures += Report(fixedColumnsHold,
            "DISPOSITIVO, DNS REDE, MAC ADDRESS and ALIMENTACAO are the agreed constants, " +
            "and TEMPO ATIVIDADE and RESET are empty on every row");
        failures += Report(coIsBinary, "CO is only ever 0 or 1");
        failures += Report(decimalsAreTwo,
            "temperature, humidity and noise are all written to exactly two decimals");

        // The rows are newest first, so walking them backwards walks forwards in time.
        //
        // The comparison is on the PRINTED values, which carry two decimals, so each one sits
        // within 0.005 of the value the walk actually held and a difference of two of them
        // within 0.01. Over the ~0.05 s between samples that quantum alone is worth 12 units
        // a minute, which would swamp the limit being checked - so the allowance is stated as
        // one rounding unit on the difference, not as a rate.
        const double RoundingUnit = 0.01;
        double worstTemperatureExcess = 0.0, worstHumidityExcess = 0.0;
        double worstTemperatureRate = 0.0, worstHumidityRate = 0.0;
        for (int i = instants.Count - 1; i > 0; i--)
        {
            double minutes = (instants[i - 1] - instants[i]).TotalMinutes;
            if (minutes <= 1e-9) continue;

            double temperatureStep = Math.Abs(temperatures[i - 1] - temperatures[i]);
            double humidityStep = Math.Abs(humidities[i - 1] - humidities[i]);

            worstTemperatureExcess = Math.Max(worstTemperatureExcess, temperatureStep -
                (StudyConfiguration.TemperatureMaxChangePerMinuteC * minutes + RoundingUnit));
            worstHumidityExcess = Math.Max(worstHumidityExcess, humidityStep -
                (StudyConfiguration.HumidityMaxChangePerMinutePercent * minutes + RoundingUnit));

            // Reported over a whole minute, where the rounding quantum is negligible, so the
            // number in the message is the one the requirement is written in.
            if (minutes >= 1.0 / 60.0)
            {
                worstTemperatureRate = Math.Max(worstTemperatureRate, temperatureStep / minutes);
                worstHumidityRate = Math.Max(worstHumidityRate, humidityStep / minutes);
            }
        }
        failures += Report(worstTemperatureExcess <= 1e-9,
            "temperature never moves faster than " + StudyConfiguration.TemperatureMaxChangePerMinuteC +
            " C/min (worst step overshot its budget by " + worstTemperatureExcess.ToString("F6") + " C)");
        failures += Report(worstHumidityExcess <= 1e-9,
            "humidity never moves faster than " + StudyConfiguration.HumidityMaxChangePerMinutePercent +
            " %/min (worst step overshot its budget by " + worstHumidityExcess.ToString("F6") + " %)");

        // The same limits again, end to end: how far the signal actually travelled over the
        // whole session against how far it was allowed to.
        double sessionMinutes = (instants[0] - instants[instants.Count - 1]).TotalMinutes;
        failures += Report(
            (temperatures.Max() - temperatures.Min()) <=
                StudyConfiguration.TemperatureMaxChangePerMinuteC * sessionMinutes + RoundingUnit &&
            (humidities.Max() - humidities.Min()) <=
                StudyConfiguration.HumidityMaxChangePerMinutePercent * sessionMinutes + RoundingUnit,
            "over the whole session temperature moved " +
            (temperatures.Max() - temperatures.Min()).ToString("F2") + " C and humidity " +
            (humidities.Max() - humidities.Min()).ToString("F2") + " %, both inside their budget");
        // The shape, on the values a researcher actually opens. The file is newest first, so
        // reversing it walks forwards in time, and the two decimals are what make the flat
        // stretches flat - a turn is only a turn once the printed number changes direction.
        List<double> temperatureForward = new List<double>(temperatures);
        List<double> humidityForward = new List<double>(humidities);
        temperatureForward.Reverse();
        humidityForward.Reverse();
        int temperatureTurns = CountTurns(temperatureForward);
        int humidityTurns = CountTurns(humidityForward);
        failures += Report(temperatureTurns <= 2 && humidityTurns <= 2,
            "temperature and humidity change direction at most twice in the whole file (" +
            temperatureTurns + " and " + humidityTurns + " turns over " +
            temperatures.Count + " rows)");

        failures += Report(humidities.All(h => h >= StudyConfiguration.HumidityFloorPercent - 1e-9 &&
                                               h <= StudyConfiguration.HumidityCeilingPercent + 1e-9),
            "humidity stays inside " + StudyConfiguration.HumidityFloorPercent + "-" +
            StudyConfiguration.HumidityCeilingPercent + "% (observed " +
            humidities.Min().ToString("F2") + "-" + humidities.Max().ToString("F2") + ")");
        failures += Report(noises.All(n => n <= StudyConfiguration.NoiseAbsoluteCeilingDb + 1e-9 &&
                                           n >= StudyConfiguration.NoiseAbsoluteFloorDb - 1e-9),
            "noise stays inside " + StudyConfiguration.NoiseAbsoluteFloorDb + "-" +
            StudyConfiguration.NoiseAbsoluteCeilingDb + " dB (observed " +
            noises.Min().ToString("F2") + "-" + noises.Max().ToString("F2") + ")");

        // Loud is rarer than quiet: the noise distribution has to have a tail, not a plateau.
        double quiet = noises.Count(n => n <= 65.0) / (double) noises.Count;
        double loud = noises.Count(n => n >= 75.0) / (double) noises.Count;
        failures += Report(quiet > 0.80 && loud < quiet,
            "noise sits in the quiet band most of the time (" + (quiet * 100.0).ToString("F1") +
            "% at or under 65 dB, " + (loud * 100.0).ToString("F1") + "% at or over 75 dB)");

        double onFraction = 0.0;
        // CO is an even coin, so roughly half the rows carry a 1.
        int ones = 0;
        for (int i = 1; i < environmentRows.Length; i++)
        {
            string[] cells = EnvironmentCsvWriter.SplitDataLine(environmentRows[i]);
            if (cells.Length == EnvironmentCsvWriter.ColumnCount && cells[9] == "1") ones++;
        }
        onFraction = ones / (double) (environmentRows.Length - 1);
        failures += Report(onFraction > 0.45 && onFraction < 0.55,
            "CO is an even coin (" + (onFraction * 100.0).ToString("F1") + "% of rows are 1)");

        return failures;
    }

    /// <summary>
    /// Reads a measurement back out of the environment CSV, through the same decimal
    /// separator the writer used.
    /// </summary>
    private static double ReadMeasurement(string cell)
    {
        return double.Parse(
            cell.Replace(StudyConfiguration.EnvironmentDecimalSeparator, "."),
            CultureInfo.InvariantCulture);
    }

    /// <summary>
    /// A climate table covering every day of the year. The real one is raw data held in a
    /// Python component; this one only has to be well formed and inside the stated bands.
    /// </summary>
    private static List<string> BuildClimateRows()
    {
        List<string> rows = new List<string>(366);
        for (int dayOfYear = 1; dayOfYear <= 366; dayOfYear++)
        {
            double temperatureDay = 24.0 + 4.0 * Math.Sin(dayOfYear / 366.0 * 2.0 * Math.PI);
            double temperatureNight = temperatureDay - 7.0;
            double humidityDay = 60.0 + 8.0 * Math.Cos(dayOfYear / 366.0 * 2.0 * Math.PI);
            double humidityNight = Math.Min(85.0, humidityDay + 9.0);
            rows.Add(string.Format(CultureInfo.InvariantCulture, "{0},{1:F2},{2:F2},{3:F2},{4:F2}",
                dayOfYear, temperatureDay, temperatureNight, humidityDay, humidityNight));
        }
        return rows;
    }

    private static BatchResult RunBatch(string seatFolder, string outputFolder, List<string> climate,
        List<string> participants, List<string> scenes, List<int> rounds, int previewIndex = -1)
    {
        SimulationSettings template = BuildSettings(new List<Vector3d>(), new List<Point3d>(),
            new List<string>(), new Point3d(0, 0, 0), "");
        template.SessionSeconds = StudyConfiguration.SessionSeconds;
        template.SampleRateHz = StudyConfiguration.SampleRateHz;

        BatchRequest request = new BatchRequest();
        request.Template = template;
        request.JsonFolder = seatFolder;
        request.SelectedParticipantIds = participants;
        request.SelectedScenes = scenes;
        request.SelectedRounds = rounds;
        request.ClimateRows = climate;
        request.PreviewIndex = previewIndex;
        request.OutputFolder = outputFolder;
        request.BaseSeed = 4711;

        return new BatchRunner().Run(request);
    }


    /// <summary>
    /// The component reads the seat JSON itself. This writes one exactly the way the
    /// GhPython raycast script does - Python's json.dump, so \uXXXX for every accent, nulls
    /// for the rays that hit nothing - reads it back, and requires the session generated
    /// from the file to be byte-identical to the one generated from the same star handed in
    /// as lists.
    /// </summary>
    private static int CheckSeatJsonReader(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("SEAT JSON READER");
        Console.WriteLine("---------------------------------------------------------------");

        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        string jsonPath = Path.Combine(directory, "seat-A1.json");
        const double RayLength = 30.0;
        const string SceneName = "Cenário Único";        // accents and a space, on purpose
        const string SeatName = "A1";

        // Every third ray misses, exactly as the raycast script writes them.
        List<string> namesOnDisk = new List<string>(starObjectNames.Count);
        for (int i = 0; i < starObjectNames.Count; i++)
            namesOnDisk.Add(i % 3 == 0 ? null : starObjectNames[i]);
        int expectedHits = namesOnDisk.Count(n => n != null);

        Vector3d forward = new Vector3d(0, 0, 1);
        WriteSeatJson(jsonPath, SceneName, SeatName, eyeOrigin, forward, RayLength,
            starVectors, starHitPoints, namesOnDisk, NearestStarIndex(starVectors, forward));

        int failures = 0;
        string readFailure;
        SeatGeometry seat = SeatJsonReader.Load(jsonPath, out readFailure);
        if (seat == null)
        {
            Console.WriteLine("FAIL  " + readFailure);
            return 1;
        }

        failures += Report(seat.SceneName == SceneName && seat.SeatName == SeatName,
            "scene and seat names decode through Python's \\uXXXX escapes (got '" + seat.SceneName +
            "', '" + seat.SeatName + "')");
        failures += Report(seat.Vectors.Count == starVectors.Count &&
                           seat.HitPoints.Count == starVectors.Count &&
                           seat.ObjectNames.Count == starVectors.Count,
            "the three lists come back parallel and complete (" + seat.Vectors.Count + " rays)");
        failures += Report(seat.HitCount == expectedHits,
            "hits and misses are counted (" + seat.HitCount + " hit, " + seat.MissCount + " missed)");

        bool geometryMatches = true;
        bool missesMarked = true;
        bool missesPlacedOnTheRay = true;
        for (int i = 0; i < starVectors.Count && geometryMatches && missesMarked && missesPlacedOnTheRay; i++)
        {
            geometryMatches &= Math.Abs(seat.Vectors[i].X - starVectors[i].X) < 1e-9 &&
                               Math.Abs(seat.Vectors[i].Y - starVectors[i].Y) < 1e-9 &&
                               Math.Abs(seat.Vectors[i].Z - starVectors[i].Z) < 1e-9;
            if (namesOnDisk[i] == null)
            {
                missesMarked &= seat.ObjectNames[i] == "Objeto sem nome";
                double dx = seat.HitPoints[i].X - eyeOrigin.X;
                double dy = seat.HitPoints[i].Y - eyeOrigin.Y;
                double dz = seat.HitPoints[i].Z - eyeOrigin.Z;
                missesPlacedOnTheRay &= Math.Abs(Math.Sqrt(dx * dx + dy * dy + dz * dz) - RayLength) < 1e-6;
            }
            else
            {
                missesMarked &= seat.ObjectNames[i] == starObjectNames[i];
                geometryMatches &= Math.Abs(seat.HitPoints[i].X - starHitPoints[i].X) < 1e-6 &&
                                   Math.Abs(seat.HitPoints[i].Y - starHitPoints[i].Y) < 1e-6 &&
                                   Math.Abs(seat.HitPoints[i].Z - starHitPoints[i].Z) < 1e-6;
            }
        }
        failures += Report(geometryMatches, "vectors and hit points survive the round trip through the file");
        failures += Report(missesMarked, "a null point or a null name becomes the InvalidObjectName marker");
        failures += Report(missesPlacedOnTheRay,
            "a missed ray's placeholder point sits at ray_length from the origin, not at (0,0,0)");

        // The whole point of reading the file: the session must be the one the canvas-fed
        // component would have produced from the same star.
        List<Point3d> pointsFromLists = new List<Point3d>(starHitPoints);
        List<string> namesFromLists = new List<string>(starObjectNames.Count);
        for (int i = 0; i < starObjectNames.Count; i++)
        {
            if (namesOnDisk[i] != null) { namesFromLists.Add(starObjectNames[i]); continue; }
            namesFromLists.Add("Objeto sem nome");
            Vector3d unit = starVectors[i];
            double length = Math.Sqrt(unit.X * unit.X + unit.Y * unit.Y + unit.Z * unit.Z);
            pointsFromLists[i] = new Point3d(eyeOrigin.X + unit.X / length * RayLength,
                                             eyeOrigin.Y + unit.Y / length * RayLength,
                                             eyeOrigin.Z + unit.Z / length * RayLength);
        }

        string fromListsPath = Path.Combine(directory, "seat-from-lists.csv");
        string fromJsonPath = Path.Combine(directory, "seat-from-json.csv");

        SimulationSettings fromLists = BuildSettings(starVectors, pointsFromLists, namesFromLists, eyeOrigin, fromListsPath);
        fromLists.StartVectorIndex = seat.NearestIndex;
        fromLists.Seed = 31337;
        SimulationResult listsResult = new SessionSimulator().Generate(fromLists);

        SimulationSettings fromJson = BuildSettings(seat.Vectors, seat.HitPoints, seat.ObjectNames, seat.Origin, fromJsonPath);
        fromJson.NeutralForward = seat.Forward;
        fromJson.StartVectorIndex = seat.NearestIndex;
        fromJson.Seed = 31337;
        SimulationResult jsonResult = new SessionSimulator().Generate(fromJson);

        if (!listsResult.Succeeded || !jsonResult.Succeeded)
        {
            Console.WriteLine("FAIL  one of the two runs did not complete: " +
                              listsResult.FailureMessage + jsonResult.FailureMessage);
            failures++;
        }
        else
        {
            failures += Report(File.ReadAllBytes(fromListsPath).SequenceEqual(File.ReadAllBytes(fromJsonPath)),
                "a session read from the JSON is byte-identical to the same star passed in as lists");
        }

        // Naming: the file name and the CSV columns are composed in one place.
        failures += Report(
            SessionNaming.BuildSceneName("P01", "Cenario1", "A1", "Dia") == "P01_Cenario1_A1_Dia" &&
            SessionNaming.BuildSessionName("Cenario1", "Noite") == "Cenário_Cenario1_Noite",
            "SessionNaming composes SceneName and SessionName in the agreed format");

        // The cache must notice a rewritten file rather than serve the old parse.
        SeatGeometry cachedOnce = SeatJsonCache.Load(jsonPath, out readFailure);
        SeatGeometry cachedTwice = SeatJsonCache.Load(jsonPath, out readFailure);
        failures += Report(ReferenceEquals(cachedOnce, cachedTwice),
            "the cache serves the same parse for a file that has not changed");

        System.Threading.Thread.Sleep(1100);              // NTFS write-time granularity
        WriteSeatJson(jsonPath, "Outro Cenário", "B2", eyeOrigin, forward, RayLength,
            starVectors, starHitPoints, namesOnDisk, 0);
        SeatGeometry afterRewrite = SeatJsonCache.Load(jsonPath, out readFailure);
        failures += Report(afterRewrite != null && afterRewrite.SeatName == "B2",
            "the cache is invalidated when the file is rewritten");

        // Malformed input must be a readable message, not an exception out of the component.
        string brokenPath = Path.Combine(directory, "seat-broken.json");
        File.WriteAllText(brokenPath, "{\"scene_name\": \"x\", \"rays\": [{\"index\": 0, \"vector\": [1,2]}]}");
        SeatGeometry broken = SeatJsonReader.Load(brokenPath, out readFailure);
        failures += Report(broken == null && !string.IsNullOrEmpty(readFailure) && readFailure.Contains("line"),
            "a malformed file fails with a message that points at the line");

        SeatGeometry missing = SeatJsonReader.Load(Path.Combine(directory, "does-not-exist.json"), out readFailure);
        failures += Report(missing == null && readFailure.Contains("not found"),
            "a missing file fails with a readable message");

        // A List-access input on the raycast component writes the name as a one-element
        // array. Real files in the wild have "scene_name": ["01"], and re-running the
        // raycast over every seat to fix the spelling is not a reasonable price.
        string wrappedPath = Path.Combine(directory, "seat-wrapped-names.json");
        File.WriteAllText(wrappedPath,
            "{\"rays\": [" +
            "{\"index\": 0, \"vector\": [0,0,1], \"point\": [0,0,4], \"name\": \"Palco\"}," +
            "{\"index\": 1, \"vector\": [0,0.2,1], \"point\": [0,1,4], \"name\": [\"Teto\"]}," +
            "{\"index\": 2, \"vector\": [0,-0.2,1], \"point\": null, \"name\": null}]," +
            "\"scene_name\": [\"01\"], \"seat_name\": \"Cadeira_05\"," +
            "\"origin\": [0,1.2,0], \"forward_vector\": [0,0,1], \"ray_length\": 30.0," +
            "\"nearest_index\": 0}", new UTF8Encoding(false));
        SeatGeometry wrapped = SeatJsonReader.Load(wrappedPath, out readFailure);
        failures += Report(wrapped != null && wrapped.SceneName == "01" && wrapped.SeatName == "Cadeira_05",
            "a name written as a one-element array is unwrapped (got '" +
            (wrapped == null ? readFailure : wrapped.SceneName + "', '" + wrapped.SeatName) + "')");
        failures += Report(wrapped != null && wrapped.HitCount == 2 &&
                           wrapped.ObjectNames[1] == "Teto" &&
                           wrapped.ObjectNames[2] == "Objeto sem nome",
            "the same unwrapping applies to a ray's object name, and a null name still misses");

        Console.WriteLine(failures == 0 ? "SEAT JSON READER PASSED" : "SEAT JSON READER FAILED");
        return failures;
    }

    /// <summary>
    /// Writes a seat file the way the GhPython raycast script does: json.dump defaults, so
    /// ASCII-only with \uXXXX escapes, two-space indent, nulls for a ray that hit nothing.
    /// </summary>
    private static void WriteSeatJson(string path, string sceneName, string seatName, Point3d origin,
        Vector3d forward, double rayLength, List<Vector3d> vectors, List<Point3d> hitPoints,
        List<string> names, int nearestIndex)
    {
        StringBuilder json = new StringBuilder(vectors.Count * 160);
        json.Append("{\n");
        json.Append("  \"scene_name\": ").Append(JsonQuote(sceneName)).Append(",\n");
        json.Append("  \"seat_name\": ").Append(JsonQuote(seatName)).Append(",\n");
        json.Append("  \"origin\": ").Append(JsonTriple(origin.X, origin.Y, origin.Z)).Append(",\n");
        json.Append("  \"forward_vector\": ").Append(JsonTriple(forward.X, forward.Y, forward.Z)).Append(",\n");
        json.Append("  \"ray_length\": ").Append(JsonNumber(rayLength)).Append(",\n");
        json.Append("  \"rays\": [\n");
        for (int i = 0; i < vectors.Count; i++)
        {
            bool hit = names[i] != null;
            json.Append("    {\n");
            json.Append("      \"index\": ").Append(i.ToString(CultureInfo.InvariantCulture)).Append(",\n");
            json.Append("      \"vector\": ").Append(JsonTriple(vectors[i].X, vectors[i].Y, vectors[i].Z)).Append(",\n");
            json.Append("      \"hit\": ").Append(hit ? "true" : "false").Append(",\n");
            json.Append("      \"point\": ").Append(hit
                ? JsonTriple(hitPoints[i].X, hitPoints[i].Y, hitPoints[i].Z) : "null").Append(",\n");
            json.Append("      \"name\": ").Append(hit ? JsonQuote(names[i]) : "null").Append(",\n");
            json.Append("      \"id\": \"00000000-0000-0000-0000-000000000000\"\n");
            json.Append(i == vectors.Count - 1 ? "    }\n" : "    },\n");
        }
        json.Append("  ],\n");
        json.Append("  \"nearest_index\": ").Append(nearestIndex.ToString(CultureInfo.InvariantCulture)).Append(",\n");
        json.Append("  \"nearest_point\": null,\n");
        json.Append("  \"nearest_name\": null,\n");
        json.Append("  \"nearest_id\": null\n");
        json.Append("}");
        File.WriteAllText(path, json.ToString(), new UTF8Encoding(false));
    }

    private static string JsonNumber(double value)
    {
        return value.ToString("R", CultureInfo.InvariantCulture);
    }

    private static string JsonTriple(double x, double y, double z)
    {
        return "[" + JsonNumber(x) + ", " + JsonNumber(y) + ", " + JsonNumber(z) + "]";
    }

    /// <summary>Quotes exactly as Python's json.dump does with ensure_ascii left on.</summary>
    private static string JsonQuote(string text)
    {
        StringBuilder builder = new StringBuilder(text.Length + 2);
        builder.Append('"');
        foreach (char c in text)
        {
            if (c == '"') builder.Append("\\\"");
            else if (c == '\\') builder.Append("\\\\");
            else if (c == '\n') builder.Append("\\n");
            else if (c == '\r') builder.Append("\\r");
            else if (c == '\t') builder.Append("\\t");
            else if (c < 0x20 || c > 0x7E) builder.Append("\\u").Append(((int)c).ToString("x4", CultureInfo.InvariantCulture));
            else builder.Append(c);
        }
        builder.Append('"');
        return builder.ToString();
    }

    /// <summary>
    /// The batch planner keeps rays that hit nothing in the star, so the three input lists
    /// stay index-aligned, and marks them with the InvalidObjectName. The gaze must never
    /// land on one. Every miss is given a hit point 100 m out - far outside the room - so a
    /// single distance test over the emitted samples proves it.
    /// </summary>
    private static int CheckMissedRaysAreNeverTargeted(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("RAYS THAT HIT NOTHING");
        Console.WriteLine("---------------------------------------------------------------");

        const double MissTipDistance = 100.0;
        const string MissName = "Objeto sem nome";

        List<Point3d> hitPoints = new List<Point3d>(starHitPoints.Count);
        List<string> names = new List<string>(starObjectNames.Count);
        int firstMissedIndex = -1;
        int missedCount = 0;
        for (int i = 0; i < starVectors.Count; i++)
        {
            if (i % 3 == 0)                                    // a third of the star misses
            {
                Vector3d d = starVectors[i];
                hitPoints.Add(new Point3d(eyeOrigin.X + d.X * MissTipDistance,
                                          eyeOrigin.Y + d.Y * MissTipDistance,
                                          eyeOrigin.Z + d.Z * MissTipDistance));
                names.Add(MissName);
                if (firstMissedIndex < 0) firstMissedIndex = i;
                missedCount++;
            }
            else
            {
                hitPoints.Add(starHitPoints[i]);
                names.Add(starObjectNames[i]);
            }
        }

        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        SimulationSettings settings = BuildSettings(starVectors, hitPoints, names, eyeOrigin,
            Path.Combine(directory, "missed-rays.csv"));
        settings.Seed = 4242;
        settings.StartVectorIndex = firstMissedIndex;          // deliberately start on a miss

        SimulationResult result = new SessionSimulator().Generate(settings);
        int failures = 0;
        if (!result.Succeeded)
        {
            Console.WriteLine("FAIL  generation failed: " + result.FailureMessage);
            return 1;
        }

        double worstDistance = 0.0;
        foreach (Point3d p in result.HitPoints)
        {
            double dx = p.X - eyeOrigin.X, dy = p.Y - eyeOrigin.Y, dz = p.Z - eyeOrigin.Z;
            double distance = Math.Sqrt(dx * dx + dy * dy + dz * dz);
            if (distance > worstDistance) worstDistance = distance;
        }
        failures += Report(worstDistance < MissTipDistance * 0.5,
            "no emitted sample landed on a ray that hit nothing (farthest hit point " +
            worstDistance.ToString("F2") + " m, miss tips are at " + MissTipDistance.ToString("F0") + " m)");

        failures += Report(result.ReportText.Contains("START VECTOR MOVED"),
            "a StartVectorIndex pointing at a miss is moved to the nearest usable ray, and says so");

        failures += Report(result.ReportText.Contains(
                "Star rays usable ........... " + (starVectors.Count - missedCount) + " of " + starVectors.Count),
            "the report counts the usable rays (" + (starVectors.Count - missedCount) + " of " + starVectors.Count + ")");

        // A star where nothing was hit is not a session, and must fail loudly rather than
        // produce a file full of the invalid marker.
        List<string> allMissed = starObjectNames.Select(n => MissName).ToList();
        SimulationSettings hopeless = BuildSettings(starVectors, hitPoints, allMissed, eyeOrigin,
            Path.Combine(directory, "missed-rays-all.csv"));
        SimulationResult hopelessResult = new SessionSimulator().Generate(hopeless);
        failures += Report(!hopelessResult.Succeeded && hopelessResult.FailureMessage.Contains("hit a named object"),
            "a star where every ray missed is rejected with a readable message");

        Console.WriteLine(failures == 0 ? "MISSED RAYS PASSED" : "MISSED RAYS FAILED");
        return failures;
    }

    /// <summary>
    /// Models the identical scene in a Z-up frame, runs it with ConvertZUpToYUp, and
    /// requires the CSV to come out byte-identical to the Y-up run. That is the only proof
    /// that the conversion is exactly the inverse of the Revit plugin's reader.
    /// </summary>
    private static int CheckZUpConversion(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("Z-UP INPUT CONVERSION");
        Console.WriteLine("---------------------------------------------------------------");

        string directory = Path.GetDirectoryName(Path.GetFullPath(outputPath));
        string yUpPath = Path.Combine(directory, "frame-yup.csv");
        string zUpPath = Path.Combine(directory, "frame-zup.csv");

        SimulationSettings yUp = BuildSettings(starVectors, starHitPoints, starObjectNames, eyeOrigin, yUpPath);
        yUp.StartVectorIndex = NearestStarIndex(starVectors, new Vector3d(0, 0, 1));
        yUp.Seed = 777;
        SimulationResult yUpResult = new SessionSimulator().Generate(yUp);

        // The same room, modelled Z-up: swap Y and Z on every input.
        List<Vector3d> zUpVectors = starVectors.Select(v => new Vector3d(v.X, v.Z, v.Y)).ToList();
        List<Point3d> zUpHitPoints = starHitPoints.Select(p => new Point3d(p.X, p.Z, p.Y)).ToList();
        Point3d zUpEyeOrigin = new Point3d(eyeOrigin.X, eyeOrigin.Z, eyeOrigin.Y);

        SimulationSettings zUp = BuildSettings(zUpVectors, zUpHitPoints, starObjectNames, zUpEyeOrigin, zUpPath);
        zUp.NeutralForward = new Vector3d(0, 1, 0);            // "forward" in a Z-up frame
        zUp.StartVectorIndex = yUp.StartVectorIndex;           // the same ray of the same star
        zUp.Seed = 777;
        zUp.ConvertZUpToYUp = true;
        SimulationResult zUpResult = new SessionSimulator().Generate(zUp);

        int failures = 0;
        if (!yUpResult.Succeeded || !zUpResult.Succeeded)
        {
            Console.WriteLine("FAIL  one of the two runs did not complete");
            return 1;
        }

        byte[] yUpBytes = File.ReadAllBytes(yUpPath);
        byte[] zUpBytes = File.ReadAllBytes(zUpPath);
        failures += Report(yUpBytes.SequenceEqual(zUpBytes),
            "a Z-up star with ConvertZUpToYUp produces a byte-identical CSV to the Y-up star");

        // And the geometry outputs must come back in the frame each caller supplied.
        bool outputsSwapped = yUpResult.HitPoints.Count == zUpResult.HitPoints.Count &&
            Enumerable.Range(0, yUpResult.HitPoints.Count).All(i =>
                Math.Abs(yUpResult.HitPoints[i].X - zUpResult.HitPoints[i].X) < 1e-9 &&
                Math.Abs(yUpResult.HitPoints[i].Y - zUpResult.HitPoints[i].Z) < 1e-9 &&
                Math.Abs(yUpResult.HitPoints[i].Z - zUpResult.HitPoints[i].Y) < 1e-9);
        failures += Report(outputsSwapped,
            "HitPoints come back in the caller's own frame, not the CSV's");

        Console.WriteLine(failures == 0 ? "Z-UP CONVERSION PASSED" : "Z-UP CONVERSION FAILED");
        return failures;
    }

    /// <summary>
    /// The blink count is a Poisson draw, so a single session says nothing about bias.
    /// Averaging many seeds does.
    /// </summary>
    private static void CheckBlinkBias(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        Console.WriteLine();
        Console.WriteLine("BLINK COUNT BIAS OVER 40 SEEDS (expected 27.0 per 300 s at 0.09 /s)");
        List<double> counts = new List<double>();
        for (int seed = 1000; seed < 1040; seed++)
        {
            SimulationSettings settings = BuildSettings(starVectors, starHitPoints, starObjectNames, eyeOrigin,
                Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)), "blink-case.csv"));
            settings.StartVectorIndex = NearestStarIndex(starVectors, new Vector3d(0, 0, 1));
            settings.Seed = seed;
            SimulationResult result = new SessionSimulator().Generate(settings);
            string line = result.ReportText.Split(new[] { "\r\n" }, StringSplitOptions.None)
                .First(l => l.StartsWith("Blink events", StringComparison.Ordinal));
            counts.Add(double.Parse(line.Split(new[] { "... " }, StringSplitOptions.None)[1].Split(' ')[0],
                CultureInfo.InvariantCulture));
        }
        double mean = counts.Average();
        double sd = Math.Sqrt(counts.Select(c => (c - mean) * (c - mean)).Sum() / (counts.Count - 1));
        double standardError = sd / Math.Sqrt(counts.Count);
        Console.WriteLine("  mean " + mean.ToString("F2") + " +/- " + standardError.ToString("F2") +
                          " (sd " + sd.ToString("F2") + ", Poisson sd would be 5.20)");
        Console.WriteLine("  " + (Math.Abs(mean - 27.0) < 3.0 * standardError
            ? "unbiased: the mean is within 3 standard errors of 27.0"
            : "BIASED: the mean is more than 3 standard errors from 27.0"));
    }

    private static int RunCase(string label, List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath, Action<SimulationSettings> configure)
    {
        SimulationSettings settings = BuildSettings(starVectors, starHitPoints, starObjectNames, eyeOrigin,
            Path.Combine(Path.GetDirectoryName(Path.GetFullPath(outputPath)), "sweep-case.csv"));
        settings.StartVectorIndex = NearestStarIndex(starVectors, new Vector3d(0, 0, 1));
        configure(settings);

        SimulationResult result = new SessionSimulator().Generate(settings);
        if (!result.Succeeded)
        {
            Console.WriteLine("FAIL  " + label.PadRight(36) + " generation failed: " + result.FailureMessage);
            return 1;
        }

        string[] reportLines = result.ReportText.Split(new[] { "\r\n", "\n" }, StringSplitOptions.RemoveEmptyEntries);
        List<string> failed = reportLines.Where(l => l.StartsWith("FAIL  ", StringComparison.Ordinal)).ToList();
        List<string> warnings = reportLines.Where(l => l.StartsWith("!!  ", StringComparison.Ordinal)).ToList();
        if (failed.Count == 0)
        {
            Console.WriteLine("PASS  " + label);
            foreach (string line in warnings) Console.WriteLine("        " + line);
            string fixationLine = reportLines.FirstOrDefault(l => l.StartsWith("Fixations recovered", StringComparison.Ordinal));
            if (warnings.Count > 0 && fixationLine != null) Console.WriteLine("        " + fixationLine);
            return 0;
        }
        Console.WriteLine("FAIL  " + label);
        foreach (string line in failed) Console.WriteLine("        " + line);
        return failed.Count;
    }

    private static SimulationSettings BuildSettings(List<Vector3d> starVectors, List<Point3d> starHitPoints,
        List<string> starObjectNames, Point3d eyeOrigin, string outputPath)
    {
        SimulationSettings settings = new SimulationSettings();
        settings.StarVectors = starVectors;
        settings.StarHitPoints = starHitPoints;
        settings.StarObjectNames = starObjectNames;
        settings.EyeOrigin = eyeOrigin;
        settings.NeutralForward = new Vector3d(0, 0, 1);

        settings.SessionSeconds = SimulationDefaults.SessionSeconds;
        settings.SampleRateHz = SimulationDefaults.SampleRateHz;
        settings.BaselineSeconds = SimulationDefaults.BaselineSeconds;
        settings.Seed = 20260725;

        settings.FixationCountMin = 0;
        settings.FixationCountMax = 0;
        settings.FixationRatePerSecond = SimulationDefaults.FixationRatePerSecond;
        settings.FixationDurationMinSec = SimulationDefaults.FixationDurationMinSeconds;
        settings.FixationDurationMaxSec = SimulationDefaults.FixationDurationMaxSeconds;
        settings.FixationDurationLogMu = SimulationDefaults.FixationDurationLogMu;
        settings.FixationDurationLogSigma = SimulationDefaults.FixationDurationLogSigma;
        settings.IntraFixationDispersionDeg = SimulationDefaults.IntraFixationDispersionDegrees;

        settings.SaccadeAmplitudeLogMu = SimulationDefaults.SaccadeAmplitudeLogMu;
        settings.SaccadeAmplitudeLogSigma = SimulationDefaults.SaccadeAmplitudeLogSigma;
        settings.SaccadeAmplitudeMinDeg = SimulationDefaults.SaccadeAmplitudeMinDegrees;
        settings.SaccadeAmplitudeMaxDeg = SimulationDefaults.SaccadeAmplitudeMaxDegrees;
        settings.ReturnBiasProbability = SimulationDefaults.ReturnBiasProbability;

        settings.DistinctObjectsMin = 8;
        settings.DistinctObjectsMax = 16;

        settings.NeckYawLimitDeg = SimulationDefaults.NeckYawLimitDegrees;
        settings.NeckPitchUpLimitDeg = SimulationDefaults.NeckPitchUpLimitDegrees;
        settings.NeckPitchDownLimitDeg = SimulationDefaults.NeckPitchDownLimitDegrees;
        settings.EyeYawLimitDeg = SimulationDefaults.EyeYawLimitDegrees;
        settings.EyePitchLimitDeg = SimulationDefaults.EyePitchLimitDegrees;
        settings.HeadTimeConstantSec = SimulationDefaults.HeadTimeConstantSeconds;

        settings.InvalidFrameRate = SimulationDefaults.InvalidFrameRate;
        settings.BlinkRatePerSecond = SimulationDefaults.BlinkRatePerSecond;
        settings.PupilMinimumDomain = new Interval(
            SimulationDefaults.PupilMinimumDomainLow, SimulationDefaults.PupilMinimumDomainHigh);
        settings.PupilMaximumDomain = new Interval(
            SimulationDefaults.PupilMaximumDomainLow, SimulationDefaults.PupilMaximumDomainHigh);

        settings.ParticipantId = SimulationDefaults.ParticipantId;
        settings.SessionName = SimulationDefaults.SessionName;
        settings.SceneName = SimulationDefaults.SceneName;
        settings.SeatName = SimulationDefaults.SeatName;
        settings.SessionStartText = SimulationDefaults.SessionStartDateTime;
        settings.DeviceName = SimulationDefaults.DeviceName;

        settings.OutputFilePath = outputPath;
        return settings;
    }

    // -----------------------------------------------------------------------------
    //  Format comparison against the two real capture files.
    // -----------------------------------------------------------------------------
    private static int CompareFormatWithRealFiles(string generatedPath)
    {
        // The reference must be a capture straight off the headset. Do NOT point this at a
        // copy that has been opened and saved in Excel: under a pt-BR locale the list
        // separator is ';', so Excel drops the whole row into one cell and writes it back as
        // a single quoted field with every inner quote doubled. The Desktop copies
        // EyeGazingData.csv / EyeGazingData2.csv are mangled that way - reading the dialect
        // off them is what produced the collapsed rows this writer used to emit.
        string[] realFiles = FindReferenceCaptures();

        Console.WriteLine("FORMAT COMPARISON AGAINST THE REAL FILES");
        Console.WriteLine("---------------------------------------------------------------");

        byte[] generatedBytes = File.ReadAllBytes(generatedPath);
        int failures = 0;

        bool hasBom = generatedBytes.Length >= 3 &&
                      generatedBytes[0] == 0xEF && generatedBytes[1] == 0xBB && generatedBytes[2] == 0xBF;
        failures += Report(!hasBom, "generated file has no UTF-8 BOM");

        string generatedText = new UTF8Encoding(false).GetString(generatedBytes);
        failures += Report(generatedText.EndsWith("\r\n", StringComparison.Ordinal), "file ends with CRLF");
        failures += Report(!generatedText.Contains("\n\r") && CountOccurrences(generatedText, "\n") ==
                           CountOccurrences(generatedText, "\r\n"), "every line terminator is CRLF");

        string[] generatedLines = generatedText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

        if (realFiles.Length == 0)
            Console.WriteLine("SKIP  no reference capture found under PluginCAISM\\Assets");

        foreach (string realFile in realFiles)
        {
            if (!File.Exists(realFile))
            {
                Console.WriteLine("SKIP  reference file not found: " + realFile);
                continue;
            }

            byte[] realBytes = File.ReadAllBytes(realFile);
            string realText = new UTF8Encoding(false).GetString(realBytes);
            string[] realLines = realText.Split(new[] { "\r\n" }, StringSplitOptions.RemoveEmptyEntries);

            string label = Path.GetFileName(realFile);
            failures += Report(string.Equals(realLines[0], generatedLines[0], StringComparison.Ordinal),
                "header identical to " + label);

            // Structural shape of a data line: the quoted timeStamp opens it and the quoted
            // htcDeviceName closes it, with no extra pair of quotes wrapping the whole row.
            failures += Report(OpensWithQuotedField(realLines[1]) && OpensWithQuotedField(generatedLines[1]),
                "data line opens with a quoted timeStamp field, like " + label);
            failures += Report(ClosesWithQuotedField(realLines[1]) && ClosesWithQuotedField(generatedLines[1]),
                "data line closes with a quoted htcDeviceName field, like " + label);

            string[] realFields = SessionReport.ParseQuotedRow(realLines[1]);
            string[] generatedFields = SessionReport.ParseQuotedRow(generatedLines[1]);
            failures += Report(realFields.Length == 38 && generatedFields.Length == 38,
                "both parse into 38 fields (" + label + ": " + realFields.Length +
                ", generated: " + generatedFields.Length + ")");

            if (realFields.Length != 38 || generatedFields.Length != 38)
            {
                Console.WriteLine("SKIP  remaining field-level checks against " + label +
                                  " - a row that does not yield 38 fields is not a usable reference");
                continue;
            }

            DateTime parsedReal, parsedGenerated;
            bool stampsOk =
                DateTime.TryParseExact(realFields[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsedReal) &&
                DateTime.TryParseExact(generatedFields[0], "yyyy-MM-dd HH:mm:ss.fff", CultureInfo.InvariantCulture,
                    DateTimeStyles.None, out parsedGenerated);
            failures += Report(stampsOk, "timestamps use yyyy-MM-dd HH:mm:ss.fff in both");

            failures += Report(string.Equals(realFields[37], generatedFields[37], StringComparison.Ordinal),
                "htcDeviceName string reproduced verbatim from " + label);

            bool realNewestFirst = IsDescending(realLines);
            failures += Report(realNewestFirst || true,
                label + " row order is " + (realNewestFirst ? "newest first (matches)" : "mixed (known 2 % DB artefact)"));
        }

        failures += Report(IsDescending(generatedLines), "generated rows are strictly newest first");

        Console.WriteLine("---------------------------------------------------------------");
        Console.WriteLine(failures == 0 ? "FORMAT COMPARISON PASSED" : failures + " FORMAT CHECK(S) FAILED");
        Console.WriteLine();
        Console.WriteLine("First two generated data lines:");
        Console.WriteLine(generatedLines[1]);
        Console.WriteLine(generatedLines[2]);
        return failures;
    }

    /// <summary>
    /// Locates the pristine capture(s) kept in the repository, by walking up from the test
    /// bench binary until PluginCAISM\Assets turns up.
    /// </summary>
    private static string[] FindReferenceCaptures()
    {
        DirectoryInfo dir = new DirectoryInfo(AppContext.BaseDirectory);
        while (dir != null)
        {
            string assets = Path.Combine(dir.FullName, "PluginCAISM", "Assets");
            if (Directory.Exists(assets))
                return Directory.GetFiles(assets, "EyeGazingData*.csv")
                                .Where(f => !Path.GetFileName(f).Contains("Copia"))
                                .OrderBy(f => f, StringComparer.OrdinalIgnoreCase)
                                .ToArray();
            dir = dir.Parent;
        }
        return new string[0];
    }

    /// <summary>
    /// True when the row starts with a quoted field that is closed before the first comma -
    /// i.e. `"2026-01-14 16:52:57.561",...` and not the collapsed `"2026-01-14 ...,RB001,...`.
    /// </summary>
    private static bool OpensWithQuotedField(string line)
    {
        if (line.Length < 2 || line[0] != '"') return false;
        int closing = line.IndexOf('"', 1);
        return closing > 0 && (closing == line.Length - 1 || line[closing + 1] == ',');
    }

    /// <summary>
    /// True when the row ends with a quoted field opened right after the last comma.
    /// </summary>
    private static bool ClosesWithQuotedField(string line)
    {
        if (line.Length < 2 || line[line.Length - 1] != '"') return false;
        int lastComma = line.LastIndexOf(',');
        return lastComma >= 0 && lastComma + 1 < line.Length - 1 && line[lastComma + 1] == '"';
    }

    private static bool IsDescending(string[] lines)
    {
        string previous = null;
        for (int i = 1; i < lines.Length; i++)
        {
            string[] fields = SessionReport.ParseQuotedRow(lines[i]);
            if (fields.Length == 0) continue;
            if (previous != null && string.CompareOrdinal(previous, fields[0]) <= 0) return false;
            previous = fields[0];
        }
        return true;
    }

    private static int CountOccurrences(string text, string token)
    {
        int count = 0, index = 0;
        while ((index = text.IndexOf(token, index, StringComparison.Ordinal)) >= 0) { count++; index += token.Length; }
        return count;
    }

    // -----------------------------------------------------------------------------
    //  The environment signal over many seeds.
    //
    //  One session says almost nothing about a model whose whole shape is drawn per
    //  session. These checks run the environment simulator on its own over many seeds and
    //  ask the three questions the sine-slice model exists to answer: does the signal ever
    //  change its mind more than twice, does it respect its rate limit whichever slice it
    //  drew, and do sessions actually differ - some nearly still, some travelling the full
    //  budget - rather than all coming out the same size.
    // -----------------------------------------------------------------------------
    private static int CheckEnvironmentShape()
    {
        const int Seeds = 200;
        int failures = 0;

        Console.WriteLine();
        Console.WriteLine("ENVIRONMENT SIGNAL SHAPE OVER " + Seeds + " SEEDS");

        double sampleInterval = 1.0 / StudyConfiguration.SampleRateHz;
        int sampleCount = (int) (StudyConfiguration.SessionSeconds / sampleInterval);
        List<double> times = new List<double>(sampleCount);
        for (int i = 0; i < sampleCount; i++) times.Add(i * sampleInterval);
        double sessionMinutes = times[times.Count - 1] / 60.0;

        int worstTemperatureTurns = 0, worstHumidityTurns = 0;
        double worstTemperatureRate = 0.0, worstHumidityRate = 0.0;
        double smallestTemperatureTravel = double.MaxValue, largestTemperatureTravel = 0.0;
        double smallestHumidityTravel = double.MaxValue, largestHumidityTravel = 0.0;
        double worstNoiseStep = 0.0;
        long calmSteps = 0, totalSteps = 0;
        long nearMedianSamples = 0, totalSamples = 0;
        double loudestNoise = 0.0;

        // What one sample can add to the noise before anything is wrong: the jitter can
        // swing corner to corner, and up to two envelopes can be climbing at their steepest
        // at the same time, because events are never held back to wait for each other.
        double steepestEnvelope = Math.Max(
            Math.PI * StudyConfiguration.NoiseSustainedMaxExcessDb /
                (2.0 * StudyConfiguration.NoiseSustainedRiseMinSeconds),
            Math.PI * StudyConfiguration.NoisePassByMaxExcessDb /
                StudyConfiguration.NoisePassByMinSpanSeconds);
        double noiseStepCeiling = 2.0 * StudyConfiguration.NoiseJitterMaxDb +
                                  2.0 * steepestEnvelope * sampleInterval;
        double calmStepBudget = 2.0 * StudyConfiguration.NoiseJitterMaxDb;

        for (int seed = 0; seed < Seeds; seed++)
        {
            List<EnvironmentSample> samples = new EnvironmentSimulator()
                .Generate(times, 24.0, 60.0, 7000 + seed);

            List<double> temperatures = samples.Select(s => s.TemperatureC).ToList();
            List<double> humidities = samples.Select(s => s.HumidityPercent).ToList();
            List<double> noises = samples.Select(s => s.NoiseDb).ToList();

            worstTemperatureTurns = Math.Max(worstTemperatureTurns, CountTurns(temperatures));
            worstHumidityTurns = Math.Max(worstHumidityTurns, CountTurns(humidities));

            for (int i = 1; i < samples.Count; i++)
            {
                double minutes = (samples[i].TimeSeconds - samples[i - 1].TimeSeconds) / 60.0;
                if (minutes <= 1e-12) continue;
                worstTemperatureRate = Math.Max(worstTemperatureRate,
                    Math.Abs(temperatures[i] - temperatures[i - 1]) / minutes);
                worstHumidityRate = Math.Max(worstHumidityRate,
                    Math.Abs(humidities[i] - humidities[i - 1]) / minutes);

                double noiseStep = Math.Abs(noises[i] - noises[i - 1]);
                worstNoiseStep = Math.Max(worstNoiseStep, noiseStep);
                if (noiseStep <= calmStepBudget) calmSteps++;
                totalSteps++;
            }

            double temperatureTravel = temperatures.Max() - temperatures.Min();
            double humidityTravel = humidities.Max() - humidities.Min();
            smallestTemperatureTravel = Math.Min(smallestTemperatureTravel, temperatureTravel);
            largestTemperatureTravel = Math.Max(largestTemperatureTravel, temperatureTravel);
            smallestHumidityTravel = Math.Min(smallestHumidityTravel, humidityTravel);
            largestHumidityTravel = Math.Max(largestHumidityTravel, humidityTravel);

            // How much of the session is just the floor: everything an event is not.
            List<double> sorted = new List<double>(noises);
            sorted.Sort();
            double median = sorted[sorted.Count / 2];
            foreach (double level in noises)
                if (Math.Abs(level - median) <= 1.5) nearMedianSamples++;
            totalSamples += noises.Count;
            loudestNoise = Math.Max(loudestNoise, noises.Max());
        }

        double calmShare = calmSteps / (double) totalSteps;
        double floorShare = nearMedianSamples / (double) totalSamples;

        Console.WriteLine("  temperature travelled " + smallestTemperatureTravel.ToString("F2") +
                          " - " + largestTemperatureTravel.ToString("F2") + " C per session, humidity " +
                          smallestHumidityTravel.ToString("F2") + " - " +
                          largestHumidityTravel.ToString("F2") + " %");
        Console.WriteLine("  loudest reading " + loudestNoise.ToString("F1") + " dB, worst noise step " +
                          worstNoiseStep.ToString("F2") + " dB (ceiling " +
                          noiseStepCeiling.ToString("F2") + ")");

        failures += Report(worstTemperatureTurns <= 2 && worstHumidityTurns <= 2,
            "no session ever reverses direction more than twice - a slice of one period cannot " +
            "(worst was " + worstTemperatureTurns + " for temperature, " + worstHumidityTurns +
            " for humidity)");
        failures += Report(
            worstTemperatureRate <= StudyConfiguration.TemperatureMaxChangePerMinuteC + 1e-9 &&
            worstHumidityRate <= StudyConfiguration.HumidityMaxChangePerMinutePercent + 1e-9,
            "whichever slice it drew, no session outran its rate limit (worst " +
            worstTemperatureRate.ToString("F3") + " C/min, " + worstHumidityRate.ToString("F3") + " %/min)");
        failures += Report(
            smallestTemperatureTravel <= 0.20 && largestTemperatureTravel >= 0.70 &&
            smallestHumidityTravel <= 0.60 && largestHumidityTravel >= 1.50,
            "sessions differ in size, from nearly still to the full budget - the excursion is " +
            "drawn per session, not shared");
        failures += Report(
            largestTemperatureTravel <= StudyConfiguration.TemperatureMaxChangePerMinuteC * sessionMinutes + 1e-9 &&
            largestHumidityTravel <= StudyConfiguration.HumidityMaxChangePerMinutePercent * sessionMinutes + 1e-9,
            "and none of them travelled further than rate x duration allows");
        failures += Report(calmShare >= 0.95,
            "the noise moves by jitter alone almost every sample (" + (calmShare * 100.0).ToString("F1") +
            "% of steps are inside " + calmStepBudget.ToString("F1") + " dB)");
        failures += Report(worstNoiseStep <= noiseStepCeiling,
            "and no sample ever jumps: even the loudest event arrives over seconds, not in one row");
        failures += Report(floorShare >= 0.85,
            "the room is just its own floor most of the time (" + (floorShare * 100.0).ToString("F1") +
            "% of samples sit within 1.5 dB of the session median)");

        return failures;
    }

    /// <summary>
    /// How many times a signal reverses direction, ignoring the stretches where it does not
    /// move at all - which is most of them once two decimals have had their say.
    /// </summary>
    private static int CountTurns(IList<double> values)
    {
        int turns = 0, direction = 0;
        for (int i = 1; i < values.Count; i++)
        {
            double step = values[i] - values[i - 1];
            if (step == 0.0) continue;
            int sign = step > 0.0 ? 1 : -1;
            if (direction != 0 && sign != direction) turns++;
            direction = sign;
        }
        return turns;
    }

    private static int Report(bool passed, string description)
    {
        Console.WriteLine((passed ? "PASS  " : "FAIL  ") + description);
        return passed ? 0 : 1;
    }

    // -----------------------------------------------------------------------------
    //  Star construction.
    // -----------------------------------------------------------------------------
    private static IEnumerable<Vector3d> FibonacciSphere(int count)
    {
        double goldenAngle = Math.PI * (3.0 - Math.Sqrt(5.0));
        for (int i = 0; i < count; i++)
        {
            double y = 1.0 - 2.0 * (i + 0.5) / count;
            double radius = Math.Sqrt(Math.Max(0.0, 1.0 - y * y));
            double theta = goldenAngle * i;
            yield return new Vector3d(Math.Cos(theta) * radius, y, Math.Sin(theta) * radius);
        }
    }

    private static int NearestStarIndex(List<Vector3d> vectors, Vector3d target)
    {
        int best = 0;
        double bestDot = -2.0;
        for (int i = 0; i < vectors.Count; i++)
        {
            double dot = vectors[i].X * target.X + vectors[i].Y * target.Y + vectors[i].Z * target.Z;
            if (dot > bestDot) { bestDot = dot; best = i; }
        }
        return best;
    }
}

/// <summary>
/// A crude named-panel auditorium: an axis-aligned box whose faces are subdivided into
/// named regions, so the star carries a realistic spread of object names.
/// </summary>
internal class Auditorium
{
    private const double MinX = -5.0, MaxX = 5.0;
    private const double MinY = 0.0, MaxY = 3.5;
    private const double MinZ = -4.0, MaxZ = 6.0;

    public double Raycast(Point3d origin, Vector3d direction, out Point3d hitPoint, out string objectName)
    {
        double distance = double.MaxValue;
        int face = -1;

        TestSlab(origin.X, direction.X, MinX, MaxX, 0, 1, ref distance, ref face);
        TestSlab(origin.Y, direction.Y, MinY, MaxY, 2, 3, ref distance, ref face);
        TestSlab(origin.Z, direction.Z, MinZ, MaxZ, 4, 5, ref distance, ref face);

        if (face < 0)
        {
            hitPoint = origin;
            objectName = "Objeto sem nome";
            return 0.0;
        }

        hitPoint = new Point3d(
            origin.X + direction.X * distance,
            origin.Y + direction.Y * distance,
            origin.Z + direction.Z * distance);
        objectName = NameFor(face, hitPoint);
        return distance;
    }

    private static void TestSlab(double origin, double direction, double low, double high,
        int lowFace, int highFace, ref double bestDistance, ref int bestFace)
    {
        if (Math.Abs(direction) < 1e-12) return;
        double toLow = (low - origin) / direction;
        double toHigh = (high - origin) / direction;
        if (toLow > 1e-6 && toLow < bestDistance) { bestDistance = toLow; bestFace = lowFace; }
        if (toHigh > 1e-6 && toHigh < bestDistance) { bestDistance = toHigh; bestFace = highFace; }
    }

    private static string NameFor(int face, Point3d p)
    {
        switch (face)
        {
            case 0:  // x = MinX, left wall
                if (p.Y > 0.4 && p.Y < 1.4 && p.Z > 0.0 && p.Z < 2.0) return "Planta: Esquerda";
                return p.Z < 1.0 ? "Parede: Lateral Esquerda Tras" : "Parede: Lateral Esquerda Frente";
            case 1:  // x = MaxX, right wall
                if (p.Y > 1.0 && p.Y < 2.6 && p.Z > -1.0 && p.Z < 3.0) return "Janela: Vista Externa";
                return p.Z < 1.0 ? "Parede: Lateral Direita Tras" : "Parede: Lateral Direita Frente";
            case 2:  // floor
                if (p.Z > -2.0 && p.Z < 4.0 && Math.Abs(p.X) < 3.0) return "Cadeiras: Bloco Central";
                return "Chão";
            case 3:  // ceiling
                if (Math.Abs(p.X) < 1.5 && Math.Abs(p.Z - 1.0) < 2.5) return "Painel LED";
                return "Teto";
            case 4:  // z = MinZ, behind
                return p.X < 0 ? "Parede: Fundo Esquerda" : "Parede: Fundo Direita";
            default: // z = MaxZ, in front
                if (p.Y < 1.6) return "Parede: Rippas";
                if (Math.Abs(p.X) < 2.0) return "Parede: Verde";
                return "Parede: Frente Lateral";
        }
    }
}
