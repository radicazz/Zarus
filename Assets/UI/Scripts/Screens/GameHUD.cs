using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Zarus.Map;
using Zarus.Systems;

namespace Zarus.UI
{
    /// <summary>
    /// Controls the modern bottom bar HUD with essential global information.
    /// Province-specific details are now handled by the diegetic ProvincePanelController.
    /// </summary>
    public class GameHUD : UIScreen
    {
        [Header("Game References")]
        [SerializeField]
        private RegionMapController mapController;

        [Header("Time Source")]
        [SerializeField]
        private DayNightCycleController dayNightController;

        [Header("Outbreak Simulation")]
        [SerializeField]
        private OutbreakSimulationController outbreakSimulation;

        [Header("Upgrade Panel")]
        [SerializeField]
        private VisualTreeAsset upgradePanelTemplate;

        // UI Elements (Bottom Bar Design)
        private Label infectionSummaryLabel;
        private Label cureSummaryLabel;
        private Label zarBalanceLabel;
        private Label timerValue;
        private Label timerSubValueLabel;
        private Label timerDetailLabel;
        private VisualElement incomeToast;
        private Label incomeToastText;
        private VisualElement modalHost;
        private UpgradePanelController upgradePanelController;
        private IVisualElementScheduledItem scheduledToastHide;

        // Game State
        private InGameTimeSnapshot latestTimeSnapshot;
        private bool hasTimeSnapshot;
        private GlobalCureState latestGlobalState;
        private bool simulationEventsHooked;

        private const float TimeScaleDisplayBaseline = 30f;

        protected override void Initialize()
        {
            if (uiDocument == null)
            {
                Debug.LogError("[GameHUD] UIDocument is null! Assign it in the Inspector.");
                return;
            }
            
            var root = uiDocument.rootVisualElement;
            if (root == null)
            {
                Debug.LogError("[GameHUD] UIDocument root element is null!");
                return;
            }

            Debug.Log($"[GameHUD] Initializing modern bottom bar HUD...");

            // Query UI elements for bottom bar design
            timerValue = root.Q<Label>("TimerValue");
            timerSubValueLabel = root.Q<Label>("TimerSubValue");
            timerDetailLabel = root.Q<Label>("TimerDetail");
            infectionSummaryLabel = root.Q<Label>("InfectionSummaryLabel");
            cureSummaryLabel = root.Q<Label>("CureSummaryLabel");
            zarBalanceLabel = root.Q<Label>("ZarBalanceLabel");
            incomeToast = root.Q<VisualElement>("IncomeToast");
            incomeToastText = root.Q<Label>("IncomeToastText");
            modalHost = root.Q<VisualElement>("ModalHost");

            // Verify essential elements
            if (timerValue == null) Debug.LogWarning("[GameHUD] TimerValue not found - time display may not work");
            if (infectionSummaryLabel == null) Debug.LogWarning("[GameHUD] InfectionSummaryLabel not found - infection status may not display");
            if (cureSummaryLabel == null) Debug.LogWarning("[GameHUD] CureSummaryLabel not found - cure status may not display");
            if (zarBalanceLabel == null) Debug.LogWarning("[GameHUD] ZarBalanceLabel not found - budget may not display");

            // Find components if not assigned
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }

            if (dayNightController == null)
            {
                dayNightController = FindFirstObjectByType<DayNightCycleController>();
            }

            if (dayNightController == null)
            {
                var bootstrapGo = new GameObject("DayNightCycleAuto");
                dayNightController = bootstrapGo.AddComponent<DayNightCycleController>();
            }

            if (dayNightController != null)
            {
                dayNightController.TimeUpdated += HandleTimeUpdated;
                if (dayNightController.HasTime)
                {
                    HandleTimeUpdated(dayNightController.CurrentTime);
                }
            }
            else
            {
                Debug.LogWarning("[GameHUD] DayNightCycleController not found; timer display will not work.");
            }

            HookOutbreakSimulationEvents();

            // Initialize displays
            UpdateTimer();
            UpdateGlobalSummaries();
            
            Debug.Log($"[GameHUD] Modern bottom bar initialization complete.");
            
            // Ensure the HUD is visible on start
            Show();
        }

        private void UpdateTimer()
        {
            if (timerValue == null) return;

            if (hasTimeSnapshot)
            {
                var timeText = latestTimeSnapshot.DateTime.ToString("HH:mm", CultureInfo.InvariantCulture);
                timerValue.text = $"Day {latestTimeSnapshot.DayIndex}";

                if (timerSubValueLabel != null)
                {
                    timerSubValueLabel.text = $"{timeText} | {GetTimeScaleDisplay()}";
                }

                if (timerDetailLabel != null)
                {
                    timerDetailLabel.text = FormatDetailedDate(latestTimeSnapshot.DateTime);
                }
            }
            else
            {
                timerValue.text = "Day --";
                if (timerSubValueLabel != null) timerSubValueLabel.text = "--:-- | --";
                if (timerDetailLabel != null) timerDetailLabel.text = "Starting...";
            }
        }

        private void HandleTimeUpdated(InGameTimeSnapshot snapshot)
        {
            latestTimeSnapshot = snapshot;
            hasTimeSnapshot = true;
            UpdateTimer();
        }

        private string GetTimeScaleDisplay()
        {
            var scale = dayNightController != null ? dayNightController.TimeScale : TimeScaleDisplayBaseline;
            if (Mathf.Approximately(TimeScaleDisplayBaseline, 0f))
            {
                return string.Format(CultureInfo.InvariantCulture, "{0:0.#}x", scale);
            }

            var relative = scale / TimeScaleDisplayBaseline;
            return string.Format(CultureInfo.InvariantCulture, "{0:0.#}x speed", relative);
        }

