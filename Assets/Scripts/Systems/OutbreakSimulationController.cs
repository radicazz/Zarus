using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using Zarus.Map;

namespace Zarus.Systems
{
    /// <summary>
    /// Drives province infection, outpost curing, and the global cure meter.
    /// </summary>
    [DisallowMultipleComponent]
    public class OutbreakSimulationController : MonoBehaviour
    {
        private const float MinutesPerDay = 1440f;

        public enum OutpostBuildError
        {
            None,
            InvalidRegion,
            ProvinceFullyInfected,
            NotEnoughZar
        }

        [Header("References")]
        [SerializeField]
        private RegionMapController mapController;

        [SerializeField]
        private DayNightCycleController dayNightController;

        [Header("Rates & Economy")]
        [SerializeField]
        private OutpostRateConfig outpostRates = new OutpostRateConfig
        {
            LocalCurePerHour = 0.025f,
            GlobalCurePerHourPerOutpost = 0.008f,
            DiminishingReturnFactor = 0.9f,
            TargetWinDayMin = 10f,
            TargetWinDayMax = 15f
        };

        [SerializeField]
        private VirusRateConfig virusRates = new VirusRateConfig
        {
            BaseInfectionPerHour = 0.011f,
            DailyVirusGrowth = 0.05f,
            OutpostDisableThreshold01 = 0.75f,
            FullyInfectedThreshold01 = 0.99f
        };

        [SerializeField]
        private OutpostCostConfig costConfig = new OutpostCostConfig
        {
            BaseCostR = 25,
            CostPerExistingOutpostR = 10
        };

        [Header("Special Provinces")]
        [SerializeField]
        private string[] urbanHubRegionIds = { "ZAGP", "ZAWC", "ZAKZN" };

        [SerializeField, Min(1f)]
        [Tooltip("Bonus multiplier applied to global research from urban hub outposts.")]
        private float urbanHubBonusMultiplier = 1.25f;

        [Header("Income Settings")]
        [SerializeField]
        private IncomeConfig incomeConfig = IncomeConfig.Default;

        [Header("Startup Settings")]
        [SerializeField]
        [Tooltip("Randomized infection percentage seeded per province (0-1 range).")]
        private Vector2 initialInfectionRange = new Vector2(0.05f, 0.2f);

        [SerializeField, Min(0)]
        [Tooltip("Starting national ZAR budget for deploying outposts.")]
        private int startingZarBalance = 150;

        
        [Header("Viral Spread Configuration")]
        [SerializeField]
        [Tooltip("Infection threshold (0-1) at which a province can spread to neighbors.")]
        private float spreadThreshold = 0.45f;

        [SerializeField]
        [Tooltip("Base spread rate per hour to neighboring provinces.")]
        private float baseSpreadRate = 0.015f;

        [SerializeField]
        [Tooltip("Multiplier for spread aggressiveness (1.0 = medium, 0.5 = slow, 2.0 = fast).")]
        private float spreadAggressivenessMultiplier = 1.0f;

        [SerializeField]
        [Tooltip("Province adjacency data for neighbor-based spreading.")]
        private ProvinceAdjacencyData adjacencyData;

        [Header("Outbreak Hotspots")]
[Header("Outbreak Hotspots")]
        [SerializeField]
        [Tooltip("Number of provinces that start with higher infection.")]
        private int hotspotCount = 2;

        [SerializeField]
        [Tooltip("Infection range for hotspot provinces.")]
        private Vector2 hotspotInfectionRange = new Vector2(0.30f, 0.45f);

        [Header("Diagnostics")]
        [SerializeField]
        [Tooltip("When enabled, prints a short daily summary to the console for tuning.")]
        private bool logSummaryToConsole;

        [Header("Events")]
        [SerializeField]
        private ProvinceStateEvent onProvinceStateChanged = new ProvinceStateEvent();

        [SerializeField]
        private GlobalStateEvent onGlobalStateChanged = new GlobalStateEvent();

        [SerializeField]
        private UnityEvent onAllProvincesFullyInfected = new UnityEvent();

