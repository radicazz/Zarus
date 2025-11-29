using System;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Zarus.Map;
using Zarus.Systems;

namespace Zarus.UI
{
    /// <summary>
    /// Controls the world-space province panel that appears above selected provinces.
    /// This creates a diegetic UI experience where province info floats in game world.
    /// </summary>
    public class ProvincePanelController : MonoBehaviour
    {
        [Header("References")]
        [SerializeField]
        private UIDocument uiDocument;

        [SerializeField]
        private RegionMapController mapController;

        [SerializeField]
        private OutbreakSimulationController outbreakSimulation;

        [SerializeField]
        private Camera mainCamera;

        [Header("Settings")]
        [SerializeField]
        private Vector3 worldOffset = new Vector3(0, 2f, 0);

        [SerializeField]
        private Vector2 screenOffset = new Vector2(0, -100f);

        // UI Elements
        private VisualElement root;
        private Label provinceNameLabel;
        private Label infectionValueLabel;
        private Label outpostStatusLabel;
        private Label populationLabel;
        private Label threatLevelLabel;
        private Button deployOutpostButton;
        private Label outpostCostLabel;

        // State
        private RegionEntry currentProvince;
        private bool isVisible;
        private bool deploymentInProgress; // Flag to prevent race conditions during deployment

        private void Awake()
        {
            if (mainCamera == null)
            {
                mainCamera = Camera.main;
            }

            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }

            if (outbreakSimulation == null)
            {
                outbreakSimulation = FindFirstObjectByType<OutbreakSimulationController>();
            }
        }

        private void Start()
        {
            InitializeUI();
            SubscribeToEvents();
            Hide();
        }

        private void OnDestroy()
        {
            UnsubscribeFromEvents();

            if (deployOutpostButton != null)
            {
                deployOutpostButton.clicked -= OnDeployClicked;
            }
        }

        private void LateUpdate()
        {
            if (isVisible && currentProvince != null)
            {
                UpdatePanelPosition();
            }
        }

        private void InitializeUI()
        {
            Debug.Log("[ProvincePanelController] InitializeUI called");
            
            if (uiDocument == null || uiDocument.rootVisualElement == null)
            {
                Debug.LogError("[ProvincePanelController] UIDocument or root element is null!");
                return;
            }

            root = uiDocument.rootVisualElement.Q<VisualElement>("ProvincePanelRoot");
            if (root == null)
            {
                Debug.LogError("[ProvincePanelController] ProvincePanelRoot not found in UXML!");
                return;
            }
            
            provinceNameLabel = root?.Q<Label>("ProvinceNameLabel");
            infectionValueLabel = root?.Q<Label>("InfectionValueLabel");
            outpostStatusLabel = root?.Q<Label>("OutpostStatusLabel");
            populationLabel = root?.Q<Label>("PopulationLabel");
            threatLevelLabel = root?.Q<Label>("ThreatLevelLabel");
            deployOutpostButton = root?.Q<Button>("DeployOutpostButton");
            outpostCostLabel = root?.Q<Label>("OutpostCostLabel");

            Debug.LogFormat("[ProvincePanelController] UI elements found: Button={0}, Root={1}", 
                deployOutpostButton != null, root != null);

            if (deployOutpostButton != null)
            {
                deployOutpostButton.clicked += OnDeployClicked;
                Debug.Log("[ProvincePanelController] Deploy button click event registered");
            }
            else
            {
                Debug.LogError("[ProvincePanelController] DeployOutpostButton not found!");
            }
        }

        private void SubscribeToEvents()
        {
            if (mapController != null)
            {
                mapController.OnRegionSelected.AddListener(OnProvinceSelected);
            }

            if (outbreakSimulation != null)
            {
                outbreakSimulation.ProvinceStateChanged += OnProvinceStateChanged;
                outbreakSimulation.GlobalStateChanged += OnGlobalStateChanged;
            }
        }

