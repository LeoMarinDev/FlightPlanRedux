using KSP.Game;
using UnityEngine.InputSystem;
using UnityEngine.UIElements;
using System.Collections.Generic;

namespace FlightPlan.API.Extensions
{

    public static class UIToolkitExtensions
    {
        // Legacy: `BepInEx.Logging.Logger.CreateLogSource("FP.UIToolkitExtensions")`.
        // The ReduxLib analogue is the static `ReduxLib.ReduxLib.GetLogger(name)` factory
        // (doc-silent: proven from the pinned source checkout and used across OrbitalSurvey).
        private static readonly ReduxLib.Logging.ILogger _Logger =
            ReduxLib.ReduxLib.GetLogger("FlightPlan|UIToolkitExtensions");

        // Legacy captured `GameManager.Instance.Game` here. Now the plugin's null-safe accessor.
        private static GameInstance Game => FlightPlanPlugin.Game;

        private static List<InputAction> _maskedInputActions = new List<InputAction>();

        private static List<InputAction> MaskedInputActions
        {
            get
            {
                if (_maskedInputActions.Count == 0)
                    _maskedInputActions =
                    new List<InputAction>
                    {
                        Game.Input.Flight.CameraZoom,
                        Game.Input.Flight.mouseDoubleTap,
                        Game.Input.Flight.mouseSecondaryTap,

                        Game.Input.MapView.cameraZoom,
                        Game.Input.MapView.Focus,
                        Game.Input.MapView.mousePrimary,
                        Game.Input.MapView.mouseSecondary,
                        Game.Input.MapView.mouseTertiary,
                        Game.Input.MapView.mousePosition,

                        Game.Input.VAB.cameraZoom,
                        Game.Input.VAB.mousePrimary,
                        Game.Input.VAB.mouseSecondary
                    };

                return _maskedInputActions;
            }
        }

        private static readonly Dictionary<int, bool> MaskedInputActionsState = new();

        /// <summary>
        /// Stop the mouse events (scroll and click) from propagating to the game (e.g. zoom).
        /// The only place where the Click still doesn't get stopped is in the MapView, neither the Focus or the Orbit mouse events.
        /// </summary>
        public static void StopMouseEventsPropagation(this VisualElement element)
        {
            element.RegisterCallback<PointerEnterEvent>(OnVisualElementPointerEnter);
            element.RegisterCallback<PointerLeaveEvent>(OnVisualElementPointerLeave);
        }

        private static void OnVisualElementPointerEnter(PointerEnterEvent evt)
        {
            for (var i = 0; i < MaskedInputActions.Count; i++)
            {
                var inputAction = MaskedInputActions[i];
                MaskedInputActionsState[i] = inputAction.enabled;
                inputAction.Disable();
            }
        }

        private static void OnVisualElementPointerLeave(PointerLeaveEvent evt)
        {
            for (var i = 0; i < MaskedInputActions.Count; i++)
            {
                var inputAction = MaskedInputActions[i];
                if (MaskedInputActionsState[i])
                    inputAction.Enable();
            }
        }
    }
}