        [SerializeField]
        private UnityEvent onCureCompleted = new UnityEvent();

        [SerializeField]
        private UnityEvent onOutcomeTriggered = new UnityEvent();

        [SerializeField]
        private IncomeReceivedEvent onDailyIncomeReceived = new IncomeReceivedEvent();

        public event Action<ProvinceInfectionState> ProvinceStateChanged;
        public event Action<GlobalCureState> GlobalStateChanged;
        public event Action AllProvincesFullyInfected;
        public event Action CureCompleted;
        public UnityEvent<ProvinceInfectionState> OnProvinceStateChanged => onProvinceStateChanged;
        public UnityEvent<GlobalCureState> OnGlobalStateChanged => onGlobalStateChanged;
        public UnityEvent OnAllProvincesFullyInfected => onAllProvincesFullyInfected;
        public UnityEvent OnCureCompleted => onCureCompleted;
        public UnityEvent OnOutcomeTriggered => onOutcomeTriggered;
        public UnityEvent<int> OnDailyIncomeReceived => onDailyIncomeReceived;

        public event Action<int> DailyIncomeReceived;

        private readonly Dictionary<string, ProvinceInfectionState> provinces =
            new Dictionary<string, ProvinceInfectionState>(StringComparer.OrdinalIgnoreCase);

        private GlobalCureState globalState;
        private InGameTimeSnapshot? lastSnapshot;
        private bool initialized;
        private bool cureCompleteRaised;
        private bool allProvincesFullyInfectedRaised;
        private bool outcomeTriggered;
        private int lastSimulatedDayIndex = 1;
        private int lastSummaryDayIndex;
        private int lastIncomeDay;
        
        private bool isSpreadingEnabled;
private PlayerUpgrades playerUpgrades;

        public IReadOnlyDictionary<string, ProvinceInfectionState> Provinces => provinces;
        public GlobalCureState GlobalState => globalState;
        public OutpostCostConfig CostConfig => costConfig;
        public OutpostRateConfig OutpostRates => outpostRates;
        public VirusRateConfig VirusRates => virusRates;
        public PlayerUpgrades Upgrades => playerUpgrades;
        public IncomeConfig IncomeConfig => incomeConfig;

        private void Awake()
        {
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
            }

            if (dayNightController == null)
            {
                dayNightController = FindFirstObjectByType<DayNightCycleController>();
            }

            playerUpgrades = new PlayerUpgrades();
            
            // Initialize adjacency data
            if (adjacencyData == null)
            {
                adjacencyData = new ProvinceAdjacencyData();
            }
            
            isSpreadingEnabled = true;

            globalState = new GlobalCureState
            {
                CureProgress01 = 0f,
                TotalOutpostCount = 0,
                ActiveOutpostCount = 0,
                ZarBalance = startingZarBalance
            };
        }

        private void OnEnable()
        {
            if (dayNightController != null)
            {
                dayNightController.TimeUpdated += OnTimeUpdated;
                if (dayNightController.HasTime)
                {
                    lastSnapshot = dayNightController.CurrentTime;
                }
            }

            if (!initialized)
            {
                InitializeFromMap();
            }
        }

        private void OnDisable()
        {
            if (dayNightController != null)
            {
                dayNightController.TimeUpdated -= OnTimeUpdated;
            }
        }

