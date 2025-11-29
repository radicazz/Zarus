using System;
using System.Collections.Generic;
using UnityEngine;
using UnityEngine.Events;
using UnityEngine.InputSystem;
using UnityEngine.EventSystems;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zarus.Map
{
    [DisallowMultipleComponent]
    public class RegionMapController : MonoBehaviour
    {
        [Header("Data")]
        [SerializeField]
        private RegionDatabase regionDatabase;

        [SerializeField]
        private TextAsset fallbackGeoJson;

        [Header("Rendering")]
        [SerializeField]
        private Material regionMaterial;

        [SerializeField]
        private Transform regionContainer;

        [SerializeField]
        private float mapScale = 10f;

        [SerializeField]
        private float regionDepth = -0.1f;

        [Header("Infection Visualization")]
        [SerializeField]
        private bool tintRegionsByInfection = true;

        [SerializeField]
        private Color maxInfectionColor = new Color(0.82f, 0.16f, 0.21f, 1f);

        [SerializeField, Range(0.1f, 3f)]
        private float infectionColorExponent = 1f;

        [Header("Animation")]
        [SerializeField]
        [Min(0f)]
        private float colorTransitionDuration = 0.2f;

        [Header("Interaction")]
        [SerializeField]
        private Camera interactionCamera;

        [SerializeField]
        private LayerMask interactionMask = ~0;

        [SerializeField]
        private int regionLayer = 0;

        [SerializeField]
        private RegionMapCameraController autoFocusController;

        [SerializeField]
        private float raycastDistance = 500f;

        [SerializeField]
        private bool enableHover = true;

        [SerializeField]
        private bool enableSelection = true;

        [SerializeField]
        private bool highlightSelection = true;

        [Header("Map Positioning")]
        [SerializeField]
        private bool useManualPositioning = true;

        [SerializeField, Range(-10f, 10f)]
        private float manualOffsetX = 0f;

        [SerializeField, Range(-10f, 10f)]
        private float manualOffsetY = 0f;

        [SerializeField]
        private bool autoUpdatePosition = true;

        [Header("Debug")]
        [SerializeField]
        private bool drawBoundsGizmo = true;

        [SerializeField]
        private Color gizmoColor = new Color(0.66f, 0.86f, 1f, 0.65f);

        [SerializeField]
        private Color gizmoSelectedColor = new Color(1f, 0.8f, 0.2f, 0.75f);

        [Header("Events")]
        [SerializeField]
        private RegionEntryEvent onRegionHovered = new();

        [SerializeField]
        private RegionEntryEvent onRegionSelected = new();

        private readonly List<RegionEntry> runtimeEntries = new();
        private readonly List<RegionRuntime> activeRegions = new();
        private readonly Dictionary<int, RegionRuntime> colliderLookup = new();
        private readonly Dictionary<string, RegionRuntime> regionIdLookup = new(StringComparer.OrdinalIgnoreCase);
        private readonly Dictionary<string, float> infectionLevels = new(StringComparer.OrdinalIgnoreCase);
        private readonly List<Mesh> runtimeGeneratedMeshes = new();
        private Bounds localBounds;
        private RegionRuntime currentHover;
        private RegionRuntime currentSelection;
        private bool interactionEnabled = true;
        private static readonly int BaseColorId = Shader.PropertyToID("_BaseColor");
        private static readonly int EmissionColorId = Shader.PropertyToID("_EmissionColor");
#if UNITY_EDITOR
        private bool pendingRebuild;
#endif

        public IReadOnlyList<RegionEntry> Entries => runtimeEntries;
        public RegionEntryEvent OnRegionHovered => onRegionHovered;
        public RegionEntryEvent OnRegionSelected => onRegionSelected;
        public Bounds LocalBounds => localBounds;
        public float MapScale => mapScale;
        public Transform RegionContainer => regionContainer;

        private void Reset()
        {
            interactionCamera = Camera.main;
        }

        private void Awake()
        {
            if (interactionCamera == null)
            {
                interactionCamera = Camera.main;
            }

            ResolveEntries();
            BuildRuntimeRegions();
        }

        private void OnDestroy()
        {
            CleanupRuntimeMeshes();
        }

        private void Update()
        {
            UpdateRegionAnimations(Time.deltaTime);

            if (!interactionEnabled)
            {
                return;
            }

            HandlePointer();
        }

#if UNITY_EDITOR
private void OnValidate()
        {
            if (!isActiveAndEnabled || pendingRebuild)
            {
                return;
            }

            // Handle real-time positioning updates during play mode
            if (autoUpdatePosition && useManualPositioning && Application.isPlaying)
            {
                CenterMapForUI();
                return;
            }

            // Handle editor-time rebuilds
            pendingRebuild = true;
            // Defer GameObject operations to avoid "SendMessage cannot be called during OnValidate" errors
            EditorApplication.delayCall += HandleDeferredRebuild;
        }

        private void HandleDeferredRebuild()
        {
            pendingRebuild = false;
            if (this != null && isActiveAndEnabled)
            {
                ResolveEntries();
                BuildRuntimeRegions();
            }
        }
#endif

        public Bounds GetWorldBounds()
        {
            var scaledCenter = new Vector3(localBounds.center.x * mapScale, localBounds.center.y * mapScale, 0f);
            var scaledSize = new Vector3(localBounds.size.x * mapScale, localBounds.size.y * mapScale, 0.1f);
            var worldCenter = transform.TransformPoint(scaledCenter);
            var worldBounds = new Bounds(worldCenter, scaledSize);
            return worldBounds;
        }

        public Vector3 GetWorldPosition(Vector3 normalizedPosition)
        {
            var scaled = new Vector3(normalizedPosition.x * mapScale, normalizedPosition.y * mapScale, 0f);
            return transform.TransformPoint(scaled);
        }

        public RegionEntry GetEntry(string regionId)
        {
            foreach (var entry in runtimeEntries)
            {
                if (string.Equals(entry.RegionId, regionId, StringComparison.OrdinalIgnoreCase))
                {
                    return entry;
                }
            }

            return null;
        }

        public void Rebuild()
        {
            ResolveEntries();
            BuildRuntimeRegions();
        }

        private void ResolveEntries()
        {
            if (regionDatabase == null)
            {
                regionDatabase = Resources.Load<RegionDatabase>("Map/RegionDatabase");
            }

            runtimeEntries.Clear();
            if (regionDatabase != null && regionDatabase.Regions != null && regionDatabase.Regions.Count > 0)
            {
                runtimeEntries.AddRange(regionDatabase.Regions);
                localBounds = regionDatabase.GlobalBounds;
            }
            else if (fallbackGeoJson != null)
            {
                var geometries = RegionGeometryFactory.ParseGeoJson(fallbackGeoJson.text, out var normalization);
                var (centeredMeshes, centroids) = RegionGeometryFactory.CreateCenteredMeshes(geometries, normalization);
                
                for (int i = 0; i < geometries.Count; i++)
                {
                    var geometry = geometries[i];
                    var mesh = centeredMeshes[i];
                    var centroid = centroids[i];
                    mesh.hideFlags = HideFlags.DontSave;
                    runtimeGeneratedMeshes.Add(mesh);
                    var entry = new RegionEntry();
                    entry.SetRuntimeData(geometry.Id, geometry.Name, mesh, centroid, mesh.bounds);
                    runtimeEntries.Add(entry);
                }

                localBounds = CalculateBounds(runtimeEntries);
            }
            else
            {
                localBounds = new Bounds(Vector3.zero, Vector3.one);
            }
        }

        private static Bounds CalculateBounds(IEnumerable<RegionEntry> entries)
        {
            var hasEntry = false;
            var bounds = new Bounds(Vector3.zero, Vector3.zero);
            foreach (var entry in entries)
            {
                if (!hasEntry)
                {
                    bounds = entry.Bounds;
                    hasEntry = true;
                }
                else
                {
                    bounds.Encapsulate(entry.Bounds);
                }
            }

            if (!hasEntry)
            {
                bounds = new Bounds(Vector3.zero, Vector3.one);
            }

            return bounds;
        }

        private void CleanupRuntimeMeshes()
        {
            // Clean up runtime-generated meshes when using fallback GeoJSON
            foreach (var mesh in runtimeGeneratedMeshes)
            {
                if (mesh != null)
                {
                    Destroy(mesh);
                }
            }
            runtimeGeneratedMeshes.Clear();
        }

        private void BuildRuntimeRegions()
        {
            CleanupRuntimeMeshes();
            EnsureContainer();
            foreach (Transform child in regionContainer)
            {
                if (Application.isPlaying)
                {
                    Destroy(child.gameObject);
                }
                else
                {
                    DestroyImmediate(child.gameObject);
                }
            }

            activeRegions.Clear();
            colliderLookup.Clear();
            regionIdLookup.Clear();
            regionContainer.localScale = new Vector3(mapScale, mapScale, 1f);

            if (runtimeEntries.Count == 0)
            {
                return;
            }

            var material = regionMaterial;
            if (material == null)
            {
                var shader = Shader.Find("Universal Render Pipeline/Unlit");
                material = shader != null ? new Material(shader) : new Material(Shader.Find("Sprites/Default"));
                material.name = "RegionFillRuntime";
            }

            foreach (var entry in runtimeEntries)
            {
                if (entry.Mesh == null)
                {
                    continue;
                }

                var regionObject = new GameObject(entry.DisplayName)
                {
                    layer = Mathf.Clamp(regionLayer, 0, 31)
                };
                regionObject.transform.SetParent(regionContainer, false);
                regionObject.transform.localPosition = new Vector3(0f, 0f, regionDepth);
                regionObject.transform.localRotation = Quaternion.identity;

                var meshFilter = regionObject.AddComponent<MeshFilter>();
                meshFilter.sharedMesh = entry.Mesh;

                var renderer = regionObject.AddComponent<MeshRenderer>();
                renderer.sharedMaterial = material;
                renderer.shadowCastingMode = UnityEngine.Rendering.ShadowCastingMode.Off;
                renderer.receiveShadows = false;

                var collider = regionObject.AddComponent<MeshCollider>();
                collider.sharedMesh = entry.Mesh;

                var runtime = new RegionRuntime(entry, renderer, collider);
                var targetColor = GetBaseColorForEntry(entry);
                runtime.UpdateColor(targetColor, true, colorTransitionDuration);
                activeRegions.Add(runtime);
                colliderLookup[collider.GetInstanceID()] = runtime;
                if (!string.IsNullOrEmpty(entry.RegionId))
                {
                    regionIdLookup[entry.RegionId] = runtime;
                }
            }

            // Center the map considering HUD bars
            CenterMapForUI();
        }

        private void UpdateRegionAnimations(float deltaTime)
        {
            if (activeRegions.Count == 0)
            {
                return;
            }

            foreach (var region in activeRegions)
            {
                region.TickColorAnimation(deltaTime);
            }
        }

        private void HandlePointer()
        {
            if (!enableHover && !enableSelection)
            {
                return;
            }

            var cam = interactionCamera != null ? interactionCamera : Camera.main;
            if (cam == null)
            {
                return;
            }

            if (!TryGetPointerPosition(out var pointerPosition, out var pressedThisFrame))
            {
                ClearHover();
                return;
            }

            // Check if pointer is over UI elements - if so, don't process province clicks
            // BUT: Don't clear selection on click if we have a current selection (to prevent race condition with world-space UI)
            if (IsPointerOverUI(pointerPosition))
            {
                ClearHover();
                // Don't clear selection when clicking if we have an active selection - let world-space UI handle the click first
                if (pressedThisFrame && currentSelection != null)
                {
                    // Defer selection clearing to next frame to allow UI button events to process first
                    StartCoroutine(DeferredSelectionClear());
                }
                return;
            }

            var ray = cam.ScreenPointToRay(pointerPosition);
            if (Physics.Raycast(ray, out var hitInfo, raycastDistance, interactionMask))
            {
                if (colliderLookup.TryGetValue(hitInfo.collider.GetInstanceID(), out var runtime))
                {
                    if (enableHover && runtime != currentHover)
                    {
                        SetHover(runtime);
                    }

                    if (enableSelection && pressedThisFrame)
                    {
                        SetSelection(runtime);
                    }
                }
            }
            else
            {
                ClearHover();
                if (enableSelection && pressedThisFrame)
                {
                    ClearSelection();
                    autoFocusController?.FocusOnWholeMap();
                }
            }
        }

/// <summary>
        /// Checks if the pointer is over any UI elements, preventing province clicks through UI.
        /// Handles both mouse and touch input properly.
        /// </summary>
        private bool IsPointerOverUI(Vector2 pointerPosition)
        {
            // Check if EventSystem exists
            if (EventSystem.current == null)
            {
                return false;
            }

            // For mouse input
            var mouse = Mouse.current;
            if (mouse != null)
            {
                return EventSystem.current.IsPointerOverGameObject();
            }

            // For touch input, we need to check each active touch
            var touch = Touchscreen.current;
            if (touch != null)
            {
                var primary = touch.primaryTouch;
                if (primary.press.isPressed)
                {
                    // For touch, we need to provide the touch ID
                    var touchId = primary.touchId.ReadValue();
                    return EventSystem.current.IsPointerOverGameObject((int)touchId);
                }
            }

            // WebGL fallback - check using legacy input system
            #if UNITY_WEBGL && !UNITY_EDITOR
            if (Input.touchSupported && Input.touchCount > 0)
            {
                var touchId = Input.GetTouch(0).fingerId;
                return EventSystem.current.IsPointerOverGameObject(touchId);
            }
            else
            {
                return EventSystem.current.IsPointerOverGameObject();
            }
            #endif

            return false;
        }


        private bool TryGetPointerPosition(out Vector2 position, out bool clicked)
        {
            position = default;
            clicked = false;

            var mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                clicked = mouse.leftButton.wasPressedThisFrame;
                
                // WebGL fallback: If Input System fails, use legacy Input
                #if UNITY_WEBGL && !UNITY_EDITOR
                if (position == Vector2.zero)
                {
                    position = Input.mousePosition;
                    clicked = Input.GetMouseButtonDown(0);
                }
                #endif
                
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                var primary = touch.primaryTouch;
                if (primary.press.isPressed)
                {
                    position = primary.position.ReadValue();
                    clicked = primary.press.wasPressedThisFrame;
                    return true;
                }
            }
            
            // WebGL ultimate fallback: Use legacy Input system
            #if UNITY_WEBGL && !UNITY_EDITOR
            position = Input.mousePosition;
            clicked = Input.GetMouseButtonDown(0);
            return true;
            #endif

            return false;
        }

        private void SetHover(RegionRuntime runtime)
        {
            if (currentHover == runtime)
            {
                return;
            }

            if (currentHover != null && currentHover != currentSelection)
            {
                currentHover.UpdateColor(GetBaseColorForEntry(currentHover.Entry), false, colorTransitionDuration);
            }

            currentHover = runtime;
            if (currentHover != null && currentHover != currentSelection)
            {
                currentHover.UpdateColor(currentHover.Entry.VisualStyle.HoverColor, false, colorTransitionDuration);
                onRegionHovered?.Invoke(currentHover.Entry);
            }
        }

        private void ClearHover()
        {
            if (currentHover != null && currentHover != currentSelection)
            {
                currentHover.UpdateColor(GetBaseColorForEntry(currentHover.Entry), false, colorTransitionDuration);
            }

            currentHover = null;
        }

private void SetSelection(RegionRuntime runtime)
        {
            // Check if clicking on already selected province to deselect
            if (currentSelection == runtime)
            {
                ClearSelection();
                autoFocusController?.FocusOnWholeMap();
                return;
            }

            if (!highlightSelection)
            {
                onRegionSelected?.Invoke(runtime.Entry);
                // Allow normal zoom behavior when selecting provinces
                autoFocusController?.FocusOnRegion(runtime.Entry);
                return;
            }

            if (currentSelection != null && currentSelection != runtime)
            {
                currentSelection.UpdateColor(GetBaseColorForEntry(currentSelection.Entry), false, colorTransitionDuration);
            }

            currentSelection = runtime;
            currentSelection?.UpdateColor(currentSelection.Entry.VisualStyle.SelectedColor, false, colorTransitionDuration);
            onRegionSelected?.Invoke(runtime.Entry);
            // Allow normal zoom behavior when selecting provinces
            autoFocusController?.FocusOnRegion(runtime.Entry);
        }

        private void ClearSelection()
        {
            if (currentSelection != null && highlightSelection)
            {
                currentSelection.UpdateColor(GetBaseColorForEntry(currentSelection.Entry), false, colorTransitionDuration);
            }

            currentSelection = null;
            onRegionSelected?.Invoke(null);
        }

#if UNITY_EDITOR
        private void OnDrawGizmos()
        {
            if (!drawBoundsGizmo)
            {
                return;
            }

            Gizmos.color = gizmoColor;
            var bounds = GetWorldBounds();
            Gizmos.DrawWireCube(bounds.center, bounds.size);

            if (currentSelection != null)
            {
                Gizmos.color = gizmoSelectedColor;
                var center = GetWorldPosition(currentSelection.Entry.Centroid);
                var size = currentSelection.Entry.Bounds.size * mapScale;
                Gizmos.DrawWireCube(center, new Vector3(size.x, size.y, 0.05f));
            }
        }
#endif

        private void EnsureContainer()
        {
            if (regionContainer != null)
            {
                return;
            }

            var containerGo = new GameObject("RegionContainer");
            containerGo.transform.SetParent(transform, false);
            regionContainer = containerGo.transform;
        }

        /// <summary>
        /// Centers the map in the available screen space, accounting for HUD bars at top and bottom
        /// </summary>
        private void CenterMapForUI()
        {
            if (regionContainer == null || interactionCamera == null)
            {
                return;
            }

            // Simply center the map at the camera's position in the XY plane
            var cameraPosition = interactionCamera.transform.position;
            var targetPosition = new Vector3(cameraPosition.x, cameraPosition.y, 0f);
            
            // Add manual positioning offsets if enabled
            if (useManualPositioning)
            {
                targetPosition.x += manualOffsetX;
                targetPosition.y += manualOffsetY;
                
                // Apply bounds checking to prevent map from going too far offscreen
                var cameraHeight = interactionCamera.orthographicSize;
                var cameraWidth = cameraHeight * interactionCamera.aspect;
                
                // Allow map to move but keep some portion visible
                var maxOffset = cameraWidth * 0.8f; // Allow 80% of screen width offset
                targetPosition.x = Mathf.Clamp(targetPosition.x, 
                    cameraPosition.x - maxOffset, 
                    cameraPosition.x + maxOffset);
                targetPosition.y = Mathf.Clamp(targetPosition.y, 
                    cameraPosition.y - cameraHeight * 0.8f, // Even less Y movement to account for UI
                    cameraPosition.y + cameraHeight * 0.8f);
            }
            
            regionContainer.position = targetPosition;
            
            if (Application.isEditor && Time.frameCount % 60 == 0) // Log only once per second to avoid spam
            {
                Debug.Log($"[RegionMapController] Centered map at position: {targetPosition}. Manual offsets: ({manualOffsetX}, {manualOffsetY})");
            }
        }

        /// <summary>
        /// Public method to recenter the map (useful for runtime adjustments)
        /// </summary>
        public void RecenterMapForUI()
        {
            CenterMapForUI();
        }

        public void SetInteractionEnabled(bool enabled)
        {
            if (interactionEnabled == enabled)
            {
                return;
            }

            interactionEnabled = enabled;

            if (!interactionEnabled)
            {
                ClearHover();
            }
        }

        public void SetGlobalEmissionMultiplier(float multiplier)
        {
            var clamped = Mathf.Max(0f, multiplier);
            foreach (var region in activeRegions)
            {
            region.SetEmissionScale(clamped);
            }
        }

        public void SetProvinceInfectionLevel(string regionId, float infection01)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                return;
            }

            infectionLevels[regionId] = Mathf.Clamp01(infection01);

            if (!tintRegionsByInfection || !regionIdLookup.TryGetValue(regionId, out var runtime))
            {
                return;
            }

            if (runtime == currentHover || runtime == currentSelection)
            {
                return;
            }

            runtime.UpdateColor(GetBaseColorForEntry(runtime.Entry), false, colorTransitionDuration);
        }

        private float GetInfectionLevel(string regionId)
        {
            if (string.IsNullOrEmpty(regionId))
            {
                return 0f;
            }

            return infectionLevels.TryGetValue(regionId, out var value) ? value : 0f;
        }

        private Color GetBaseColorForEntry(RegionEntry entry)
        {
            if (entry == null)
            {
                return Color.white;
            }

            var baseColor = entry.VisualStyle != null ? entry.VisualStyle.BaseColor : Color.white;
            if (!tintRegionsByInfection)
            {
                return baseColor;
            }

            var infection = GetInfectionLevel(entry.RegionId);
            if (infection <= 0f)
            {
                return baseColor;
            }

            if (!Mathf.Approximately(infectionColorExponent, 1f))
            {
                infection = Mathf.Pow(infection, infectionColorExponent);
            }

            return Color.Lerp(baseColor, maxInfectionColor, infection);
        }

        /// <summary>
        /// Deferred selection clearing to prevent race condition with world-space UI button clicks
        /// </summary>
