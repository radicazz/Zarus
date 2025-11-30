using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zarus.Systems
{
    /// <summary>
    /// Identifies all available upgrade types in the game.
    /// </summary>
    public enum UpgradeType
    {
        // Infrastructure upgrades (income-related)
        TaxEfficiency,
        EconomicRecovery,
        EmergencyFunds,

        // Cure upgrades (research/effectiveness)
        ResearchEfficiency,
        OutpostCapacity,
        RapidDeployment,
        VaccineBreakthrough,

        // Virus containment upgrades (spread reduction)
        ContainmentProtocols,
        BorderSecurity,
        QuarantineMeasures,
        MedicalStockpiles
    }

    /// <summary>
    /// Defines the properties of an upgrade including costs and effects.
    /// </summary>
    [Serializable]
    public class UpgradeDefinition
    {
        public UpgradeType Type;
        public string DisplayName;
        public string Description;
        public int BaseCostR;
        public int CostPerLevelR;
        public int MaxLevel;
        public bool IsOneTimeBonus;
        public int[] OneTimeBonusAmounts; // For EmergencyFunds: bonus at each level

        /// <summary>
        /// Calculate the cost to purchase the next level of this upgrade.
        /// </summary>
        public int GetCostForLevel(int currentLevel)
        {
            if (currentLevel >= MaxLevel)
            {
                return int.MaxValue;
            }

            return BaseCostR + CostPerLevelR * currentLevel;
        }
    }

    /// <summary>
    /// Tracks the current level of a specific upgrade.
    /// </summary>
    [Serializable]
    public class UpgradeState
    {
        public UpgradeType Type;
        public int CurrentLevel;

        public UpgradeState(UpgradeType type)
        {
            Type = type;
            CurrentLevel = 0;
        }
    }

    /// <summary>
    /// Container for all player upgrades with helper methods for querying and purchasing.
    /// </summary>
    [Serializable]
    public class PlayerUpgrades
    {
        // Dictionary for O(1) lookup by upgrade type
        private readonly Dictionary<UpgradeType, UpgradeState> upgradeMap = 
            new Dictionary<UpgradeType, UpgradeState>();

        public IEnumerable<UpgradeState> Upgrades => upgradeMap.Values;

        public event Action<UpgradeType, int> OnUpgradePurchased;

        public PlayerUpgrades()
        {
            InitializeAllUpgrades();
        }

        private void InitializeAllUpgrades()
        {
            upgradeMap.Clear();
            foreach (UpgradeType type in Enum.GetValues(typeof(UpgradeType)))
            {
                upgradeMap[type] = new UpgradeState(type);
            }
        }

        /// <summary>
        /// Get the current level of a specific upgrade. O(1) lookup.
        /// </summary>
        public int GetLevel(UpgradeType type)
        {
            return upgradeMap.TryGetValue(type, out var upgrade) ? upgrade.CurrentLevel : 0;
        }

        /// <summary>
        /// Get the cost to purchase the next level of an upgrade.
        /// </summary>
        public int GetCost(UpgradeType type)
        {
            var definition = UpgradeRegistry.GetDefinition(type);
            if (definition == null)
            {
                return int.MaxValue;
            }

            var currentLevel = GetLevel(type);
            return definition.GetCostForLevel(currentLevel);
        }

        /// <summary>
        /// Check if an upgrade is at max level.
        /// </summary>
        public bool IsMaxLevel(UpgradeType type)
        {
            var definition = UpgradeRegistry.GetDefinition(type);
            if (definition == null)
            {
                return true;
            }

            return GetLevel(type) >= definition.MaxLevel;
        }

        /// <summary>
        /// Check if the player can afford to purchase an upgrade.
        /// </summary>
        public bool CanAfford(UpgradeType type, int zarBalance)
        {
            if (IsMaxLevel(type))
            {
                return false;
            }

            var cost = GetCost(type);
            return zarBalance >= cost;
        }

        /// <summary>
        /// Attempt to purchase an upgrade. Returns the cost if successful, 0 if failed. O(1) lookup.
        /// </summary>
        public bool TryPurchase(UpgradeType type, ref int zarBalance, out int cost, out int bonusAmount)
        {
            cost = 0;
            bonusAmount = 0;

            if (!CanAfford(type, zarBalance))
            {
                return false;
            }

            cost = GetCost(type);
            zarBalance -= cost;

            if (upgradeMap.TryGetValue(type, out var upgrade))
            {
                upgrade.CurrentLevel++;

                // Handle one-time bonus (EmergencyFunds)
                var definition = UpgradeRegistry.GetDefinition(type);
                if (definition != null && definition.IsOneTimeBonus && definition.OneTimeBonusAmounts != null)
                {
                    var bonusIndex = upgrade.CurrentLevel - 1;
                    if (bonusIndex >= 0 && bonusIndex < definition.OneTimeBonusAmounts.Length)
                    {
                        bonusAmount = definition.OneTimeBonusAmounts[bonusIndex];
                        zarBalance += bonusAmount;
                    }
                }

                OnUpgradePurchased?.Invoke(type, upgrade.CurrentLevel);
                return true;
            }

            return false;
        }

        /// <summary>
        /// Reset all upgrades to level 0.
        /// </summary>
        public void Reset()
        {
            foreach (var upgrade in upgradeMap.Values)
            {
                upgrade.CurrentLevel = 0;
            }
        }
    }

    /// <summary>
    /// Static registry containing all upgrade definitions.
    /// </summary>
    public static class UpgradeRegistry
    {
        private static readonly Dictionary<UpgradeType, UpgradeDefinition> Definitions;

        static UpgradeRegistry()
        {
            Definitions = new Dictionary<UpgradeType, UpgradeDefinition>
            {
                // Infrastructure Upgrades
                {
                    UpgradeType.TaxEfficiency,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.TaxEfficiency,
                        DisplayName = "Tax Efficiency",
                        Description = "+15% base income per level",
                        BaseCostR = 100,
                        CostPerLevelR = 50,
                        MaxLevel = 5,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.EconomicRecovery,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.EconomicRecovery,
                        DisplayName = "Economic Recovery",
                        Description = "+R10 per healthy province per level",
                        BaseCostR = 150,
                        CostPerLevelR = 75,
                        MaxLevel = 3,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.EmergencyFunds,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.EmergencyFunds,
                        DisplayName = "Emergency Funds",
                        Description = "One-time budget injection",
                        BaseCostR = 80,
                        CostPerLevelR = 80,
                        MaxLevel = 3,
                        IsOneTimeBonus = true,
                        OneTimeBonusAmounts = new[] { 200, 300, 500 }
                    }
                },

                // Cure Upgrades
                {
                    UpgradeType.ResearchEfficiency,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.ResearchEfficiency,
                        DisplayName = "Research Efficiency",
                        Description = "+20% global cure speed per level",
                        BaseCostR = 120,
                        CostPerLevelR = 60,
                        MaxLevel = 5,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.OutpostCapacity,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.OutpostCapacity,
                        DisplayName = "Outpost Capacity",
                        Description = "+50% local cure rate per level",
                        BaseCostR = 200,
                        CostPerLevelR = 100,
                        MaxLevel = 3,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.RapidDeployment,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.RapidDeployment,
                        DisplayName = "Rapid Deployment",
                        Description = "-25% outpost cost per level",
                        BaseCostR = 150,
                        CostPerLevelR = 75,
                        MaxLevel = 3,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.VaccineBreakthrough,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.VaccineBreakthrough,
                        DisplayName = "Vaccine Breakthrough",
                        Description = "-5% cure threshold per level (easier wins)",
                        BaseCostR = 300,
                        CostPerLevelR = 150,
                        MaxLevel = 2,
                        IsOneTimeBonus = false
                    }
                },

                // Virus Containment Upgrades
                {
                    UpgradeType.ContainmentProtocols,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.ContainmentProtocols,
                        DisplayName = "Containment Protocols",
                        Description = "-15% virus spread rate per level",
                        BaseCostR = 180,
                        CostPerLevelR = 90,
                        MaxLevel = 3,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.BorderSecurity,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.BorderSecurity,
                        DisplayName = "Border Security",
                        Description = "-10% base infection rate per level",
                        BaseCostR = 140,
                        CostPerLevelR = 70,
                        MaxLevel = 4,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.QuarantineMeasures,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.QuarantineMeasures,
                        DisplayName = "Quarantine Measures",
                        Description = "+10% spread threshold per level",
                        BaseCostR = 220,
                        CostPerLevelR = 110,
                        MaxLevel = 2,
                        IsOneTimeBonus = false
                    }
                },
                {
                    UpgradeType.MedicalStockpiles,
                    new UpgradeDefinition
                    {
                        Type = UpgradeType.MedicalStockpiles,
                        DisplayName = "Medical Stockpiles",
                        Description = "-5% outpost disable threshold per level",
                        BaseCostR = 160,
                        CostPerLevelR = 80,
                        MaxLevel = 3,
                        IsOneTimeBonus = false
                    }
                }
            };
        }

        /// <summary>
        /// Get the definition for a specific upgrade type.
        /// </summary>
        public static UpgradeDefinition GetDefinition(UpgradeType type)
        {
            return Definitions.TryGetValue(type, out var def) ? def : null;
        }

        /// <summary>
        /// Get all upgrade definitions.
        /// </summary>
        public static IEnumerable<UpgradeDefinition> GetAllDefinitions()
        {
            return Definitions.Values;
        }

        /// <summary>
        /// Get all infrastructure upgrade types.
        /// </summary>
        public static IEnumerable<UpgradeType> GetInfrastructureUpgrades()
        {
            yield return UpgradeType.TaxEfficiency;
            yield return UpgradeType.EconomicRecovery;
            yield return UpgradeType.EmergencyFunds;
        }

        /// <summary>
        /// Get all cure upgrade types.
        /// </summary>
        public static IEnumerable<UpgradeType> GetCureUpgrades()
        {
            yield return UpgradeType.ResearchEfficiency;
            yield return UpgradeType.OutpostCapacity;
            yield return UpgradeType.RapidDeployment;
            yield return UpgradeType.VaccineBreakthrough;
        }

        /// <summary>
        /// Get all virus containment upgrade types.
        /// </summary>
        public static IEnumerable<UpgradeType> GetContainmentUpgrades()
        {
            yield return UpgradeType.ContainmentProtocols;
            yield return UpgradeType.BorderSecurity;
            yield return UpgradeType.QuarantineMeasures;
            yield return UpgradeType.MedicalStockpiles;
        }
    }
}