        public void InitializeFromMap()
        {
            provinces.Clear();
            initialized = false;
            outcomeTriggered = false;
            lastSimulatedDayIndex = 1;
            lastSummaryDayIndex = 0;
            lastIncomeDay = 0;
            playerUpgrades?.Reset();
            GameOutcomeState.Reset();

            if (mapController == null)
            {
                Debug.LogWarning("[OutbreakSimulation] MapController is missing; cannot initialize provinces.");
                return;
            }

            var entries = mapController.Entries;
            if (entries == null || entries.Count == 0)
            {
                Debug.LogWarning("[OutbreakSimulation] No map entries available for initialization.");
                return;
            }

            var minSeed = Mathf.Clamp01(Mathf.Min(initialInfectionRange.x, initialInfectionRange.y));
            var maxSeed = Mathf.Clamp01(Mathf.Max(initialInfectionRange.x, initialInfectionRange.y));
            
            // Initialize all provinces with minimal baseline infection
            foreach (var entry in entries)
            {
                if (entry == null || string.IsNullOrEmpty(entry.RegionId))
                {
                    continue;
                }

                var infectionSeed = Mathf.Approximately(minSeed, maxSeed)
                    ? minSeed
                    : UnityEngine.Random.Range(minSeed, maxSeed);

                var state = new ProvinceInfectionState
                {
                    RegionId = entry.RegionId,
                    Infection01 = infectionSeed,
                    OutpostCount = 0,
                    OutpostDisabled = false,
                    IsFullyInfected = false
                };

                provinces[entry.RegionId] = state;
                RaiseProvinceStateChanged(state);
            }

            // Apply hotspot infection to random provinces
            ApplyHotspotInfection();

            globalState.CureProgress01 = 0f;
            globalState.ActiveOutpostCount = 0;
            globalState.TotalOutpostCount = 0;
            globalState.ZarBalance = startingZarBalance;
            RaiseGlobalStateChanged();

            initialized = true;
            cureCompleteRaised = false;
            allProvincesFullyInfectedRaised = false;
            
            if (logSummaryToConsole)
            {
                Debug.LogFormat("[OutbreakSimulation] Initialized {0} provinces with neighbor-based spreading enabled (threshold: {1:P0})",
                    provinces.Count, spreadThreshold);
            }
        }

        private void OnTimeUpdated(InGameTimeSnapshot snapshot)
        {
            if (!initialized)
            {
                InitializeFromMap();
            }

            if (!initialized)
            {
                return;
            }

            lastSimulatedDayIndex = snapshot.DayIndex;

            if (!lastSnapshot.HasValue)
            {
                lastSnapshot = snapshot;
                return;
            }

            var previous = lastSnapshot.Value;
            var dayDelta = snapshot.DayIndex - previous.DayIndex;
            var deltaMinutes = snapshot.TimeOfDayMinutes - previous.TimeOfDayMinutes + dayDelta * MinutesPerDay;
            if (deltaMinutes < 0f)
            {
                deltaMinutes = 0f;
            }

            lastSnapshot = snapshot;

            if (deltaMinutes <= 0f)
            {
                return;
            }

            var deltaHours = deltaMinutes / 60f;
            SimulateStep(deltaHours, snapshot.DayIndex);
        }

        private void SimulateStep(float deltaHours, int dayIndex)
        {
            if (deltaHours <= 0f || provinces.Count == 0)
            {
                return;
            }

            var virusStrengthFactor = 1f + Mathf.Max(0, dayIndex - 1) * virusRates.DailyVirusGrowth;
            var fullyInfectedCount = 0;

            foreach (var state in provinces.Values)
            {
                var previousInfection = state.Infection01;
                var previousDisabled = state.OutpostDisabled;
                var previousFullyInfected = state.IsFullyInfected;

                var infectionIncrease = Mathf.Max(0f, GetEffectiveBaseInfectionRate()) * virusStrengthFactor * deltaHours;
                var localCure = 0f;
                if (state.OutpostCount > 0 && !state.OutpostDisabled)
                {
                    localCure = Mathf.Max(0f, GetEffectiveLocalCureRate()) * state.OutpostCount * deltaHours;
                }

                state.Infection01 = Mathf.Clamp01(state.Infection01 + infectionIncrease - localCure);

                var effectiveOutpostDisableThreshold = GetEffectiveOutpostDisableThreshold();
                if (state.OutpostCount > 0)
                {
                    if (!state.OutpostDisabled && state.Infection01 >= effectiveOutpostDisableThreshold)
                    {
                        state.OutpostDisabled = true;
                    }
                    else if (state.OutpostDisabled && state.Infection01 < effectiveOutpostDisableThreshold)
                    {
                        state.OutpostDisabled = false;
                    }
                }
                else
                {
                    state.OutpostDisabled = false;
                }

                state.IsFullyInfected = state.Infection01 >= virusRates.FullyInfectedThreshold01;
                if (state.IsFullyInfected)
                {
                    fullyInfectedCount++;
                }

                if (!Mathf.Approximately(previousInfection, state.Infection01) || previousDisabled != state.OutpostDisabled || previousFullyInfected != state.IsFullyInfected)
                {
                    RaiseProvinceStateChanged(state);
                }
            }
            
            // Process viral spread between neighboring provinces
            ProcessViralSpread(deltaHours);

            UpdateGlobalCure(deltaHours);
            ProcessDailyIncome(dayIndex);
            EvaluateWinLoss(dayIndex);
            LogSummaryIfNeeded(dayIndex);

            var allFullyInfected = fullyInfectedCount == provinces.Count && provinces.Count > 0;
            if (allFullyInfected && !allProvincesFullyInfectedRaised)
            {
                allProvincesFullyInfectedRaised = true;
                onAllProvincesFullyInfected?.Invoke();
                AllProvincesFullyInfected?.Invoke();
            }
        }