        private void UnsubscribeFromEvents()
        {
            if (mapController != null)
            {
                mapController.OnRegionSelected.RemoveListener(OnProvinceSelected);
            }

            if (outbreakSimulation != null)
            {
                outbreakSimulation.ProvinceStateChanged -= OnProvinceStateChanged;
                outbreakSimulation.GlobalStateChanged -= OnGlobalStateChanged;
            }
        }

private void OnProvinceSelected(RegionEntry province)
        {
            Debug.LogFormat("[ProvincePanelController] Province selected: {0}", 
                province?.RegionId ?? "NULL");
            
            if (province == null)
            {
                // Only hide if not in the middle of a deployment
                if (!deploymentInProgress)
                {
                    Hide();
                }
                return;
            }

            currentProvince = province;
            Show();
            RefreshProvinceData();
            Debug.LogFormat("[ProvincePanelController] Province panel shown for {0}", province.RegionId);
        }

        private void OnProvinceStateChanged(ProvinceInfectionState state)
        {
            if (currentProvince == null || state == null)
            {
                return;
            }

            if (string.Equals(state.RegionId, currentProvince.RegionId, StringComparison.OrdinalIgnoreCase))
            {
                RefreshProvinceData();
            }
        }

        private void OnGlobalStateChanged(GlobalCureState state)
        {
            if (isVisible)
            {
                UpdateDeployButton();
            }
        }

        private void RefreshProvinceData()
        {
            if (currentProvince == null)
            {
                return;
            }

            // Update province name
            if (provinceNameLabel != null)
            {
                provinceNameLabel.text = $"★ {currentProvince.DisplayName.ToUpper()} ★";
            }

            // Update additional province information
            UpdateProvinceInfo(currentProvince);

            // Get province state from simulation
            if (outbreakSimulation != null && outbreakSimulation.TryGetProvinceState(currentProvince.RegionId, out var state))
            {
                UpdateInfectionDisplay(state);
                UpdateOutpostDisplay(state);
                UpdateThreatLevel(state);
                UpdateDeployButton();
            }
        }

        private void UpdateInfectionDisplay(ProvinceInfectionState state)
        {
            if (infectionValueLabel == null)
            {
                return;
            }

            var percent = Mathf.RoundToInt(Mathf.Clamp01(state.Infection01) * 100f);
            infectionValueLabel.text = string.Format(CultureInfo.InvariantCulture, "{0}%", percent);

            // Update color based on severity
            infectionValueLabel.RemoveFromClassList("province-stat__value--muted");
            infectionValueLabel.RemoveFromClassList("province-stat__value--warning");
            infectionValueLabel.RemoveFromClassList("province-stat__value--danger");
            infectionValueLabel.RemoveFromClassList("province-stat__value--success");

            if (state.IsFullyInfected)
            {
                infectionValueLabel.AddToClassList("province-stat__value--danger");
            }
            else if (state.Infection01 >= 0.5f)
            {
                infectionValueLabel.AddToClassList("province-stat__value--warning");
            }
            else
            {
                infectionValueLabel.AddToClassList("province-stat__value--success");
            }
        }

        private void UpdateOutpostDisplay(ProvinceInfectionState state)
        {
            if (outpostStatusLabel == null)
            {
                return;
            }

            outpostStatusLabel.RemoveFromClassList("province-stat__value--muted");
            outpostStatusLabel.RemoveFromClassList("province-stat__value--warning");
            outpostStatusLabel.RemoveFromClassList("province-stat__value--danger");
            outpostStatusLabel.RemoveFromClassList("province-stat__value--success");

            if (!state.HasOutpost)
            {
                outpostStatusLabel.text = "None";
                outpostStatusLabel.AddToClassList("province-stat__value--muted");
            }
            else if (state.OutpostDisabled)
            {
                outpostStatusLabel.text = string.Format(CultureInfo.InvariantCulture, "{0} Disabled", state.OutpostCount);
                outpostStatusLabel.AddToClassList("province-stat__value--warning");
            }
            else
            {
                outpostStatusLabel.text = string.Format(CultureInfo.InvariantCulture, "{0} Active", state.OutpostCount);
                outpostStatusLabel.AddToClassList("province-stat__value--success");
            }
        }

