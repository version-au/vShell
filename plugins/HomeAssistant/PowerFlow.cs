using System;

namespace vShell.Core
{
    /// <summary>Where the power is going right now, in watts (all 0 or more).</summary>
    public sealed class PowerFlows
    {
        public double Solar, GridImport, GridExport, BatteryCharge, BatteryDischarge, Home;
        public double SolarToHome, SolarToBattery, SolarToGrid;
        public double GridToHome, GridToBattery;
        public double BatteryToHome, BatteryToGrid;
        /// <summary>Battery charge in percent, or NaN when not known.</summary>
        public double BatterySoc = double.NaN;
        public bool HasSolar, HasGrid, HasBattery;
    }

    /// <summary>
    /// Splits the measured powers into the flows between solar, grid, battery and home - the same way the Home Assistant power flow
    /// cards do: solar feeds the home first, then the battery, then the grid; the battery feeds the home before the grid; the grid
    /// covers what is left of the home and the battery.
    /// </summary>
    public static class PowerFlowMath
    {
        /// <param name="battery">Battery power, positive = charging.</param>
        /// <param name="home">Measured home use, or NaN to work it out.</param>
        public static PowerFlows Compute(double solar, double gridImport, double gridExport, double battery, double home)
        {
            var f = new PowerFlows();
            f.Solar = Pos(solar);
            f.GridImport = Pos(gridImport);
            f.GridExport = Pos(gridExport);
            double b = double.IsNaN(battery) ? 0 : battery;
            f.BatteryCharge = Math.Max(0, b);
            f.BatteryDischarge = Math.Max(0, -b);

            double worked = f.Solar + f.GridImport + f.BatteryDischarge - f.GridExport - f.BatteryCharge;
            f.Home = double.IsNaN(home) ? Math.Max(0, worked) : Math.Max(0, home);

            double solarLeft = f.Solar;
            f.SolarToHome = Math.Min(solarLeft, f.Home);
            solarLeft -= f.SolarToHome;
            f.SolarToBattery = Math.Min(solarLeft, f.BatteryCharge);
            solarLeft -= f.SolarToBattery;
            f.SolarToGrid = Math.Min(solarLeft, f.GridExport);

            f.GridToBattery = Math.Min(f.GridImport, Math.Max(0, f.BatteryCharge - f.SolarToBattery));
            f.BatteryToHome = Math.Min(f.BatteryDischarge, Math.Max(0, f.Home - f.SolarToHome));
            f.BatteryToGrid = Math.Min(Math.Max(0, f.BatteryDischarge - f.BatteryToHome), Math.Max(0, f.GridExport - f.SolarToGrid));
            f.GridToHome = Math.Min(Math.Max(0, f.GridImport - f.GridToBattery), Math.Max(0, f.Home - f.SolarToHome - f.BatteryToHome));
            return f;
        }

        static double Pos(double v) { return double.IsNaN(v) || v < 0 ? 0 : v; }

        /// <summary>"640 W", "2.2 kW", "12 kW".</summary>
        public static string Format(double w)
        {
            if (double.IsNaN(w)) return "--";
            double a = Math.Abs(w);
            if (a < 1000) return Math.Round(w).ToString("0", System.Globalization.CultureInfo.CurrentCulture) + " W";
            return (w / 1000.0).ToString(a < 10000 ? "0.0" : "0", System.Globalization.CultureInfo.CurrentCulture) + " kW";
        }

        /// <summary>A Home Assistant power reading in watts (W, kW and MW understood), or NaN.</summary>
        public static double Watts(HaState s)
        {
            double v;
            if (s == null || s.Unavailable || !s.TryNumber(out v)) return double.NaN;
            string u = (s.Unit ?? "").Trim();
            if (u.Equals("kW", StringComparison.OrdinalIgnoreCase)) return v * 1000;
            if (u.Equals("MW", StringComparison.OrdinalIgnoreCase)) return v * 1000000;
            return v;
        }
    }
}