        /// <summary>
        /// Processes viral spreading from infected provinces to their neighbors.
        /// </summary>
        private void ProcessViralSpread(float deltaHours)
        {
            if (!isSpreadingEnabled || deltaHours <= 0f || adjacencyData == null)
            {
                return;
            }

            var spreadEvents = new List<(string sourceProvince, string targetProvince, float spreadAmount)>();
            var effectiveSpreadThreshold = GetEffectiveSpreadThreshold();
            var effectiveSpreadAggressiveness = GetEffectiveSpreadAggressiveness();
            
            // Find provinces above spread threshold that can infect neighbors
            foreach (var sourceState in provinces.Values)
            {
                if (sourceState.Infection01 < effectiveSpreadThreshold || sourceState.IsFullyInfected)
                {
                    continue;
                }
                
                var neighbors = adjacencyData.GetNeighbors(sourceState.RegionId);
                foreach (var neighborId in neighbors)
                {
                    if (!provinces.TryGetValue(neighborId, out var targetState))
                    {
                        continue;
                    }
                    
                    // Calculate spread amount based on source infection level and time
                    var spreadMultiplier = (sourceState.Infection01 - effectiveSpreadThreshold) / (1f - effectiveSpreadThreshold);
                    var spreadAmount = baseSpreadRate * spreadMultiplier * effectiveSpreadAggressiveness * deltaHours;
                    
                    // Reduce spread if target is already heavily infected
                    var targetResistance = 1f - (targetState.Infection01 * 0.5f);
                    spreadAmount *= targetResistance;
                    
                    if (spreadAmount > 0.001f) // Only apply meaningful spread
                    {
                        spreadEvents.Add((sourceState.RegionId, neighborId, spreadAmount));
                    }
                }
            }
            
            // Apply all spread events
            var totalSpreadEvents = 0;
            foreach (var (sourceId, targetId, amount) in spreadEvents)
            {
                if (provinces.TryGetValue(targetId, out var targetState))
                {
                    var previousInfection = targetState.Infection01;
                    targetState.Infection01 = Mathf.Clamp01(targetState.Infection01 + amount);
                    
                    if (Mathf.Abs(targetState.Infection01 - previousInfection) > 0.001f)
                    {
                        RaiseProvinceStateChanged(targetState);
                        totalSpreadEvents++;
                        
                        if (logSummaryToConsole && amount > 0.01f)
                        {
                            Debug.LogFormat("[Viral Spread] {0} → {1}: +{2:P1} (now {3:P1})",
                                sourceId, targetId, amount, targetState.Infection01);
                        }
                    }
                }
            }
            
            if (logSummaryToConsole && totalSpreadEvents > 0)
            {
                Debug.LogFormat("[OutbreakSimulation] Processed {0} viral spread events this step", totalSpreadEvents);
            }
        }


