# JCO Swings Trend Multi TF (cTrader)

A cTrader/cAlgo indicator that detects swing highs/lows on up to 4 configurable timeframes and determines trend direction based on swing structure.

Based on the [TradingView version](https://github.com/jcornierfra/TradingView_Indicator_JCO_Swings_Trend_Multi_TF) (Pine Script v6) and the cTrader single-TF version [JCO Swings Trend HTF](https://github.com/jcornierfra/cTrader_Indicator_JCO_Swings_Trend_HTF).

## Features

- **Multi-timeframe analysis** - 1 primary + 3 optional secondary timeframes
- **Trend detection** - Bullish/Bearish with Momentum or Compression status
- **CHoCH-gated trend reversals** - Prevents premature trend changes by requiring close price confirmation
- **Dual CHoCH detection** - Detects both bullish and bearish CHoCH simultaneously
- **Liquidity sweep detection** - With close price confirmation and dual CHoCH restoration
- **Swing alternation** - Automatic missing swing insertion when consecutive same-type pivots occur
- **Multi-line dashboard** - Real-time overview of all active TF analyses, sorted by timeframe ascending
- **Configurable icon colors** - Per-TF color selection via dropdown

## Installation

1. Copy the `.cs` file into your cTrader indicators source folder:
   `Documents\cAlgo\Sources\Indicators\`
2. Open cTrader, go to **Automate** > **Indicators**
3. Build the indicator (or it will auto-build)
4. Add to any chart from the indicator list

Alternatively, open the `.sln` file in Visual Studio or the cTrader code editor.

## Parameters

### General

| Parameter | Default | Description |
|-----------|---------|-------------|
| Swing Period | 5 | Number of bars for pivot detection (left/right = period/2) |
| Swing Lookback Period | 200 | Number of HTF bars to scan for swings |
| Icon Gap (%) | 2.0 | Distance between swing icon and price level (% of chart height) |
| Display Dashboard | true | Show/hide the multi-line dashboard |
| Display Expansion | true | Show/hide expansion in dashboard |
| Dashboard Font Size | 10 | Font size for dashboard text (6-20) |
| Enable Print | false | Enable debug logging to cTrader log |

### TF1 (Primary - always enabled)

| Parameter | Default | Description |
|-----------|---------|-------------|
| Timeframe | Hour (H1) | Primary analysis timeframe |
| Draw Icons | true | Show swing high (red) / low (green) icons |
| Draw Dots | false | Show yellow dots at swing points |

### TF2, TF3, TF4 (Secondary - optional)

| Parameter | Default TF2 / TF3 / TF4 | Description |
|-----------|--------------------------|-------------|
| Enable | false | Enable/disable this timeframe |
| Timeframe | Hour4 / Daily / Weekly | Analysis timeframe |
| Draw Icons | true | Show swing icons |
| Icon Color | Orange / Purple / Aqua | Single color for both high and low icons |

Available icon colors: Orange, Purple, Aqua, Blue, Yellow, DodgerBlue, Magenta, White.

## Detection Logic

### 1. Swing Detection & Alternation

Swings are detected using a fractal-based approach: a bar is a swing high if its high is the highest in a window of `SwingPeriod` bars. Detection occurs on HTF bars, then the exact chart candle with the wick is located for precise icon placement.

The indicator enforces **strict alternation** between swing highs and lows:

- Swings must alternate: High, Low, High, Low...
- When two consecutive highs (or lows) are detected, the indicator automatically inserts a **missing swing** by finding the lowest low (or highest high) between the two same-type pivots
- If no missing swing can be inserted and the new pivot extends the current one, the current swing is updated

Each timeframe maintains its own `TFState` tracking swing arrays, trend direction, CHoCH status, and liquidity sweep flags independently.

### 2. Trend Detection

The trend is determined from the 3 most recent swing highs (`sh0, sh1, sh2`) and swing lows (`sl0, sl1, sl2`):

**Bullish patterns** (based on swing lows):
- **Perfect**: `sl2 < sl1 < sl0` (3 consecutive higher lows)
- **Sweep**: `sl2 > sl1` then `sl0 > sl2` (middle low dips below sl2 but last recovers above)
- Both require `sl0 > sl1` (last low higher than previous)

**Bearish patterns** (based on swing highs):
- **Perfect**: `sh2 > sh1 > sh0` (3 consecutive lower highs)
- **Sweep**: `sh2 < sh1` then `sh0 < sh2` (middle high exceeds sh2 but last drops below)
- Both require `sh0 < sh1` (last high lower than previous)

**Ambiguous patterns** - accepted only when opposite swings confirm:
- Ambiguous Bullish + `sh0 > sh1` (highs confirm) -> Bullish
- Ambiguous Bearish + `sl0 < sl1` (lows confirm) -> Bearish

**Status**:
- **Momentum (M)**: Opposite swings confirm the direction
- **Compression (C)**: Direction established but opposite swings don't confirm yet

**Priority**: Primary Bullish > Primary Bearish > Ambiguous Bullish > Ambiguous Bearish > No Direction (?)

### 3. CHoCH - Change of Character (Dual Detection)

Detects potential trend reversals using **dual detection**: both bullish and bearish CHoCH are evaluated simultaneously. Requires both **price break** and **close confirmation**:

**CHoCH Bullish** conditions:
1. `sh0 > sh1` - New higher high (price break)
2. Close of HH1 candle > `sh1` - Close confirms above previous high
3. Previous highs were declining (`sh1 < sh2`) OR previous trend was not bullish

**CHoCH Bearish** conditions:
1. `sl0 < sl1` - New lower low (price break)
2. Close of LL1 candle < `sl1` - Close confirms below previous low
3. Previous lows were rising (`sl1 > sl2`) OR previous trend was not bearish

**Dual CHoCH resolution**: When both bullish and bearish structural conditions are true simultaneously, the **most recent swing** wins (by comparing swing timestamps). If the winning CHoCH matches the previous trend direction, the opposing CHoCH was a **liquidity sweep**.

### 4. Gated Trend Change

Prevents premature trend reversals by requiring CHoCH confirmation before accepting a direction change:

| Previous | Raw | CHoCH confirms? | Larger structure holds? | Result |
|----------|-----|-----------------|------------------------|--------|
| any | any | Dual CHoCH liq sweep | - | **prevDir (M)** + liq sweep |
| Bullish | Bearish | Yes (CHoCH Bearish) | - | **Bearish** (accept) |
| Bullish | Bearish | No | `sh0 > sh3` | **Bullish (C)** (maintain) |
| Bullish | Bearish | No | `sh0 <= sh3` | **? (unknown)** |
| Bearish | Bullish | Yes (CHoCH Bullish) | - | **Bullish** (accept) |
| Bearish | Bullish | No | `sl0 < sl3` | **Bearish (C)** (maintain) |
| Bearish | Bullish | No | `sl0 >= sl3` | **? (unknown)** |

When the trend is maintained without CHoCH, the status is forced to **Compression (C)** to signal the structure is under pressure.

### 5. Liquidity Sweep

Detects when price sweeps beyond a previous swing level then reverses, suggesting institutional liquidity collection:

**Bullish liquidity sweep** (in bullish trend):
- Case 1: Previous low broke structure (`sl1 < sl2`) but price recovered (`sl0 > sl1, sh0 > sh1`)
- Case 2: Current low broke previous (`sl0 < sl1`) but close recovered above or highs confirm

**Bearish liquidity sweep** (in bearish trend):
- Case 1: Previous high broke structure (`sh1 > sh2`) but price reversed down (`sh0 < sh1, sl0 < sl1`)
- Case 2: Current high broke previous (`sh0 > sh1`) but close rejected below or lows confirm

Liquidity sweep is the **union** of:
- **Gated liquidity sweep**: from dual CHoCH detection
- **Structural liquidity sweep**: from swing analysis using the gated trend direction

### 6. Expansion

Measures the distance between the most recent swing high and swing low in pips:

```
Expansion = (SwingHigh[0] - SwingLow[0]) / PipSize
```

Display can be toggled on/off. Values are dynamically padded for dashboard alignment.

## Dashboard

The dashboard displays a multi-line overlay in the top-right corner, sorted by timeframe ascending:

```
JCO Swings Trend Multi TF
          Trend     CHoCH   Sweep   Exp
TF2 M15:  Bull (M)  Cont    No      __43.1p
TF1 H1 :  Bear (C)  Bear    Yes     _120.5p
TF3 H4 :  Bull (M)  Cont    No      3500.0p
TF4 W1 :  OFF
```

- Each line is colored by its trend direction (green=bullish, red=bearish, orange=unclear, gray=off)
- TF prefix shows which parameter group the timeframe belongs to (TF1, TF2, etc.)
- Lines are sorted by timeframe duration, not by TF number
- Expansion values use `_` padding for alignment with proportional fonts
- Font size is configurable via parameter

## Architecture

The indicator uses a `TFState` class to encapsulate all per-timeframe state:

```csharp
private class TFState
{
    public TimeFrame TimeFrame;
    public bool Active;
    public Bars BarsHTF;
    public Swing[] SwingHighPrices, SwingLowPrices;
    public int Direction, TrendStatus, CHoCHStatus;
    public bool LiquiditySweep;
    // ...
}
```

All detection methods (`DetectSwings`, `CalculateSwingsTrend`, `CalculateCHoCH`, `GateTrendChange`, `CalculateLiquiditySweep`) take a `TFState` parameter and operate independently per timeframe. This makes the logic modular and easy to maintain - changes to any detection algorithm automatically apply to all 4 TFs.

## Known Limitations

- Chart timeframe must be <= swing timeframe for valid results (inactive TFs show "N/A")
- `DrawStaticText` does not support per-cell coloring - each dashboard line has a single color based on trend direction
- Dashboard uses proportional fonts - column alignment uses tabs and `_` padding as workaround
- All swings are recalculated from scratch on every bar (no incremental state), which may be slower on very low timeframes with all 4 TFs active
- `AccessRights.None` - no WPF custom controls (which would require `FullAccess`)

## Differences from TradingView Version

| Feature | TradingView | cTrader |
|---------|-------------|---------|
| Dashboard | Built-in `table.new()` with per-cell colors | `DrawStaticText` with per-line color |
| Swing detection | `ta.pivothigh()` / `ta.pivotlow()` | Custom fractal-based `IsSwingHigh()` / `IsSwingLow()` |
| HTF data | `request.security()` | `MarketData.GetBars()` |
| State management | Persistent `var` variables | Recalculated from scratch each bar |
| Close price for CHoCH | Best close in 5-candle window around pivot | Single HTF candle close |
| Draw dots | Not available | Optional yellow dots at swing points |
| Icon colors | Color picker input | Enum dropdown (8 preset colors) |
| Font size | Not configurable | Configurable parameter |

## Changelog

### v2.0 - 2026-02-10
- Multi-timeframe support: 4 TFs (1 primary + 3 optional secondary)
- `TFState` class encapsulating per-TF state for clean multi-TF processing
- Multi-line dashboard with trend, CHoCH, liquidity sweep, expansion per TF
- Dashboard sorted by timeframe ascending with TF prefix labels
- Configurable dashboard font size
- TF1: red/green icons (hardcoded); TF2-4: single configurable color per TF via enum dropdown
- Display expansion as toggle parameter
- Expansion values dynamically padded with `_` for alignment
- All analysis logic runs independently per TF
- Based on cTrader JCO Swings Trend HTF v1.7 (single TF) and TradingView v2.2 (multi TF)

## License

This code is subject to the terms of the [Mozilla Public License 2.0](https://mozilla.org/MPL/2.0/).