        private void UpdateDeployButton()
        {
            if (deployOutpostButton == null || outpostCostLabel == null || currentProvince == null)
            {
                return;
            }

            var canBuild = outbreakSimulation.CanBuildOutpost(currentProvince.RegionId, out var costR, out var error);
            deployOutpostButton.SetEnabled(canBuild);

            outpostCostLabel.text = string.Format(CultureInfo.InvariantCulture, "Cost: R {0}", costR);

            // Update button text based on error
            if (!canBuild)
            {
                switch (error)
                {
                    case OutbreakSimulationController.OutpostBuildError.ProvinceFullyInfected:
                        deployOutpostButton.text = "Province Lost";
                        break;
                    case OutbreakSimulationController.OutpostBuildError.NotEnoughZar:
                        deployOutpostButton.text = "Insufficient Funds";
                        break;
                    default:
                        deployOutpostButton.text = "Cannot Deploy";
                        break;
                }
            }
            else
            {
                deployOutpostButton.text = "Deploy Cure Outpost";
            }
        }

        private void UpdateProvinceInfo(RegionEntry province)
        {
            if (populationLabel != null)
            {
                // Generate realistic population based on province bounds area
                var area = province.Bounds.size.x * province.Bounds.size.y;
                var population = Mathf.RoundToInt(area * 50000f); // Rough population density
                
                if (population >= 1000000)
                {
                    populationLabel.text = string.Format(CultureInfo.InvariantCulture, "{0:F1}M", population / 1000000f);
                }
                else if (population >= 1000)
                {
                    populationLabel.text = string.Format(CultureInfo.InvariantCulture, "{0:F0}K", population / 1000f);
                }
                else
                {
                    populationLabel.text = population.ToString(CultureInfo.InvariantCulture);
                }
            }
        }

        private void UpdateThreatLevel(ProvinceInfectionState state)
        {
            if (threatLevelLabel == null)
            {
                return;
            }

            // Clear existing classes
            threatLevelLabel.RemoveFromClassList("province-stat__value--muted");
            threatLevelLabel.RemoveFromClassList("province-stat__value--warning");
            threatLevelLabel.RemoveFromClassList("province-stat__value--danger");
            threatLevelLabel.RemoveFromClassList("province-stat__value--success");

            // Determine threat level based on infection rate and outpost status
            if (state.IsFullyInfected)
            {
                threatLevelLabel.text = "CRITICAL";
                threatLevelLabel.AddToClassList("province-stat__value--danger");
            }
            else if (state.Infection01 >= 0.7f)
            {
                threatLevelLabel.text = "SEVERE";
                threatLevelLabel.AddToClassList("province-stat__value--danger");
            }
            else if (state.Infection01 >= 0.4f)
            {
                threatLevelLabel.text = "MODERATE";
                threatLevelLabel.AddToClassList("province-stat__value--warning");
            }
            else if (state.Infection01 >= 0.1f)
            {
                threatLevelLabel.text = "LOW";
                threatLevelLabel.AddToClassList("province-stat__value--warning");
            }
            else
            {
                threatLevelLabel.text = "MINIMAL";
                threatLevelLabel.AddToClassList("province-stat__value--success");
            }
        }

private void OnDeployClicked()
        {
            // Capture province at click time to avoid race conditions
            var targetProvince = currentProvince;
            
            Debug.LogFormat("[ProvincePanelController] Deploy button clicked for province: {0}", 
                targetProvince?.RegionId ?? "NULL");
            
            if (targetProvince == null)
            {
                Debug.LogError("[ProvincePanelController] currentProvince is null! This shouldn't happen if the panel is visible.");
                return;
            }
            
            if (outbreakSimulation == null)
            {
                Debug.LogError("[ProvincePanelController] outbreakSimulation is null!");
                return;
            }

            // Set deployment flag to prevent interference
            deploymentInProgress = true;
            
            Debug.LogFormat("[ProvincePanelController] Calling TryBuildOutpost for {0}...", targetProvince.RegionId);
            if (outbreakSimulation.TryBuildOutpost(targetProvince.RegionId, out _, out _))
            {
                Debug.LogFormat("[ProvincePanelController] Successfully deployed outpost in {0}!", targetProvince.RegionId);
                
                // Keep the panel open and province selected after successful deployment
                RefreshProvinceData();
                
                // Keep the province selected by ensuring we don't clear the selection
                // The panel should remain visible and the camera should not zoom out
            }
            else
            {
                Debug.LogFormat("[ProvincePanelController] Failed to deploy outpost in {0}", targetProvince.RegionId);
            }
            
            // Clear deployment flag but maintain selection
            deploymentInProgress = false;
        }