        private void UpdateGlobalCure(float deltaHours)
        {
            var totalOutposts = 0;
            var activeOutposts = 0;
            var effectiveOutpostFactor = 0f;
            var activeIndex = 0;

            foreach (var state in provinces.Values)
            {
                if (state.OutpostCount <= 0)
                {
                    continue;
                }

                totalOutposts += state.OutpostCount;
                if (state.OutpostDisabled)
                {
                    continue;
                }

                activeOutposts += state.OutpostCount;
                for (int i = 0; i < state.OutpostCount; i++)
                {
                    var multiplier = OutbreakMath.ComputeGlobalOutpostMultiplierForIndex(activeIndex, outpostRates.DiminishingReturnFactor);
                    if (IsUrbanHub(state.RegionId))
                    {
                        multiplier *= urbanHubBonusMultiplier;
                    }

                    effectiveOutpostFactor += multiplier;
                    activeIndex++;
                }
            }

            globalState.TotalOutpostCount = totalOutposts;
            globalState.ActiveOutpostCount = activeOutposts;

            var effectiveGlobalRate = GetEffectiveGlobalCureRate();
            if (deltaHours > 0f && effectiveOutpostFactor > 0f && effectiveGlobalRate > 0f)
            {
                globalState.CureProgress01 = Mathf.Clamp01(
                    globalState.CureProgress01 + effectiveGlobalRate * effectiveOutpostFactor * deltaHours);
            }

            RaiseGlobalStateChanged();

            var cureThreshold = GetEffectiveCureThreshold();
            if (!cureCompleteRaised && globalState.CureProgress01 >= cureThreshold)
            {
                cureCompleteRaised = true;
                onCureCompleted?.Invoke();
                CureCompleted?.Invoke();
            }
        }

        public bool TryGetProvinceState(string regionId, out ProvinceInfectionState state)
        {
            state = null;
            if (string.IsNullOrEmpty(regionId))
            {
                return false;
            }

            return provinces.TryGetValue(regionId, out state);
        }

        public bool CanBuildOutpost(string regionId, out int costR, out OutpostBuildError error)
        {
            costR = GetEffectiveOutpostCost();
            error = OutpostBuildError.None;

            if (string.IsNullOrEmpty(regionId) || !provinces.TryGetValue(regionId, out var state))
            {
                error = OutpostBuildError.InvalidRegion;
                return false;
            }

            if (state.Infection01 >= virusRates.FullyInfectedThreshold01)
            {
                error = OutpostBuildError.ProvinceFullyInfected;
                return false;
            }

            if (globalState.ZarBalance < costR)
            {
                error = OutpostBuildError.NotEnoughZar;
                return false;
            }

            return true;
        }

public bool TryBuildOutpost(string regionId, out int costR, out OutpostBuildError error)
        {
            Debug.LogFormat("[OutbreakSimulation] TryBuildOutpost called for province: {0}", regionId);
            
            if (!CanBuildOutpost(regionId, out costR, out error))
            {
                Debug.LogFormat("[OutbreakSimulation] Cannot build outpost in {0}: {1} (Cost: R{2}, Balance: R{3})", 
                    regionId, error, costR, globalState?.ZarBalance ?? 0);
                return false;
            }

            var state = provinces[regionId];
            var previousBalance = globalState.ZarBalance;
            var previousOutpostCount = state.OutpostCount;
            
            globalState.ZarBalance -= costR;
            state.OutpostCount++;
            state.OutpostDisabled = state.Infection01 >= virusRates.OutpostDisableThreshold01;
            state.IsFullyInfected = state.Infection01 >= virusRates.FullyInfectedThreshold01;

            Debug.LogFormat("[OutbreakSimulation] Outpost deployed in {0}! Cost: R{1}, Balance: R{2}→R{3}, Outposts: {4}→{5}",
                regionId, costR, previousBalance, globalState.ZarBalance, previousOutpostCount, state.OutpostCount);

            // Ensure all state changes are propagated immediately
            RaiseProvinceStateChanged(state);
            UpdateGlobalCure(0f); // This will call RaiseGlobalStateChanged internally
            
            // Force immediate update of global state to ensure HUD refreshes
            RaiseGlobalStateChanged();
            
            EvaluateWinLoss(lastSimulatedDayIndex);
            return true;
        }

