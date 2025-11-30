using System;
using System.Collections.Generic;
using System.Globalization;
using UnityEngine;
using UnityEngine.UIElements;
using Zarus.Systems;

namespace Zarus.UI
{
    /// <summary>
    /// Controls the upgrade panel UI, handling upgrade purchases and display.
    /// </summary>
    public class UpgradePanelController
    {
        private readonly VisualElement root;
        private readonly OutbreakSimulationController simulation;

        private Label budgetValueLabel;
        private Button closeButton;

        private readonly Dictionary<UpgradeType, UpgradeCardElements> upgradeCards =
            new Dictionary<UpgradeType, UpgradeCardElements>();

        private readonly Dictionary<UpgradeType, Action> upgradeButtonCallbacks =
            new Dictionary<UpgradeType, Action>();

        public event Action OnClosed;
        public event Action<UpgradeType, int> OnUpgradePurchased;

        public bool IsVisible => root != null && !root.ClassListContains("hidden");

        private class UpgradeCardElements
        {
            public VisualElement Card;
            public Label NameLabel;
            public Label LevelLabel;
            public Label DescriptionLabel;
            public Label CostLabel;
            public Button UpgradeButton;
        }

        public UpgradePanelController(VisualElement rootElement, OutbreakSimulationController simulationController)
        {
            root = rootElement;
            simulation = simulationController;

            Initialize();
        }

        private void Initialize()
        {
            if (root == null)
            {
                Debug.LogWarning("[UpgradePanelController] Root element is null.");
                return;
            }

            // Query main elements
            budgetValueLabel = root.Q<Label>("UpgradeBudgetValue");
            closeButton = root.Q<Button>("CloseUpgradePanelButton");

            if (closeButton != null)
            {
                closeButton.clicked += HandleCloseClicked;
            }

            // Initialize upgrade cards
            InitializeUpgradeCard(UpgradeType.TaxEfficiency, "TaxEfficiency");
            InitializeUpgradeCard(UpgradeType.EconomicRecovery, "EconomicRecovery");
            InitializeUpgradeCard(UpgradeType.EmergencyFunds, "EmergencyFunds");
            InitializeUpgradeCard(UpgradeType.ResearchEfficiency, "ResearchEfficiency");
            InitializeUpgradeCard(UpgradeType.OutpostCapacity, "OutpostCapacity");
            InitializeUpgradeCard(UpgradeType.RapidDeployment, "RapidDeployment");
            InitializeUpgradeCard(UpgradeType.VaccineBreakthrough, "VaccineBreakthrough");
            InitializeUpgradeCard(UpgradeType.ContainmentProtocols, "ContainmentProtocols");
            InitializeUpgradeCard(UpgradeType.BorderSecurity, "BorderSecurity");
            InitializeUpgradeCard(UpgradeType.QuarantineMeasures, "QuarantineMeasures");
            InitializeUpgradeCard(UpgradeType.MedicalStockpiles, "MedicalStockpiles");

            // Subscribe to simulation events
            if (simulation != null)
            {
                simulation.GlobalStateChanged += HandleGlobalStateChanged;
            }

            RefreshAllUpgrades();
        }

        private void InitializeUpgradeCard(UpgradeType type, string baseName)
        {
            var card = root.Q<VisualElement>($"{baseName}Card");
            if (card == null)
            {
                return;
            }

            var elements = new UpgradeCardElements
            {
                Card = card,
                NameLabel = root.Q<Label>($"{baseName}Name"),
                LevelLabel = root.Q<Label>($"{baseName}Level"),
                DescriptionLabel = root.Q<Label>($"{baseName}Desc"),
                CostLabel = root.Q<Label>($"{baseName}Cost"),
                UpgradeButton = root.Q<Button>($"{baseName}Button")
            };

            if (elements.UpgradeButton != null)
            {
                Action callback = () => HandleUpgradeClicked(type);
                upgradeButtonCallbacks[type] = callback;
                elements.UpgradeButton.clicked += callback;
            }

            upgradeCards[type] = elements;
        }

        private void HandleCloseClicked()
        {
            Hide();
            OnClosed?.Invoke();
        }

        private void HandleUpgradeClicked(UpgradeType type)
        {
            if (simulation == null)
            {
                return;
            }

            if (simulation.TryPurchaseUpgrade(type, out var cost, out var bonusAmount))
            {
                RefreshUpgradeCard(type);
                UpdateBudgetDisplay();
                OnUpgradePurchased?.Invoke(type, simulation.Upgrades?.GetLevel(type) ?? 0);

                // Play sound feedback
                // AudioManager.Instance?.PlaySound("upgrade_purchased");

                if (bonusAmount > 0)
                {
                    Debug.Log($"[UpgradePanelController] Emergency Funds bonus: +R {bonusAmount}");
                }
            }
            else
            {
                // Play error sound
                // AudioManager.Instance?.PlaySound("upgrade_failed");
            }
        }

