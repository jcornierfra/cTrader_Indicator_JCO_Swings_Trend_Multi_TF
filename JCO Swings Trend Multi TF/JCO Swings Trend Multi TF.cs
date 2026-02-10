// =====================================================
// JCO Swings Trend Multi TF Indicator
// =====================================================
// Version: 2.0
// Date: 2026-02-10
// Author: Jerome Cornier
//
// Description:
// Detects swing highs and lows on up to 4 configurable timeframes and determines
// the trend direction based on swing structure. Features include:
// - Multi-timeframe analysis (1 primary + 3 optional secondary TFs)
// - Trend detection: Bullish/Bearish with Momentum or Compression status
// - Dual CHoCH detection with liquidity sweep restoration
// - CHoCH-gated trend reversals to prevent premature trend changes
// - Liquidity sweep detection with close price confirmation and dual CHoCH
// - Swing alternation with automatic missing swing insertion
// - Multi-line dashboard showing all active TF analyses
//
// Based on:
// - cTrader JCO Swings Trend HTF v1.7 (single TF)
// - TradingView JCO Swings Trend Multi TF v2.2 (multi TF)
//
// Changelog:
// v2.0 (2026-02-10)
//   - Multi-timeframe support: 4 TFs (1 primary + 3 optional secondary)
//   - TFState class encapsulating per-TF state for clean multi-TF processing
//   - Multi-line dashboard with trend, CHoCH, liquidity sweep, expansion per TF
//   - TF1: red/green icons (hardcoded); TF2-4: single configurable color per TF
//   - Configurable icon colors via enum dropdown
//   - Display expansion as toggle parameter
//   - All analysis logic (trend, CHoCH, gate, liquidity) runs independently per TF
// =====================================================

using System;
using System.Linq;
using cAlgo.API;
using cAlgo.API.Indicators;
using cAlgo.API.Internals;

namespace cAlgo.Indicators
{
    [Indicator(IsOverlay = true, TimeZone = TimeZones.UTC, AccessRights = AccessRights.None)]
    public class JCOSwingsTrendMultiTF : Indicator
    {
        // ===== Enums =====
        public enum TFIconColor
        {
            Orange,
            Purple,
            Aqua,
            Blue,
            Yellow,
            DodgerBlue,
            Magenta,
            White
        }

        // ===== Parameters: General =====
        [Parameter("Swing Period", DefaultValue = 5, Group = "General")]
        public int SwingPeriod { get; set; }

        [Parameter("Swing Lookback Period", DefaultValue = 200, Group = "General")]
        public int SwingLookbackPeriod { get; set; }

        [Parameter("Icon Gap (%)", DefaultValue = 2.0, MinValue = 0.1, MaxValue = 10, Group = "General")]
        public double SwingIconGapPercent { get; set; }

        [Parameter("Display Dashboard", DefaultValue = true, Group = "General")]
        public bool DisplayDashboard { get; set; }

        [Parameter("Display Expansion", DefaultValue = true, Group = "General")]
        public bool DisplayExpansion { get; set; }

        [Parameter("Dashboard Font Size", DefaultValue = 10, MinValue = 6, MaxValue = 20, Group = "General")]
        public int DashboardFontSize { get; set; }

        [Parameter("Enable Print", DefaultValue = false, Group = "General")]
        public bool EnablePrint { get; set; }

        // ===== Parameters: TF1 (Primary - always enabled) =====
        [Parameter("Timeframe", DefaultValue = "Hour", Group = "TF1 (Primary)")]
        public TimeFrame SwingTimeFrame1 { get; set; }

        [Parameter("Draw Icons", DefaultValue = true, Group = "TF1 (Primary)")]
        public bool DrawIcons1 { get; set; }

        [Parameter("Draw Dots", DefaultValue = false, Group = "TF1 (Primary)")]
        public bool DrawDots1 { get; set; }

        // ===== Parameters: TF2 =====
        [Parameter("Enable", DefaultValue = false, Group = "TF2")]
        public bool EnableTF2 { get; set; }

        [Parameter("Timeframe", DefaultValue = "Hour4", Group = "TF2")]
        public TimeFrame SwingTimeFrame2 { get; set; }

        [Parameter("Draw Icons", DefaultValue = true, Group = "TF2")]
        public bool DrawIcons2 { get; set; }

        [Parameter("Icon Color", DefaultValue = TFIconColor.Orange, Group = "TF2")]
        public TFIconColor IconColor2 { get; set; }

        // ===== Parameters: TF3 =====
        [Parameter("Enable", DefaultValue = false, Group = "TF3")]
        public bool EnableTF3 { get; set; }

        [Parameter("Timeframe", DefaultValue = "Daily", Group = "TF3")]
        public TimeFrame SwingTimeFrame3 { get; set; }

        [Parameter("Draw Icons", DefaultValue = true, Group = "TF3")]
        public bool DrawIcons3 { get; set; }

        [Parameter("Icon Color", DefaultValue = TFIconColor.Purple, Group = "TF3")]
        public TFIconColor IconColor3 { get; set; }

        // ===== Parameters: TF4 =====
        [Parameter("Enable", DefaultValue = false, Group = "TF4")]
        public bool EnableTF4 { get; set; }

