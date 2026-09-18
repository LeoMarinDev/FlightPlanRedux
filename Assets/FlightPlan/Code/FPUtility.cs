using KSP.Game;
using KSP.Messages;
using KSP.Sim.impl;
using KSP.Sim.Maneuver;
using System;
using System.Linq;

namespace FPUtilities
{

    public static class FPUtility
    {
        public static VesselComponent ActiveVessel;
        public static ManeuverNodeData CurrentNode;
        public static GameStateConfiguration GameState;
        public static MessageCenter MessageCenter;
        public static string InputDisableWindowAbbreviation = "WindowAbbreviation";
        public static string InputDisableWindowName = "WindowName";

        /// <summary>
        /// Refreshes the ActiveVessel and CurrentNode
        /// </summary>
        public static void RefreshActiveVesselAndCurrentManeuver()
        {
            ActiveVessel = GameManager.Instance?.Game?.ViewController?.GetActiveVehicle(true)?.GetSimVessel(true);
            CurrentNode = ActiveVessel != null ? GameManager.Instance?.Game?.SpaceSimulation.Maneuvers.GetNodesForVessel(ActiveVessel.GlobalId).FirstOrDefault() : null;
        }

        public static void RefreshGameManager()
        {
            GameState = GameManager.Instance?.Game?.GlobalGameState?.GetGameState();
        }

        // The legacy GetModVersion()/IsModOlderThan() pair (legacy :216-273) is deleted: it walked
        // BepInEx's `Chainloader.Plugins` for a `BaseSpaceWarpPlugin` and read its
        // `SpaceWarpMetadata.ModID`/`.Version`. Neither the loader nor the base type exists on Redux.
        // Mod discovery is `SpaceWarp2.API.Mods.PluginList` now - see FPOtherModsInterface.

        public static string MetersToDistanceString(double heightInMeters)
        {
            return $"{heightInMeters:N0}";
        }

        public static string MetersToScaledDistanceString(double heightInMeters, int decimalPlaces = 0)
        {
            string distance;
            string formatString = string.Concat("{0:N", decimalPlaces, "}");
            if (heightInMeters < 1e3)
            {
                distance = string.Format(formatString, heightInMeters) + " m";
            }
            else if (heightInMeters < 1e6)
            {
                distance = string.Format(formatString, heightInMeters / 1e3) + " km";
            }
            else if (heightInMeters < 1e9)
            {
                distance = string.Format(formatString, heightInMeters / 1e6) + " Mm";
            }
            else
            {
                distance = string.Format(formatString, heightInMeters / 1e9) + " Gm";
            }
            return distance;
        }

        public static string SecondsToTimeString(double seconds, bool addSpacing = true, bool returnLastUnit = false, bool omitFractionalSeconds = false)
        {
            if (seconds == double.PositiveInfinity)
            {
                return "∞";
            }
            else if (seconds == double.NegativeInfinity)
            {
                return "-∞";
            }

            double _cap = Math.Floor(seconds);

            string _result = "";
            string _spacing = "";
            if (addSpacing)
            {
                _spacing = " ";
            }

            if (seconds < 0)
            {
                _result += "-";
                seconds = Math.Abs(seconds);
            }

            int _secPerYear = 21600 * 426 + 32 * 60;
            int _years = (int)(_cap / _secPerYear);
            int _days = (int)((_cap - (_years * _secPerYear)) / 21600);
            int _hours = (int)((_cap - (_days * 21600) - (_years * _secPerYear)) / 3600);
            int _minutes = (int)((_cap - (_hours * 3600) - (_days * 21600) - (_years * _secPerYear)) / 60);
            double _secs = (seconds - (_years * _secPerYear) - (_days * 21600) - (_hours * 3600) - (_minutes * 60));

            if (_years > 0)
            {
                _result += $"{_years}{_spacing}y ";
            }

            if (_days > 0)
            {
                _result += $"{_days}{_spacing}d ";
            }

            if (_hours > 0 || _days > 0)
            {
                {
                    _result += $"{_hours}:";
                }
            }

            if (_minutes > 0 || _hours > 0 || _days > 0)
            {
                if (_hours > 0 || _days > 0)
                {
                    _result += $"{_minutes:00.}:";
                }
                else
                {
                    _result += $"{_minutes}:";
                }
            }

            if (_minutes > 0 || _hours > 0 || _days > 0)
            {
                if (omitFractionalSeconds)
                    _result += returnLastUnit ? $"{_secs:00}{_spacing}" : $"{_secs:00}";
                else
                    _result += returnLastUnit ? $"{_secs:00.00}{_spacing}" : $"{_secs:00.00}";
            }
            else
            {
                if (omitFractionalSeconds)
                    _result += returnLastUnit ? $"{_secs:00}{_spacing}" : $"{_secs:00}";
                else
                    _result += returnLastUnit ? $"{_secs:00.00}{_spacing}" : $"{_secs:00.00}";
            }

            return _result;
        }

        /// <summary>
        /// Check if current vessel has an active target (celestial body or vessel)
        /// </summary>
        /// <returns></returns>
        public static bool TargetExists()
        {
            try { return (ActiveVessel.TargetObject != null); }
            catch { return false; }
        }

        /// <summary>
        /// Checks if current vessel has a maneuver
        /// </summary>
        /// <returns></returns>
        public static bool ManeuverExists()
        {
            try { return (GameManager.Instance?.Game?.SpaceSimulation.Maneuvers.GetNodesForVessel(ActiveVessel.GlobalId).FirstOrDefault() != null); }
            catch { return false; }
        }
    }
}