        private bool IsUrbanHub(string regionId)
        {
            if (urbanHubRegionIds == null)
            {
                return false;
            }

            foreach (var id in urbanHubRegionIds)
            {
                if (string.Equals(id, regionId, StringComparison.OrdinalIgnoreCase))
                {
                    return true;
                }
            }

            return false;
        }

        private void EvaluateWinLoss(int dayIndex)
        {
            if (outcomeTriggered || provinces.Count == 0)
            {
                return;
            }

            var fullyInfected = 0;
            foreach (var state in provinces.Values)
            {
                if (state.IsFullyInfected)
                {
                    fullyInfected++;
                }
            }

            var savedProvinces = Mathf.Max(0, provinces.Count - fullyInfected);
            var cureThreshold = GetEffectiveCureThreshold();

            if (globalState.CureProgress01 >= cureThreshold)
            {
                TriggerOutcome(GameOutcomeKind.Victory, dayIndex, savedProvinces, fullyInfected);
            }
            else if (fullyInfected == provinces.Count)
            {
                TriggerOutcome(GameOutcomeKind.Defeat, dayIndex, savedProvinces, fullyInfected);
            }
        }

        private void TriggerOutcome(GameOutcomeKind outcome, int dayIndex, int savedProvinces, int fullyInfectedProvinces)
        {
            if (outcomeTriggered)
            {
                return;
            }

            outcomeTriggered = true;
            GameOutcomeState.SetOutcome(outcome, globalState, dayIndex, savedProvinces, fullyInfectedProvinces);
            onOutcomeTriggered?.Invoke();
        }

        private void LogSummaryIfNeeded(int dayIndex)
        {
            if (!logSummaryToConsole || dayIndex <= 0)
            {
                return;
            }

            if (lastSummaryDayIndex == dayIndex)
            {
                return;
            }

            lastSummaryDayIndex = dayIndex;
            LogSimulationSummary(dayIndex);
        }

        private void LogSimulationSummary(int dayIndex)
        {
            if (provinces.Count == 0)
            {
                return;
            }

            float infectionSum = 0f;
            int fullyInfected = 0;
            foreach (var state in provinces.Values)
            {
                infectionSum += Mathf.Clamp01(state.Infection01);
                if (state.IsFullyInfected)
                {
                    fullyInfected++;
                }
            }

            var provinceCount = provinces.Count;
            var avgInfection = provinceCount > 0 ? infectionSum / provinceCount : 0f;
            var saved = Mathf.Max(0, provinceCount - fullyInfected);
            var curePercent = Mathf.RoundToInt(Mathf.Clamp01(globalState?.CureProgress01 ?? 0f) * 100f);
            var infectionPercent = Mathf.RoundToInt(Mathf.Clamp01(avgInfection) * 100f);
            var active = globalState?.ActiveOutpostCount ?? 0;
            var total = globalState?.TotalOutpostCount ?? 0;
            var budget = globalState?.ZarBalance ?? 0;

            Debug.LogFormat("[OutbreakSimulation] Day {0}: Cure {1}% | Avg infection {2}% | Provinces saved {3}/{4} | Outposts {5} active/{6} total | Budget R {7}",
                dayIndex,
                curePercent,
                infectionPercent,
                saved,
                provinceCount,
                active,
                total,
                budget);
        }

        private void RaiseProvinceStateChanged(ProvinceInfectionState state)
        {
            if (state == null)
            {
                return;
            }

            if (mapController != null)
            {
                mapController.SetProvinceInfectionLevel(state.RegionId, state.Infection01);
            }

            onProvinceStateChanged?.Invoke(state);
            ProvinceStateChanged?.Invoke(state);
        }

        private void RaiseGlobalStateChanged()
        {
            if (globalState == null)
            {
                return;
            }

            onGlobalStateChanged?.Invoke(globalState);
            GlobalStateChanged?.Invoke(globalState);
        }