        [Parameter("Timeframe", DefaultValue = "Weekly", Group = "TF4")]
        public TimeFrame SwingTimeFrame4 { get; set; }

        [Parameter("Draw Icons", DefaultValue = true, Group = "TF4")]
        public bool DrawIcons4 { get; set; }

        [Parameter("Icon Color", DefaultValue = TFIconColor.Aqua, Group = "TF4")]
        public TFIconColor IconColor4 { get; set; }

        // ===== Constants =====
        private const int NODIRECTION = 0;
        private const int BULLISH = 1;
        private const int BEARISH = -1;
        private const int MOMENTUM = 1;
        private const int COMPRESSION = -1;
        private const int CONTINUATION = 0;
        private const int CHOCH_BULLISH = 1;
        private const int CHOCH_BEARISH = -1;

        // ===== Inner Classes =====
        public class Swing
        {
            public DateTime SwingHTFOpenTime { get; set; }
            public DateTime SwingChartOpenTime { get; set; }
            public double SwingPrice { get; set; }
            public double DisplayPrice { get; set; }
            public int SwingBarsIndex { get; set; }
        }

        private enum SwingType { High, Low }

        private class SwingPoint
        {
            public Swing Swing { get; set; }
            public SwingType Type { get; set; }
        }

        private class TFState
        {
            public int Index;
            public TimeFrame TimeFrame;
            public bool Active;
            public bool DrawIcons;
            public bool DrawDots;
            public Color HighColor;
            public Color LowColor;

            public Bars BarsHTF;
            public int CandleRangeInHTF;

            public Swing[] SwingHighPrices;
            public Swing[] SwingLowPrices;
            public int SwingHighCount;
            public int SwingLowCount;

            public int Direction;
            public int TrendStatus;
            public int CHoCHStatus;
            public bool LiquiditySweep;
        }

        // ===== Fields =====
        private TFState[] _tfStates;

        // ===== Color Helper =====
        private Color ToColor(TFIconColor c)
        {
            switch (c)
            {
                case TFIconColor.Orange: return Color.Orange;
                case TFIconColor.Purple: return Color.Purple;
                case TFIconColor.Aqua: return Color.Aqua;
                case TFIconColor.Blue: return Color.Blue;
                case TFIconColor.Yellow: return Color.Yellow;
                case TFIconColor.DodgerBlue: return Color.DodgerBlue;
                case TFIconColor.Magenta: return Color.Magenta;
                case TFIconColor.White: return Color.White;
                default: return Color.White;
            }
        }