        private static string FormatDetailedDate(System.DateTime dateTime)
        {
            var month = dateTime.ToString("MMM", CultureInfo.InvariantCulture);
            var day = dateTime.Day;
            var suffix = GetDaySuffix(day);
            return $"{month} {day}{suffix} {dateTime:yyyy}";
        }

        private static string GetDaySuffix(int day)
        {
            var rem100 = day % 100;
            if (rem100 >= 11 && rem100 <= 13)
            {
                return "th";
            }

            return (day % 10) switch
            {
                1 => "st",
                2 => "nd",
                3 => "rd",
                _ => "th"
            };
        }

        private void HookOutbreakSimulationEvents()
        {
            if (simulationEventsHooked)
            {
                return;
            }

            if (outbreakSimulation == null)
            {
                outbreakSimulation = FindFirstObjectByType<OutbreakSimulationController>();
            }

            if (outbreakSimulation == null)
            {
                Debug.LogWarning("[GameHUD] OutbreakSimulationController not found; cure and budget displays will not work.");
                return;
            }

            outbreakSimulation.GlobalStateChanged += HandleGlobalStateChanged;
            outbreakSimulation.DailyIncomeReceived += HandleDailyIncomeReceived;
            simulationEventsHooked = true;
            outbreakSimulation.ProvinceStateChanged += HandleProvinceStateChanged;

            if (outbreakSimulation.GlobalState != null)
            {
                HandleGlobalStateChanged(outbreakSimulation.GlobalState);
            }

            UpdateGlobalSummaries();
        }

        private void HandleGlobalStateChanged(GlobalCureState state)
        {
            latestGlobalState = state;
            UpdateGlobalSummaries();

            if (zarBalanceLabel != null)
            {
                var balance = state?.ZarBalance ?? 0;
                zarBalanceLabel.text = string.Format(CultureInfo.InvariantCulture, "R {0}", balance);
            }
        }

        private void HandleProvinceStateChanged(ProvinceInfectionState state)
        {
            UpdateGlobalSummaries();
        }

        private void UpdateGlobalSummaries()
        {
            var curePercent = latestGlobalState != null
                ? Mathf.Clamp01(latestGlobalState.CureProgress01) * 100f
                : 0f;

            if (cureSummaryLabel != null)
            {
                cureSummaryLabel.text = string.Format(CultureInfo.InvariantCulture, "Cure {0:0}%", curePercent);
            }

            var infection01 = outbreakSimulation != null ? outbreakSimulation.GetAverageInfection01() : 0f;
            var infectionPercent = Mathf.Clamp01(infection01) * 100f;

            if (infectionSummaryLabel != null)
            {
                infectionSummaryLabel.text = string.Format(CultureInfo.InvariantCulture, "Infected {0:0}%", infectionPercent);
            }
        }

        public void OpenUpgradePanel()
        {
            if (upgradePanelController != null)
            {
                upgradePanelController.Toggle();
                return;
            }

            if (modalHost == null || upgradePanelTemplate == null || outbreakSimulation == null)
            {
                Debug.LogWarning("[GameHUD] Cannot open upgrades panel: missing references.");
                return;
            }

            // Create upgrade panel from template
            modalHost.RemoveFromClassList("hidden");
            modalHost.Clear();

            var panelRoot = upgradePanelTemplate.CloneTree();
            modalHost.Add(panelRoot);

            upgradePanelController = new UpgradePanelController(panelRoot, outbreakSimulation);
            upgradePanelController.OnClosed += HandleUpgradePanelClosed;
            upgradePanelController.Show();
        }

        private void HandleUpgradePanelClosed()
        {
            if (modalHost != null)
            {
                modalHost.AddToClassList("hidden");
            }
        }

        private void HandleDailyIncomeReceived(int incomeAmount)
        {
            ShowIncomeToast(incomeAmount);
        }

        private void ShowIncomeToast(int amount)
        {
            if (incomeToast == null || incomeToastText == null)
            {
                return;
            }

            // Cancel any previously scheduled hide to prevent race conditions
            scheduledToastHide?.Pause();

            incomeToastText.text = string.Format(CultureInfo.InvariantCulture, "+R {0} Daily Income", amount);
            incomeToast.AddToClassList("income-toast--visible");

            // Schedule hide after delay
            scheduledToastHide = incomeToast.schedule.Execute(() =>
            {
                incomeToast.RemoveFromClassList("income-toast--visible");
            }).StartingIn(3000);
        }

        /// <summary>
        /// Resets the game timer.
        /// </summary>
        public void ResetTimer()
        {
            dayNightController?.RestartCycle();
        }

        /// <summary>
        /// Gets the current game time in seconds.
        /// </summary>
        public float GetGameTime()
        {
            if (!hasTimeSnapshot)
            {
                return 0f;
            }

            return latestTimeSnapshot.TimeOfDayMinutes * 60f;
        }

        private void OnDestroy()
        {
            // Unsubscribe from events
            if (dayNightController != null)
            {
                dayNightController.TimeUpdated -= HandleTimeUpdated;
            }

            if (simulationEventsHooked && outbreakSimulation != null)
            {
                outbreakSimulation.GlobalStateChanged -= HandleGlobalStateChanged;
                outbreakSimulation.ProvinceStateChanged -= HandleProvinceStateChanged;
                outbreakSimulation.DailyIncomeReceived -= HandleDailyIncomeReceived;
                simulationEventsHooked = false;
            }

            if (upgradePanelController != null)
            {
                upgradePanelController.OnClosed -= HandleUpgradePanelClosed;
                upgradePanelController.Dispose();
                upgradePanelController = null;
            }
        }
    }
}