        /// <summary>
        /// Calculates the daily income based on province health and upgrades.
        /// </summary>
        public int CalculateDailyIncome()
        {
            var healthyProvinceCount = 0;
            var fullyInfectedCount = 0;

            foreach (var state in provinces.Values)
            {
                if (state.IsFullyInfected)
                {
                    fullyInfectedCount++;
                }
                else if (state.Infection01 < virusRates.OutpostDisableThreshold01)
                {
                    healthyProvinceCount++;
                }
            }

            // Base income
            var baseIncome = incomeConfig.BaseDailyIncomeR;

            // Tax Efficiency upgrade: +15% base income per level
            var taxEfficiencyLevel = playerUpgrades?.GetLevel(UpgradeType.TaxEfficiency) ?? 0;
            var taxMultiplier = 1f + taxEfficiencyLevel * 0.15f;

            // Province health bonus
            var provinceBonus = healthyProvinceCount * incomeConfig.PerHealthyProvinceBonusR;

            // Economic Recovery upgrade: +R10 per healthy province per level
            var economicRecoveryLevel = playerUpgrades?.GetLevel(UpgradeType.EconomicRecovery) ?? 0;
            var economyBonus = economicRecoveryLevel * 10 * healthyProvinceCount;

            // Penalty for fully infected provinces
            var penalty = fullyInfectedCount * incomeConfig.PerFullyInfectedPenaltyR;

            var totalIncome = (int)((baseIncome + provinceBonus + economyBonus) * taxMultiplier) + penalty;
            return Mathf.Max(0, totalIncome);
        }

        private void ProcessDailyIncome(int dayIndex)
        {
            if (dayIndex <= lastIncomeDay || outcomeTriggered)
            {
                return;
            }

            var income = CalculateDailyIncome();
            if (income > 0)
            {
                globalState.ZarBalance += income;
                lastIncomeDay = dayIndex;
                onDailyIncomeReceived?.Invoke(income);
                DailyIncomeReceived?.Invoke(income);
                RaiseGlobalStateChanged();

                if (logSummaryToConsole)
                {
                    Debug.LogFormat("[OutbreakSimulation] Day {0} income: +R {1} (Budget now: R {2})",
                        dayIndex, income, globalState.ZarBalance);
                }
            }
        }

        /// <summary>
        /// Attempts to purchase an upgrade. Returns true if successful.
        /// </summary>
        public bool TryPurchaseUpgrade(UpgradeType type, out int cost, out int bonusAmount)
        {
            cost = 0;
            bonusAmount = 0;

            if (playerUpgrades == null || globalState == null)
            {
                return false;
            }

            var balance = globalState.ZarBalance;
            if (!playerUpgrades.TryPurchase(type, ref balance, out cost, out bonusAmount))
            {
                return false;
            }

            globalState.ZarBalance = balance;
            RaiseGlobalStateChanged();
            return true;
        }

        /// <summary>
        /// Gets the effective outpost cost after applying RapidDeployment upgrade discount.
        /// </summary>
        public int GetEffectiveOutpostCost()
        {
            var baseCost = OutbreakMath.ComputeOutpostCostR(globalState.TotalOutpostCount, costConfig);
            var rapidDeploymentLevel = playerUpgrades?.GetLevel(UpgradeType.RapidDeployment) ?? 0;
            var discount = rapidDeploymentLevel * 0.25f;
            return Mathf.Max(1, (int)(baseCost * (1f - discount)));
        }

        /// <summary>
        /// Gets the effective local cure rate after applying OutpostCapacity upgrade.
        /// </summary>
        public float GetEffectiveLocalCureRate()
        {
            var outpostCapacityLevel = playerUpgrades?.GetLevel(UpgradeType.OutpostCapacity) ?? 0;
            return outpostRates.LocalCurePerHour * (1f + outpostCapacityLevel * 0.50f);
        }

        /// <summary>
        /// Gets the effective global cure rate after applying ResearchEfficiency upgrade.
        /// </summary>
        public float GetEffectiveGlobalCureRate()
        {
            var researchEfficiencyLevel = playerUpgrades?.GetLevel(UpgradeType.ResearchEfficiency) ?? 0;
            return outpostRates.GlobalCurePerHourPerOutpost * (1f + researchEfficiencyLevel * 0.20f);
        }