        private void HandleGlobalStateChanged(GlobalCureState state)
        {
            if (!IsVisible)
            {
                return;
            }

            UpdateBudgetDisplay();
            RefreshAllUpgradeButtons();
        }

        public void Show()
        {
            if (root == null)
            {
                return;
            }

            root.RemoveFromClassList("hidden");
            RefreshAllUpgrades();
        }

        public void Hide()
        {
            if (root == null)
            {
                return;
            }

            root.AddToClassList("hidden");
        }

        public void Toggle()
        {
            if (IsVisible)
            {
                Hide();
                OnClosed?.Invoke();
            }
            else
            {
                Show();
            }
        }

        public void RefreshAllUpgrades()
        {
            UpdateBudgetDisplay();

            foreach (var type in upgradeCards.Keys)
            {
                RefreshUpgradeCard(type);
            }
        }

        private void UpdateBudgetDisplay()
        {
            if (budgetValueLabel == null || simulation?.GlobalState == null)
            {
                return;
            }

            var balance = simulation.GlobalState.ZarBalance;
            budgetValueLabel.text = string.Format(CultureInfo.InvariantCulture, "R {0}", balance);
        }

        private void RefreshUpgradeCard(UpgradeType type)
        {
            if (!upgradeCards.TryGetValue(type, out var elements))
            {
                return;
            }

            var definition = UpgradeRegistry.GetDefinition(type);
            if (definition == null)
            {
                return;
            }

            var currentLevel = simulation?.Upgrades?.GetLevel(type) ?? 0;
            var maxLevel = definition.MaxLevel;
            var isMaxed = currentLevel >= maxLevel;

            // Update level display
            if (elements.LevelLabel != null)
            {
                elements.LevelLabel.text = string.Format(CultureInfo.InvariantCulture, "{0} / {1}", currentLevel, maxLevel);
            }

            // Update cost display
            if (elements.CostLabel != null)
            {
                if (isMaxed)
                {
                    elements.CostLabel.text = "MAXED";
                }
                else
                {
                    var cost = simulation?.Upgrades?.GetCost(type) ?? definition.GetCostForLevel(currentLevel);
                    elements.CostLabel.text = string.Format(CultureInfo.InvariantCulture, "Cost: R {0}", cost);
                }
            }

            // Update button state
            if (elements.UpgradeButton != null)
            {
                if (isMaxed)
                {
                    elements.UpgradeButton.SetEnabled(false);
                    elements.UpgradeButton.text = "Maxed";
                }
                else
                {
                    var canAfford = simulation?.Upgrades?.CanAfford(type, simulation.GlobalState?.ZarBalance ?? 0) ?? false;
                    elements.UpgradeButton.SetEnabled(canAfford);
                    elements.UpgradeButton.text = definition.IsOneTimeBonus ? "Unlock" : "Upgrade";
                }
            }

            // Update card style based on max status
            if (elements.Card != null)
            {
                if (isMaxed)
                {
                    elements.Card.AddToClassList("upgrade-card--maxed");
                }
                else
                {
                    elements.Card.RemoveFromClassList("upgrade-card--maxed");
                }
            }
        }

        private void RefreshAllUpgradeButtons()
        {
            foreach (var type in upgradeCards.Keys)
            {
                RefreshUpgradeButtonState(type);
            }
        }

        private void RefreshUpgradeButtonState(UpgradeType type)
        {
            if (!upgradeCards.TryGetValue(type, out var elements) || elements.UpgradeButton == null)
            {
                return;
            }

            var definition = UpgradeRegistry.GetDefinition(type);
            if (definition == null)
            {
                return;
            }

            var currentLevel = simulation?.Upgrades?.GetLevel(type) ?? 0;
            var isMaxed = currentLevel >= definition.MaxLevel;

            if (isMaxed)
            {
                elements.UpgradeButton.SetEnabled(false);
            }
            else
            {
                var canAfford = simulation?.Upgrades?.CanAfford(type, simulation.GlobalState?.ZarBalance ?? 0) ?? false;
                elements.UpgradeButton.SetEnabled(canAfford);
            }
        }

        public void Dispose()
        {
            if (closeButton != null)
            {
                closeButton.clicked -= HandleCloseClicked;
            }

            // Properly unregister stored callbacks to prevent memory leaks
            foreach (var kvp in upgradeButtonCallbacks)
            {
                if (upgradeCards.TryGetValue(kvp.Key, out var elements) && elements?.UpgradeButton != null)
                {
                    elements.UpgradeButton.clicked -= kvp.Value;
                }
            }

            if (simulation != null)
            {
                simulation.GlobalStateChanged -= HandleGlobalStateChanged;
            }

            upgradeButtonCallbacks.Clear();
            upgradeCards.Clear();
        }
    }
}