        private void UpdatePanelPosition()
        {
            if (root == null || currentProvince == null || mainCamera == null)
            {
                return;
            }

            // Get province center position in world space
            var provinceBounds = currentProvince.Bounds;
            var provinceCenter = provinceBounds.center + worldOffset;

            // Convert to screen position
            var screenPos = mainCamera.WorldToScreenPoint(provinceCenter);

            // Check if province is behind camera
            if (screenPos.z < 0)
            {
                Hide();
                return;
            }

            // Ensure panel is visible
            if (!isVisible)
            {
                Show();
            }

            // Apply screen offset and set position
            screenPos.x += screenOffset.x;
            screenPos.y = Screen.height - screenPos.y + screenOffset.y; // Flip Y for UI coordinates

            // Get panel dimensions for clamping
            var panelWidth = root.resolvedStyle.width;
            var panelHeight = root.resolvedStyle.height;
            
            // If dimensions aren't resolved yet, use default values
            if (panelWidth <= 0) panelWidth = 320f; // max-width from CSS
            if (panelHeight <= 0) panelHeight = 200f; // estimated height
            
            // Define HUD safe zones (avoid overlapping with top and bottom bars)
            const float topHudHeight = 48f;  // Top bar height
            const float bottomHudHeight = 56f; // Bottom bar height
            const float padding = 16f; // Additional padding from screen edges
            
            // Clamp to screen bounds with HUD avoidance
            var clampedX = Mathf.Clamp(screenPos.x - (panelWidth / 2f), 
                                      padding, 
                                      Screen.width - panelWidth - padding);
            
            var clampedY = Mathf.Clamp(screenPos.y, 
                                      topHudHeight + padding, 
                                      Screen.height - bottomHudHeight - panelHeight - padding);

            root.style.left = clampedX;
            root.style.top = clampedY;
            root.style.position = Position.Absolute;
        }

        public void Show()
        {
            if (root == null)
            {
                return;
            }

            isVisible = true;
            root.RemoveFromClassList("hidden");
            root.AddToClassList("province-panel--visible");
        }

        public void Hide()
        {
            if (root == null)
            {
                return;
            }

            isVisible = false;
            
            // Don't clear currentProvince immediately to avoid race conditions with button clicks
            // Only clear it when we're sure no deployment is in progress
            if (!deploymentInProgress)
            {
                currentProvince = null;
            }
            
            root.RemoveFromClassList("province-panel--visible");
            
            // Use schedule to avoid immediate removal during animation
            root.schedule.Execute(() =>
            {
                if (!isVisible)
                {
                    root.AddToClassList("hidden");
                    // Clear province reference after animation if not in deployment
                    if (!deploymentInProgress)
                    {
                        currentProvince = null;
                    }
                }
            }).StartingIn(200); // Wait for transition
        }
    

/// <summary>
        /// Returns true if a deployment is currently in progress
        /// </summary>
        public bool IsDeploymentInProgress => deploymentInProgress;
}
}