        /// <summary>
        /// Gets the effective cure threshold after applying VaccineBreakthrough upgrade.
        /// </summary>
        public float GetEffectiveCureThreshold()
        {
            var vaccineBreakthroughLevel = playerUpgrades?.GetLevel(UpgradeType.VaccineBreakthrough) ?? 0;
            return 0.999f - vaccineBreakthroughLevel * 0.05f;
        }

        /// <summary>
        /// Gets the effective virus spread aggressiveness multiplier after applying ContainmentProtocols upgrade.
        /// </summary>
        public float GetEffectiveSpreadAggressiveness()
        {
            var containmentLevel = playerUpgrades?.GetLevel(UpgradeType.ContainmentProtocols) ?? 0;
            return spreadAggressivenessMultiplier * (1f - containmentLevel * 0.15f);
        }

        /// <summary>
        /// Gets the effective base infection rate after applying BorderSecurity upgrade.
        /// </summary>
        public float GetEffectiveBaseInfectionRate()
        {
            var borderSecurityLevel = playerUpgrades?.GetLevel(UpgradeType.BorderSecurity) ?? 0;
            return virusRates.BaseInfectionPerHour * (1f - borderSecurityLevel * 0.10f);
        }

        /// <summary>
        /// Gets the effective spread threshold after applying QuarantineMeasures upgrade.
        /// </summary>
        public float GetEffectiveSpreadThreshold()
        {
            var quarantineLevel = playerUpgrades?.GetLevel(UpgradeType.QuarantineMeasures) ?? 0;
            return spreadThreshold * (1f + quarantineLevel * 0.10f);
        }

        /// <summary>
        /// Gets the effective outpost disable threshold after applying MedicalStockpiles upgrade.
        /// </summary>
        public float GetEffectiveOutpostDisableThreshold()
        {
            var stockpilesLevel = playerUpgrades?.GetLevel(UpgradeType.MedicalStockpiles) ?? 0;
            return virusRates.OutpostDisableThreshold01 * (1f - stockpilesLevel * 0.05f);
        }

        /// <summary>
        /// Applies higher infection levels to random provinces as outbreak hotspots.
        /// </summary>
        private void ApplyHotspotInfection()
        {
            if (hotspotCount <= 0 || provinces.Count == 0)
            {
                return;
            }

            var provinceList = new List<ProvinceInfectionState>(provinces.Values);
            var actualHotspotCount = Mathf.Min(hotspotCount, provinceList.Count);
            
            // Fisher-Yates shuffle to select random provinces
            for (int i = 0; i < actualHotspotCount; i++)
            {
                var randomIndex = UnityEngine.Random.Range(i, provinceList.Count);
                (provinceList[i], provinceList[randomIndex]) = (provinceList[randomIndex], provinceList[i]);
            }

            var minHotspot = Mathf.Clamp01(Mathf.Min(hotspotInfectionRange.x, hotspotInfectionRange.y));
            var maxHotspot = Mathf.Clamp01(Mathf.Max(hotspotInfectionRange.x, hotspotInfectionRange.y));

            for (int i = 0; i < actualHotspotCount; i++)
            {
                var state = provinceList[i];
                var hotspotInfection = Mathf.Approximately(minHotspot, maxHotspot)
                    ? minHotspot
                    : UnityEngine.Random.Range(minHotspot, maxHotspot);
                
                state.Infection01 = Mathf.Max(state.Infection01, hotspotInfection);
                state.IsFullyInfected = state.Infection01 >= virusRates.FullyInfectedThreshold01;
                RaiseProvinceStateChanged(state);

                if (logSummaryToConsole)
                {
                    Debug.LogFormat("[OutbreakSimulation] Hotspot province: {0} at {1:P0} infection",
                        state.RegionId, state.Infection01);
                }
            }
        }

        [Serializable]
        private class ProvinceStateEvent : UnityEvent<ProvinceInfectionState> { }

        [Serializable]
        private class GlobalStateEvent : UnityEvent<GlobalCureState> { }

        [Serializable]
        private class IncomeReceivedEvent : UnityEvent<int> { }
    }
}