/// <summary>
        /// Deferred selection clearing to prevent race condition with world-space UI button clicks
        /// </summary>
/// <summary>
        /// Deferred selection clearing to prevent race condition with world-space UI button clicks
        /// </summary>
/// <summary>
        /// Deferred selection clearing to prevent race condition with world-space UI button clicks
        /// </summary>
/// <summary>
        /// Deferred selection clearing to prevent race condition with world-space UI button clicks
        /// </summary>
        private System.Collections.IEnumerator DeferredSelectionClear()
        {
            yield return new WaitForEndOfFrame();
            
            // Check if we have a province panel that might be handling a deployment
            var provincePanelController = FindFirstObjectByType<MonoBehaviour>();
            var hasDeploymentInProgress = false;
            
            if (provincePanelController != null && provincePanelController.name.Contains("ProvincePanelController"))
            {
                // Use reflection to check deployment status to avoid assembly reference issues
                var deploymentProperty = provincePanelController.GetType().GetProperty("IsDeploymentInProgress");
                if (deploymentProperty != null)
                {
                    hasDeploymentInProgress = (bool)deploymentProperty.GetValue(provincePanelController);
                }
            }
            
            // Only clear if selection still exists and no UI consumed the click
            // Also don't clear if a deployment is in progress
            if (currentSelection != null && !hasDeploymentInProgress)
            {
                ClearSelection();
                autoFocusController?.FocusOnWholeMap();
            }
        }

        [Serializable]
        public class RegionEntryEvent : UnityEvent<RegionEntry> { }

        private sealed class RegionRuntime
        {
            public RegionEntry Entry { get; }
            public MeshRenderer Renderer { get; }
            public MeshCollider Collider { get; }
            private readonly MaterialPropertyBlock propertyBlock = new();
            private float emissionScale = 0.1f;

            public RegionRuntime(RegionEntry entry, MeshRenderer renderer, MeshCollider collider)
            {
                Entry = entry;
                Renderer = renderer;
                Collider = collider;
            }

            private Color currentColor;
            private Color startColor;
            private Color targetColor;
            private float colorLerpTime;
            private float colorLerpDuration;
            private bool colorAnimating;

            public void UpdateColor(Color color, bool instant, float transitionDuration)
            {
                if (instant || transitionDuration <= 0f)
                {
                    currentColor = color;
                    targetColor = color;
                    colorAnimating = false;
                    ApplyColor(color);
                    return;
                }

                startColor = currentColor;
                targetColor = color;
                colorLerpTime = 0f;
                colorLerpDuration = transitionDuration;
                colorAnimating = true;
            }

            public void TickColorAnimation(float deltaTime)
            {
                if (!colorAnimating)
                {
                    return;
                }

                colorLerpTime += deltaTime;
                var t = Mathf.Clamp01(colorLerpTime / Mathf.Max(colorLerpDuration, Mathf.Epsilon));
                var eased = Mathf.SmoothStep(0f, 1f, t);
                currentColor = Color.Lerp(startColor, targetColor, eased);
                ApplyColor(currentColor);

                if (t >= 1f)
                {
                    colorAnimating = false;
                }
            }

            private void ApplyColor(Color color)
            {
                propertyBlock.SetColor(BaseColorId, color);
                propertyBlock.SetColor(EmissionColorId, color * emissionScale);
                Renderer.SetPropertyBlock(propertyBlock);
            }

            public void SetEmissionScale(float scale)
            {
                emissionScale = Mathf.Max(0f, scale);
                ApplyColor(currentColor);
            }
        }
    }
}