        // ===== Initialize =====
        protected override void Initialize()
        {
            // Clean up objects from previous single-TF version (backward compatibility)
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("SwingsText")).ToList())
                Chart.RemoveObject(obj.Name);
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("SwingIcon")).ToList())
                Chart.RemoveObject(obj.Name);
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("SwingDot")).ToList())
                Chart.RemoveObject(obj.Name);

            int chartTFSeconds = GetTimeFrameInSeconds(Bars.TimeFrame);

            _tfStates = new TFState[4];

            // TF1 (Primary - always enabled)
            _tfStates[0] = CreateTFState(0, SwingTimeFrame1, true, DrawIcons1, DrawDots1,
                Color.Red, Color.Green, chartTFSeconds);

            // TF2
            Color c2 = ToColor(IconColor2);
            _tfStates[1] = CreateTFState(1, SwingTimeFrame2, EnableTF2, DrawIcons2, false,
                c2, c2, chartTFSeconds);

            // TF3
            Color c3 = ToColor(IconColor3);
            _tfStates[2] = CreateTFState(2, SwingTimeFrame3, EnableTF3, DrawIcons3, false,
                c3, c3, chartTFSeconds);

            // TF4
            Color c4 = ToColor(IconColor4);
            _tfStates[3] = CreateTFState(3, SwingTimeFrame4, EnableTF4, DrawIcons4, false,
                c4, c4, chartTFSeconds);

            if (EnablePrint)
            {
                for (int i = 0; i < 4; i++)
                {
                    var tf = _tfStates[i];
                    Print($"TF{i + 1}: {tf.TimeFrame}, Active={tf.Active}, CandleRange={tf.CandleRangeInHTF}");
                }
            }
        }

        private TFState CreateTFState(int index, TimeFrame timeFrame, bool enabled, bool drawIcons, bool drawDots,
            Color highColor, Color lowColor, int chartTFSeconds)
        {
            int swingTFSeconds = GetTimeFrameInSeconds(timeFrame);
            bool active = enabled && (chartTFSeconds <= swingTFSeconds);

            var state = new TFState
            {
                Index = index,
                TimeFrame = timeFrame,
                Active = active,
                DrawIcons = drawIcons,
                DrawDots = drawDots,
                HighColor = highColor,
                LowColor = lowColor,
                SwingHighPrices = new Swing[SwingLookbackPeriod],
                SwingLowPrices = new Swing[SwingLookbackPeriod],
                SwingHighCount = 0,
                SwingLowCount = 0,
                Direction = NODIRECTION,
                TrendStatus = 0,
                CHoCHStatus = CONTINUATION,
                LiquiditySweep = false
            };

            if (active)
            {
                state.BarsHTF = MarketData.GetBars(timeFrame);
                state.CandleRangeInHTF = Math.Max(1, swingTFSeconds / chartTFSeconds);
            }

            return state;
        }

        // ===== Calculate =====
        public override void Calculate(int index)
        {
            for (int t = 0; t < 4; t++)
            {
                if (!_tfStates[t].Active) continue;
                ProcessTF(_tfStates[t], index);
            }

            if (DisplayDashboard)
                DisplayMultiTFDashboard();
        }

        private void ProcessTF(TFState tf, int index)
        {
            int minRequiredBars = Math.Min(500, SwingLookbackPeriod * tf.CandleRangeInHTF);
            if (index < minRequiredBars)
                return;

            // Reset swing counts
            tf.SwingHighCount = 0;
            tf.SwingLowCount = 0;

            // Get current HTF bar index
            var time = Bars.OpenTimes[index];
            int swingIndex = tf.BarsHTF.OpenTimes.GetIndexByTime(time);

            // Detect swings
            bool swingDetected = DetectSwings(tf, swingIndex);

            if (swingDetected)
            {
                ForceSwingAlternation(tf);
            }

            if (swingDetected)
            {
                // 1. Calculate previous trend from swings 1,2,3
                int prevDir = CalculateSwingsTrend(tf, 1, out int prevStatus);
                bool hasPrevTrend = (tf.SwingHighCount >= 4 && tf.SwingLowCount >= 4);

                // 2. Calculate raw trend from swings 0,1,2
                int rawDir = CalculateSwingsTrend(tf, 0, out int rawStatus);

                // 3. Check for CHoCH (Change of Character) using previous trend direction
                bool chochLiqSweep;
                tf.CHoCHStatus = CalculateCHoCH(tf, prevDir, hasPrevTrend, out chochLiqSweep);

                // 4. Gate trend change: require CHoCH confirmation for reversals
                tf.LiquiditySweep = false;
                GateTrendChange(tf, rawDir, rawStatus, prevDir, tf.CHoCHStatus, chochLiqSweep);

                // 5. Search for liquidity sweep (combine CHoCH sweep + swing analysis)
                tf.LiquiditySweep = tf.LiquiditySweep || CalculateLiquiditySweep(tf);

                // Draw the swings
                if (tf.DrawIcons || tf.DrawDots)
                    DrawSwingIconsDots(tf);
            }
        }

        // ===== Detect Swings =====
        private bool DetectSwings(TFState tf, int swingIndex)
        {
            bool swingFound = false;
            int middleIndex = SwingPeriod / 2;

            if (swingIndex < SwingLookbackPeriod)
                return swingFound;

            int searchStartIndex = swingIndex - middleIndex;

            for (int i = searchStartIndex; i > swingIndex - SwingLookbackPeriod + middleIndex; i--)
            {
                if (IsSwingHigh(tf.BarsHTF, i, SwingPeriod))
                {
                    int startIndex = Bars.OpenTimes.GetIndexByTime(tf.BarsHTF.OpenTimes[i]);
                    int chartIndex = startIndex;

                    if (startIndex < 0)
                        continue;

                    int endIndex = Math.Min(startIndex + tf.CandleRangeInHTF, Bars.Count - 1);
                    for (int x = startIndex; x < endIndex; x++)
                    {
                        if (Bars.HighPrices[x] > Bars.HighPrices[chartIndex])
                            chartIndex = x;
                    }

                    if (chartIndex >= 0 && tf.SwingHighCount < SwingLookbackPeriod)
                    {
                        tf.SwingHighPrices[tf.SwingHighCount++] = new Swing
                        {
                            SwingHTFOpenTime = tf.BarsHTF.OpenTimes[i],
                            SwingChartOpenTime = Bars.OpenTimes[chartIndex],
                            SwingPrice = tf.BarsHTF.HighPrices[i],
                            DisplayPrice = Bars.HighPrices[chartIndex],
                            SwingBarsIndex = startIndex
                        };
                        swingFound = true;
                    }
                }

                if (IsSwingLow(tf.BarsHTF, i, SwingPeriod))
                {
                    int startIndex = Bars.OpenTimes.GetIndexByTime(tf.BarsHTF.OpenTimes[i]);
                    int chartIndex = startIndex;

                    if (startIndex < 0)
                        continue;

                    int endIndex = Math.Min(startIndex + tf.CandleRangeInHTF, Bars.Count - 1);
                    for (int x = startIndex; x <= endIndex; x++)
                    {
                        if (Bars.LowPrices[x] < Bars.LowPrices[chartIndex])
                            chartIndex = x;
                    }

                    if (chartIndex >= 0 && tf.SwingLowCount < SwingLookbackPeriod)
                    {
                        tf.SwingLowPrices[tf.SwingLowCount++] = new Swing
                        {
                            SwingHTFOpenTime = tf.BarsHTF.OpenTimes[i],
                            SwingChartOpenTime = Bars.OpenTimes[chartIndex],
                            SwingPrice = tf.BarsHTF.LowPrices[i],
                            DisplayPrice = Bars.LowPrices[chartIndex],
                            SwingBarsIndex = startIndex
                        };
                        swingFound = true;
                    }
                }
            }

            return swingFound;
        }

        // ===== Force Swing Alternation =====
        private void ForceSwingAlternation(TFState tf)
        {
            var allSwings = new System.Collections.Generic.List<SwingPoint>();

            for (int i = 0; i < tf.SwingHighCount; i++)
                allSwings.Add(new SwingPoint { Swing = tf.SwingHighPrices[i], Type = SwingType.High });
            for (int i = 0; i < tf.SwingLowCount; i++)
                allSwings.Add(new SwingPoint { Swing = tf.SwingLowPrices[i], Type = SwingType.Low });

            allSwings = allSwings.OrderBy(s => s.Swing.SwingHTFOpenTime).ToList();

            if (allSwings.Count < 2)
                return;

            var correctedSwings = new System.Collections.Generic.List<SwingPoint>();
            correctedSwings.Add(allSwings[0]);

            for (int i = 1; i < allSwings.Count; i++)
            {
                var current = allSwings[i];
                var previous = correctedSwings[correctedSwings.Count - 1];

                if (current.Type == previous.Type)
                {
                    var missingSwing = FindMissingSwing(tf, previous, current);
                    if (missingSwing != null)
                        correctedSwings.Add(missingSwing);
                }

                correctedSwings.Add(current);
            }

            // Rebuild separate High/Low arrays (from most recent to oldest)
            tf.SwingHighCount = 0;
            tf.SwingLowCount = 0;

            for (int i = correctedSwings.Count - 1; i >= 0; i--)
            {
                var sp = correctedSwings[i];
                if (sp.Type == SwingType.High && tf.SwingHighCount < SwingLookbackPeriod)
                    tf.SwingHighPrices[tf.SwingHighCount++] = sp.Swing;
                else if (sp.Type == SwingType.Low && tf.SwingLowCount < SwingLookbackPeriod)
                    tf.SwingLowPrices[tf.SwingLowCount++] = sp.Swing;
            }
        }

        // ===== Find Missing Swing =====
        private SwingPoint FindMissingSwing(TFState tf, SwingPoint first, SwingPoint second)
        {
            int startIdx = tf.BarsHTF.OpenTimes.GetIndexByTime(first.Swing.SwingHTFOpenTime);
            int endIdx = tf.BarsHTF.OpenTimes.GetIndexByTime(second.Swing.SwingHTFOpenTime);

            if (startIdx >= endIdx || startIdx < 0 || endIdx < 0)
                return null;

            SwingType missingType = (first.Type == SwingType.High) ? SwingType.Low : SwingType.High;

            int bestIdx = startIdx + 1;
            if (bestIdx >= endIdx)
                return null;

            double bestPrice = (missingType == SwingType.High)
                ? tf.BarsHTF.HighPrices[bestIdx]
                : tf.BarsHTF.LowPrices[bestIdx];

            for (int i = startIdx + 1; i < endIdx; i++)
            {
                if (missingType == SwingType.High)
                {
                    if (tf.BarsHTF.HighPrices[i] > bestPrice)
                    {
                        bestPrice = tf.BarsHTF.HighPrices[i];
                        bestIdx = i;
                    }
                }
                else
                {
                    if (tf.BarsHTF.LowPrices[i] < bestPrice)
                    {
                        bestPrice = tf.BarsHTF.LowPrices[i];
                        bestIdx = i;
                    }
                }
            }

            int chartStartIndex = Bars.OpenTimes.GetIndexByTime(tf.BarsHTF.OpenTimes[bestIdx]);
            int chartIndex = chartStartIndex;

            if (chartStartIndex < 0)
                return null;

            int chartEndIndex = Math.Min(chartStartIndex + tf.CandleRangeInHTF, Bars.Count - 1);
            for (int x = chartStartIndex; x < chartEndIndex; x++)
            {
                if (missingType == SwingType.High)
                {
                    if (Bars.HighPrices[x] > Bars.HighPrices[chartIndex])
                        chartIndex = x;
                }
                else
                {
                    if (Bars.LowPrices[x] < Bars.LowPrices[chartIndex])
                        chartIndex = x;
                }
            }

            var newSwing = new Swing
            {
                SwingHTFOpenTime = tf.BarsHTF.OpenTimes[bestIdx],
                SwingChartOpenTime = Bars.OpenTimes[chartIndex],
                SwingPrice = (missingType == SwingType.High)
                    ? tf.BarsHTF.HighPrices[bestIdx]
                    : tf.BarsHTF.LowPrices[bestIdx],
                DisplayPrice = (missingType == SwingType.High)
                    ? Bars.HighPrices[chartIndex]
                    : Bars.LowPrices[chartIndex],
                SwingBarsIndex = chartStartIndex
            };

            return new SwingPoint { Swing = newSwing, Type = missingType };
        }

        // ===== Calculate Swings Trend =====
        private int CalculateSwingsTrend(TFState tf, int offset, out int status)
        {
            if (tf.SwingHighCount < 3 + offset || tf.SwingLowCount < 3 + offset)
            {
                status = 0;
                return NODIRECTION;
            }

            double sh0 = tf.SwingHighPrices[0 + offset].SwingPrice;
            double sh1 = tf.SwingHighPrices[1 + offset].SwingPrice;
            double sh2 = tf.SwingHighPrices[2 + offset].SwingPrice;
            double sl0 = tf.SwingLowPrices[0 + offset].SwingPrice;
            double sl1 = tf.SwingLowPrices[1 + offset].SwingPrice;
            double sl2 = tf.SwingLowPrices[2 + offset].SwingPrice;

            // PRIMARY ANALYSIS - BULLISH structure based on LOWS
            bool perfectBullish = (sl2 < sl1) && (sl1 < sl0);
            bool sweepBullish = (sl2 > sl1) && (sl0 > sl2);
            bool ll1HigherThanLL2 = sl0 > sl1;
            bool primaryBullish = (perfectBullish || sweepBullish) && ll1HigherThanLL2;
            bool ambiguousBullish = (sl2 > sl1) && (sl1 < sl0);

            // PRIMARY ANALYSIS - BEARISH structure based on HIGHS
            bool perfectBearish = (sh2 > sh1) && (sh1 > sh0);
            bool sweepBearish = (sh2 < sh1) && (sh0 < sh2);
            bool hh1LowerThanHH2 = sh0 < sh1;
            bool primaryBearish = (perfectBearish || sweepBearish) && hh1LowerThanHH2;
            bool ambiguousBearish = (sh2 < sh1) && (sh1 > sh0);

            // SECONDARY CONFIRMATION using opposite swings
            bool highsConfirmBullish = sh0 > sh1;
            bool lowsConfirmBearish = sl0 < sl1;

            if (primaryBullish)
            {
                status = highsConfirmBullish ? MOMENTUM : COMPRESSION;
                return BULLISH;
            }
            if (primaryBearish)
            {
                status = lowsConfirmBearish ? MOMENTUM : COMPRESSION;
                return BEARISH;
            }
            if (ambiguousBullish && highsConfirmBullish)
            {
                status = MOMENTUM;
                return BULLISH;
            }
            if (ambiguousBearish && lowsConfirmBearish)
            {
                status = MOMENTUM;
                return BEARISH;
            }

            status = 0;
            return NODIRECTION;
        }

        // ===== Calculate CHoCH (Dual Detection) =====
        private int CalculateCHoCH(TFState tf, int prevDir, bool hasPrevTrend, out bool chochLiquiditySweep)
        {
            chochLiquiditySweep = false;

            if (tf.SwingHighCount < 3 || tf.SwingLowCount < 3)
                return CONTINUATION;

            // Get close prices of the HTF candles that formed HH1 and LL1
            int hh1HTFIndex = tf.BarsHTF.OpenTimes.GetIndexByTime(tf.SwingHighPrices[0].SwingHTFOpenTime);
            int ll1HTFIndex = tf.BarsHTF.OpenTimes.GetIndexByTime(tf.SwingLowPrices[0].SwingHTFOpenTime);

            double hh1CandleClose = (hh1HTFIndex >= 0) ? tf.BarsHTF.ClosePrices[hh1HTFIndex] : 0;
            double ll1CandleClose = (ll1HTFIndex >= 0) ? tf.BarsHTF.ClosePrices[ll1HTFIndex] : 0;

            // CHoCH BULLISH conditions
            bool hh1AboveHH2 = tf.SwingHighPrices[0].SwingPrice > tf.SwingHighPrices[1].SwingPrice;
            bool hh1CandleClosedAboveHH2 = hh1CandleClose > tf.SwingHighPrices[1].SwingPrice;
            bool prevHighsDeclining = tf.SwingHighPrices[1].SwingPrice < tf.SwingHighPrices[2].SwingPrice;
            bool prevNotBullish = hasPrevTrend ? (prevDir != BULLISH) : prevHighsDeclining;

            bool bullishByStructure = prevHighsDeclining && hh1AboveHH2 && hh1CandleClosedAboveHH2;
            bool bullishByPrev = prevNotBullish && hh1AboveHH2 && hh1CandleClosedAboveHH2;

            // CHoCH BEARISH conditions
            bool ll1BelowLL2 = tf.SwingLowPrices[0].SwingPrice < tf.SwingLowPrices[1].SwingPrice;
            bool ll1CandleClosedBelowLL2 = ll1CandleClose < tf.SwingLowPrices[1].SwingPrice;
            bool prevLowsRising = tf.SwingLowPrices[1].SwingPrice > tf.SwingLowPrices[2].SwingPrice;
            bool prevNotBearish = hasPrevTrend ? (prevDir != BEARISH) : prevLowsRising;

            bool bearishByStructure = prevLowsRising && ll1BelowLL2 && ll1CandleClosedBelowLL2;
            bool bearishByPrev = prevNotBearish && ll1BelowLL2 && ll1CandleClosedBelowLL2;

            // DUAL CHoCH: both structural conditions true
            // Compare timestamps to determine which is more recent
            // If the most recent matches prevDir -> liquidity sweep pattern
            if (bullishByStructure && bearishByStructure)
            {
                DateTime sh0Time = tf.SwingHighPrices[0].SwingHTFOpenTime;
                DateTime sl0Time = tf.SwingLowPrices[0].SwingHTFOpenTime;

                if (sh0Time > sl0Time)
                {
                    if (hasPrevTrend && prevDir == BULLISH)
                        chochLiquiditySweep = true;

                    if (EnablePrint)
                        Print($"TF{tf.Index + 1}: DUAL CHoCH: Bullish wins (SH0 {sh0Time} > SL0 {sl0Time}), LiqSweep={chochLiquiditySweep}");
                    return CHOCH_BULLISH;
                }
                else
                {
                    if (hasPrevTrend && prevDir == BEARISH)
                        chochLiquiditySweep = true;

                    if (EnablePrint)
                        Print($"TF{tf.Index + 1}: DUAL CHoCH: Bearish wins (SL0 {sl0Time} > SH0 {sh0Time}), LiqSweep={chochLiquiditySweep}");
                    return CHOCH_BEARISH;
                }
            }

            // SINGLE CHoCH detection
            if (bullishByPrev)
                return CHOCH_BULLISH;
            if (bearishByPrev)
                return CHOCH_BEARISH;

            return CONTINUATION;
        }

        // ===== Gate Trend Change =====
        private void GateTrendChange(TFState tf, int rawDir, int rawStatus, int prevDir, int choch, bool chochLiquiditySweep)
        {
            // Dual CHoCH liquidity sweep: restore previous trend
            if (chochLiquiditySweep)
            {
                tf.Direction = prevDir;
                tf.TrendStatus = MOMENTUM;
                tf.LiquiditySweep = true;
                return;
            }

            if (prevDir == BULLISH && rawDir == BEARISH)
            {
                if (choch == CHOCH_BEARISH)
                {
                    tf.Direction = BEARISH;
                    tf.TrendStatus = rawStatus;
                }
                else
                {
                    // No CHoCH: check if HH1 > HH4 (structure still supports bullish compression)
                    if (tf.SwingHighCount >= 4 && tf.SwingHighPrices[0].SwingPrice > tf.SwingHighPrices[3].SwingPrice)
                    {
                        tf.Direction = BULLISH;
                        tf.TrendStatus = COMPRESSION;
                    }
                    else
                    {
                        tf.Direction = NODIRECTION;
                        tf.TrendStatus = 0;
                    }
                }
            }
            else if (prevDir == BEARISH && rawDir == BULLISH)
            {
                if (choch == CHOCH_BULLISH)
                {
                    tf.Direction = BULLISH;
                    tf.TrendStatus = rawStatus;
                }
                else
                {
                    // No CHoCH: check if LL1 < LL4 (structure still supports bearish compression)
                    if (tf.SwingLowCount >= 4 && tf.SwingLowPrices[0].SwingPrice < tf.SwingLowPrices[3].SwingPrice)
                    {
                        tf.Direction = BEARISH;
                        tf.TrendStatus = COMPRESSION;
                    }
                    else
                    {
                        tf.Direction = NODIRECTION;
                        tf.TrendStatus = 0;
                    }
                }
            }
            else
            {
                // No reversal, pass through raw direction
                tf.Direction = rawDir;
                tf.TrendStatus = rawStatus;
            }
        }

        // ===== Calculate Liquidity Sweep =====
        private bool CalculateLiquiditySweep(TFState tf)
        {
            if (tf.SwingHighCount < 3 || tf.SwingLowCount < 3)
                return false;

            // Get close prices of the HTF candles that formed LL1 and HH1
            int ll1HTFIndex = tf.BarsHTF.OpenTimes.GetIndexByTime(tf.SwingLowPrices[0].SwingHTFOpenTime);
            int hh1HTFIndex = tf.BarsHTF.OpenTimes.GetIndexByTime(tf.SwingHighPrices[0].SwingHTFOpenTime);
            double ll1CandleClose = (ll1HTFIndex >= 0) ? tf.BarsHTF.ClosePrices[ll1HTFIndex] : 0;
            double hh1CandleClose = (hh1HTFIndex >= 0) ? tf.BarsHTF.ClosePrices[hh1HTFIndex] : 0;

            double low0 = tf.SwingLowPrices[0].SwingPrice;
            double low1 = tf.SwingLowPrices[1].SwingPrice;
            double low2 = tf.SwingLowPrices[2].SwingPrice;
            double high0 = tf.SwingHighPrices[0].SwingPrice;
            double high1 = tf.SwingHighPrices[1].SwingPrice;
            double high2 = tf.SwingHighPrices[2].SwingPrice;

            if (tf.Direction == BULLISH)
            {
                bool case1 = (low1 < low2) && (low0 > low1) && (high0 > high1);
                bool case2 = (low0 < low1) && (ll1CandleClose > low1 || high0 > high1);
                if (case1 || case2) return true;
            }

            if (tf.Direction == BEARISH)
            {
                bool case1 = (high1 > high2) && (high0 < high1) && (low0 < low1);
                bool case2 = (high0 > high1) && (hh1CandleClose < high1 || low0 < low1);
                if (case1 || case2) return true;
            }

            return false;
        }

        // ===== Draw Swing Icons & Dots =====
        private void DrawSwingIconsDots(TFState tf)
        {
            string prefix = $"Swing_TF{tf.Index}";

            // Remove existing objects for this TF
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith(prefix)).ToList())
                Chart.RemoveObject(obj.Name);

            // Dynamic icon gap based on visible chart height
            double chartHeight = Chart.TopY - Chart.BottomY;
            double iconGap = chartHeight * (SwingIconGapPercent / 100.0);

            if (tf.DrawIcons)
            {
                for (int i = 0; i < tf.SwingHighCount; i++)
                {
                    var swing = tf.SwingHighPrices[i];
                    Chart.DrawIcon($"{prefix}_H_{swing.SwingBarsIndex}", ChartIconType.DownArrow,
                        swing.SwingChartOpenTime, swing.DisplayPrice + iconGap, tf.HighColor);
                }

                for (int i = 0; i < tf.SwingLowCount; i++)
                {
                    var swing = tf.SwingLowPrices[i];
                    Chart.DrawIcon($"{prefix}_L_{swing.SwingBarsIndex}", ChartIconType.UpArrow,
                        swing.SwingChartOpenTime, swing.DisplayPrice - iconGap, tf.LowColor);
                }
            }

            if (tf.DrawDots)
            {
                for (int i = 0; i < tf.SwingHighCount; i++)
                {
                    var swing = tf.SwingHighPrices[i];
                    Chart.DrawTrendLine($"{prefix}_DH_{swing.SwingBarsIndex}",
                        swing.SwingChartOpenTime, swing.DisplayPrice,
                        swing.SwingChartOpenTime, swing.DisplayPrice + Symbol.PipSize,
                        Color.Yellow, 3, LineStyle.Solid);
                }

                for (int i = 0; i < tf.SwingLowCount; i++)
                {
                    var swing = tf.SwingLowPrices[i];
                    Chart.DrawTrendLine($"{prefix}_DL_{swing.SwingBarsIndex}",
                        swing.SwingChartOpenTime, swing.DisplayPrice,
                        swing.SwingChartOpenTime, swing.DisplayPrice - Symbol.PipSize,
                        Color.Yellow, 3, LineStyle.Solid);
                }
            }
        }

        // ===== Display Multi-TF Dashboard =====
        private void DisplayMultiTFDashboard()
        {
            // Remove existing dashboard objects
            foreach (var obj in Chart.Objects.Where(o => o.Name.StartsWith("MTF_")).ToList())
                Chart.RemoveObject(obj.Name);

            // Header
            var header = Chart.DrawStaticText("MTF_Header", "JCO Swings Trend Multi TF",
                VerticalAlignment.Top, HorizontalAlignment.Right, Color.SlateGray);
            header.FontSize = DashboardFontSize;

            // Column headers
            string expansionHeader = DisplayExpansion ? "\tExp" : "";
            var colHeaders = Chart.DrawStaticText("MTF_ColHeaders", $"\n\tTrend\tCHoCH\tSweep{expansionHeader}",
                VerticalAlignment.Top, HorizontalAlignment.Right, Color.DimGray);
            colHeaders.FontSize = DashboardFontSize;

            // Sort TFs by timeframe duration (ascending) for display
            var sortedIndices = Enumerable.Range(0, 4)
                .OrderBy(t => GetTimeFrameInSeconds(_tfStates[t].TimeFrame))
                .ToArray();

            // Pre-compute expansion strings and find max width for alignment
            var expansionStrings = new string[4];
            int maxExpLen = 3; // minimum "N/A" length

            if (DisplayExpansion)
            {
                for (int i = 0; i < 4; i++)
                {
                    var tf = _tfStates[i];
                    if (tf.Active && tf.SwingHighCount > 0 && tf.SwingLowCount > 0)
                    {
                        double expansion = (tf.SwingHighPrices[0].SwingPrice - tf.SwingLowPrices[0].SwingPrice) / Symbol.PipSize;
                        expansionStrings[i] = expansion.ToString("F1");
                    }
                    else
                    {
                        expansionStrings[i] = null;
                    }

                    if (expansionStrings[i] != null && expansionStrings[i].Length > maxExpLen)
                        maxExpLen = expansionStrings[i].Length;
                }
            }

            // Per-TF lines (sorted by TF ascending)
            for (int row = 0; row < 4; row++)
            {
                int t = sortedIndices[row];
                var tf = _tfStates[t];
                string newlines = new string('\n', row + 2); // +2 for header + column headers
                string tfLabel = GetTFLabel(tf.TimeFrame);
                string tfPrefix = $"TF{t + 1} {tfLabel}";

                if (!tf.Active)
                {
                    bool enabled = (t == 0) || (t == 1 && EnableTF2) || (t == 2 && EnableTF3) || (t == 3 && EnableTF4);
                    string status = enabled ? "N/A" : "OFF";
                    string expCol = DisplayExpansion ? $"\t{"".PadLeft(maxExpLen + 1, '_')}" : "";
                    string msg = $"{tfPrefix}:\t{status}\t\t{expCol}";
                    var offText = Chart.DrawStaticText($"MTF_TF{t}", newlines + msg,
                        VerticalAlignment.Top, HorizontalAlignment.Right, Color.Gray);
                    offText.FontSize = DashboardFontSize;
                    continue;
                }

                // Build trend text
                string trendText;
                Color lineColor;

                if (tf.Direction == BULLISH)
                {
                    string statusChar = tf.TrendStatus == MOMENTUM ? "M" : "C";
                    trendText = $"Bull ({statusChar})";
                    lineColor = Color.LimeGreen;
                }
                else if (tf.Direction == BEARISH)
                {
                    string statusChar = tf.TrendStatus == MOMENTUM ? "M" : "C";
                    trendText = $"Bear ({statusChar})";
                    lineColor = Color.Red;
                }
                else
                {
                    trendText = "?";
                    lineColor = Color.Orange;
                }

                // Build CHoCH text
                string chochText = tf.CHoCHStatus == CHOCH_BULLISH ? "Bull"
                    : tf.CHoCHStatus == CHOCH_BEARISH ? "Bear"
                    : "Cont";

                // Build sweep text
                string sweepText = tf.LiquiditySweep ? "Yes" : "No";

                // Build line with tabs for column alignment
                string line = $"{tfPrefix}:\t{trendText}\t{chochText}\t{sweepText}";

                // Add expansion if enabled (padded to max width across all TFs)
                if (DisplayExpansion)
                {
                    string expValue = expansionStrings[t] != null
                        ? expansionStrings[t].PadLeft(maxExpLen, '_') + "p"
                        : "N/A".PadLeft(maxExpLen, '_') + " ";
                    line += $"\t{expValue}";
                }

                var tfText = Chart.DrawStaticText($"MTF_TF{t}", newlines + line,
                    VerticalAlignment.Top, HorizontalAlignment.Right, lineColor);
                tfText.FontSize = DashboardFontSize;
            }
        }

        // ===== Utility: Get TF Label =====
        private string GetTFLabel(TimeFrame tf)
        {
            string s = tf.ToString();
            switch (s)
            {
                case "Minute": return "M1 ";
                case "Minute2": return "M2 ";
                case "Minute3": return "M3 ";
                case "Minute4": return "M4 ";
                case "Minute5": return "M5 ";
                case "Minute10": return "M10";
                case "Minute15": return "M15";
                case "Minute20": return "M20";
                case "Minute30": return "M30";
                case "Minute45": return "M45";
                case "Hour": return "H1 ";
                case "Hour2": return "H2 ";
                case "Hour3": return "H3 ";
                case "Hour4": return "H4 ";
                case "Hour6": return "H6 ";
                case "Hour8": return "H8 ";
                case "Hour12": return "H12";
                case "Daily": return "D1 ";
                case "Weekly": return "W1 ";
                case "Monthly": return "MN ";
                default: return s.Length > 3 ? s.Substring(0, 3) : s.PadRight(3);
            }
        }

        // ===== Utility: IsSwingHigh =====
        private bool IsSwingHigh(Bars candleArray, int index, int period)
        {
            int middleIndex = period / 2;
            double middleValue = candleArray.HighPrices[index];

            if (index - middleIndex < 0)
                return false;

            for (int i = 0; i < period; i++)
            {
                if (i != middleIndex && candleArray.HighPrices[index - middleIndex + i] >= middleValue)
                    return false;
            }
            return true;
        }

        // ===== Utility: IsSwingLow =====
        private bool IsSwingLow(Bars candleArray, int index, int period)
        {
            int middleIndex = period / 2;
            double middleValue = candleArray.LowPrices[index];

            if (index - middleIndex < 0)
                return false;

            for (int i = 0; i < period; i++)
            {
                if (i != middleIndex && candleArray.LowPrices[index - middleIndex + i] <= middleValue)
                    return false;
            }
            return true;
        }

        // ===== Utility: GetTimeFrameInSeconds =====
        private int GetTimeFrameInSeconds(TimeFrame timeFrame)
        {
            switch (timeFrame.ToString())
            {
                case "Minute": return 60;
                case "Minute2": return 2 * 60;
                case "Minute3": return 3 * 60;
                case "Minute4": return 4 * 60;
                case "Minute5": return 5 * 60;
                case "Minute10": return 10 * 60;
                case "Minute15": return 15 * 60;
                case "Minute20": return 20 * 60;
                case "Minute30": return 30 * 60;
                case "Minute45": return 45 * 60;
                case "Hour": return 60 * 60;
                case "Hour2": return 2 * 60 * 60;
                case "Hour3": return 3 * 60 * 60;
                case "Hour4": return 4 * 60 * 60;
                case "Hour6": return 6 * 60 * 60;
                case "Hour8": return 8 * 60 * 60;
                case "Hour12": return 12 * 60 * 60;
                case "Daily": return 24 * 60 * 60;
                case "Weekly": return 7 * 24 * 60 * 60;
                case "Monthly": return 30 * 24 * 60 * 60;
                default:
                    throw new ArgumentException("Unsupported timeframe: " + timeFrame.ToString());
            }
        }
    }
}
