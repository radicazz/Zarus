using System;
using System.Collections.Generic;
using UnityEngine;

namespace Zarus.Systems
{
    /// <summary>
    /// Contains province neighbor mappings based on South African geography.
    /// Used for viral spreading between adjacent provinces.
    /// </summary>
    [Serializable]
    public class ProvinceAdjacencyData
    {
        [Header("Province Neighbor Mappings")]
        [SerializeField]
        private ProvinceNeighborMapping[] neighborMappings = new ProvinceNeighborMapping[]
        {
            // Gauteng (ZAGP) - central province, borders many others
            new ProvinceNeighborMapping("ZAGP", new string[] { "ZALP", "ZANW", "ZAMP", "ZAFS" }),
            
            // Western Cape (ZAWC) - southwestern province
            new ProvinceNeighborMapping("ZAWC", new string[] { "ZAEC", "ZANC" }),
            
            // KwaZulu-Natal (ZAKZN) - eastern coastal province
            new ProvinceNeighborMapping("ZAKZN", new string[] { "ZAEC", "ZAFS", "ZALP", "ZAMP" }),
            
            // Eastern Cape (ZAEC) - southern coastal province
            new ProvinceNeighborMapping("ZAEC", new string[] { "ZAWC", "ZANC", "ZAFS", "ZAKZN" }),
            
            // Free State (ZAFS) - central inland province
            new ProvinceNeighborMapping("ZAFS", new string[] { "ZAGP", "ZALP", "ZAKZN", "ZAEC", "ZANC" }),
            
            // Limpopo (ZALP) - northern province
            new ProvinceNeighborMapping("ZALP", new string[] { "ZAGP", "ZANW", "ZAMP", "ZAKZN", "ZAFS" }),
            
            // North West (ZANW) - western inland province
            new ProvinceNeighborMapping("ZANW", new string[] { "ZAGP", "ZALP", "ZANC" }),
            
            // Mpumalanga (ZAMP) - eastern inland province
            new ProvinceNeighborMapping("ZAMP", new string[] { "ZAGP", "ZALP", "ZAKZN" }),
            
            // Northern Cape (ZANC) - largest province, sparsely populated
            new ProvinceNeighborMapping("ZANC", new string[] { "ZAWC", "ZAEC", "ZAFS", "ZANW" })
        };

        private readonly Dictionary<string, HashSet<string>> neighborMap = new Dictionary<string, HashSet<string>>(StringComparer.OrdinalIgnoreCase);

        public ProvinceAdjacencyData()
        {
            BuildNeighborMap();
        }

        private void BuildNeighborMap()
        {
            neighborMap.Clear();
            
            foreach (var mapping in neighborMappings)
            {
                if (string.IsNullOrEmpty(mapping.ProvinceId) || mapping.Neighbors == null)
                    continue;
                    
                var neighbors = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
                foreach (var neighbor in mapping.Neighbors)
                {
                    if (!string.IsNullOrEmpty(neighbor))
                        neighbors.Add(neighbor);
                }
                
                neighborMap[mapping.ProvinceId] = neighbors;
            }
        }

        /// <summary>
        /// Gets the neighbors of a specific province.
        /// </summary>
        public IEnumerable<string> GetNeighbors(string provinceId)
        {
            if (string.IsNullOrEmpty(provinceId) || !neighborMap.TryGetValue(provinceId, out var neighbors))
                return Array.Empty<string>();
                
            return neighbors;
        }

        /// <summary>
        /// Checks if two provinces are neighbors.
        /// </summary>
        public bool AreNeighbors(string provinceA, string provinceB)
        {
            if (string.IsNullOrEmpty(provinceA) || string.IsNullOrEmpty(provinceB))
                return false;
                
            return neighborMap.TryGetValue(provinceA, out var neighbors) && neighbors.Contains(provinceB);
        }

        /// <summary>
        /// Gets all provinces that have neighbor mappings.
        /// </summary>
        public IEnumerable<string> GetAllProvinces()
        {
            return neighborMap.Keys;
        }
    }

    /// <summary>
    /// Represents a province and its neighbors for serialization.
    /// </summary>
    [Serializable]
    public struct ProvinceNeighborMapping
    {
        [SerializeField]
        public string ProvinceId;
        
        [SerializeField]
        public string[] Neighbors;

        public ProvinceNeighborMapping(string provinceId, string[] neighbors)
        {
            ProvinceId = provinceId;
            Neighbors = neighbors ?? Array.Empty<string>();
        }
    }
}