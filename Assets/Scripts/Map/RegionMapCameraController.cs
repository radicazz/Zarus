using UnityEngine;
using UnityEngine.InputSystem;

namespace Zarus.Map
{
    [RequireComponent(typeof(Camera))]
    [DisallowMultipleComponent]
    public class RegionMapCameraController : MonoBehaviour
    {
        [SerializeField]
        private RegionMapController mapController;

        [SerializeField]
        private float minOrthoSize = 3f;

        [SerializeField]
        private float maxOrthoSize = 18f;

        [SerializeField]
        private float zoomSpeed = 0.15f;

        [SerializeField]
        private float panSpeed = 0.001f;

        [SerializeField]
        private float focusLerpSpeed = 5f;

        [SerializeField]
        private bool clampToBounds = true;

        [SerializeField]
        private Vector2 clampPadding = new Vector2(0.5f, 0.5f);

        [SerializeField]
        private bool drawDebugBounds;

        private Camera targetCamera;
        private Vector3 targetPosition;
        private float targetOrthoSize;
        private Vector2 previousPointerPosition;
        private bool dragging;

        private void Awake()
        {
            targetCamera = GetComponent<Camera>();
            targetPosition = transform.position;
            targetOrthoSize = targetCamera.orthographicSize;
            if (mapController == null)
            {
                mapController = FindFirstObjectByType<RegionMapController>();
                if (mapController == null)
                {
                    Debug.LogWarning("[RegionMapCameraController] No RegionMapController found in scene. Camera bounds clamping will be disabled.");
                }
            }
        }

        private void Start()
        {
            FocusOnWholeMap(true);
        }

        private void LateUpdate()
        {
            HandleZoom();
            HandlePan();
            ApplyCameraState();
        }

public void FocusOnRegion(RegionEntry entry, bool shouldZoom = true)
        {
            if (entry == null || mapController == null || targetCamera == null)
            {
                return;
            }

            var worldPos = mapController.GetWorldPosition(entry.Centroid);
            
            // Only zoom if explicitly requested
            if (shouldZoom)
            {
                // Zoom in 2x further than the minimum ortho size
                targetOrthoSize = Mathf.Max(minOrthoSize / 2f, 0.5f); // Ensure a reasonable minimum
            }
            
            targetPosition = new Vector3(worldPos.x, worldPos.y, transform.position.z);

            if (clampToBounds && mapController != null)
            {
                targetPosition = ClampPosition(targetPosition);
            }
        }

        public void FocusOnRegionById(string regionId)
        {
            if (mapController == null)
            {
                return;
            }

            var entry = mapController.GetEntry(regionId);
            FocusOnRegion(entry);
        }

        public void FocusOnWholeMap(bool instant = false)
        {
            if (mapController == null || targetCamera == null)
            {
                return;
            }

            var bounds = mapController.GetWorldBounds();
            var baseTarget = new Vector3(bounds.center.x, bounds.center.y, transform.position.z);
            var extents = bounds.extents;
            var paddedExtents = new Vector2(extents.x + clampPadding.x, extents.y + clampPadding.y);
            var aspect = Mathf.Max(targetCamera.aspect, 0.01f);
            var requiredSize = Mathf.Max(paddedExtents.y, paddedExtents.x / aspect);
            targetOrthoSize = Mathf.Clamp(requiredSize, minOrthoSize, maxOrthoSize);
            targetPosition = baseTarget;

            if (clampToBounds && mapController != null)
            {
                targetPosition = ClampPosition(targetPosition);
            }

            if (instant)
            {
                transform.position = targetPosition;
                targetCamera.orthographicSize = targetOrthoSize;
            }
        }

        private void HandleZoom()
        {
            var scrollDelta = 0f;
            var mouse = Mouse.current;
            if (mouse != null)
            {
                scrollDelta += mouse.scroll.ReadValue().y;
            }

            if (!Mathf.Approximately(scrollDelta, 0f))
            {
                targetOrthoSize = Mathf.Clamp(targetOrthoSize - scrollDelta * zoomSpeed, minOrthoSize, maxOrthoSize);
            }
        }

        private void HandlePan()
        {
            if (targetCamera == null)
            {
                return;
            }

            TryGetPointer(out var pointerPosition, out var dragInputActive);
            if (dragInputActive)
            {
                if (!dragging)
                {
                    dragging = true;
                    previousPointerPosition = pointerPosition;
                }
                else
                {
                    var delta = pointerPosition - previousPointerPosition;
                    previousPointerPosition = pointerPosition;
                    
                    // Apply deadzone to prevent micro-movements from causing drift
                    var deadzone = 2f; // pixels
                    if (delta.magnitude < deadzone)
                    {
                        return;
                    }
                    
                    // More controlled and less sensitive panning calculation
                    var normalizedDelta = new Vector2(-delta.x / Screen.width, -delta.y / Screen.height);
                    var scaleFactor = Mathf.Clamp(targetCamera.orthographicSize / 10f, 0.1f, 2f);
                    var scaledDelta = new Vector3(normalizedDelta.x * targetCamera.orthographicSize * panSpeed * scaleFactor,
                                                  normalizedDelta.y * targetCamera.orthographicSize * panSpeed * scaleFactor,
                                                  0f);
                    
                    // Apply maximum movement per frame to prevent jumps
                    var maxMovePerFrame = targetCamera.orthographicSize * 0.1f;
                    scaledDelta = Vector3.ClampMagnitude(scaledDelta, maxMovePerFrame);
                    
                    targetPosition += scaledDelta;
                }
            }
            else
            {
                dragging = false;
            }

            if (clampToBounds && mapController != null)
            {
                targetPosition = ClampPosition(targetPosition);
            }
        }

        private Vector3 ClampPosition(Vector3 desired)
        {
            var bounds = mapController.GetWorldBounds();
            
            // Calculate camera bounds at current zoom level
            var cameraHeight = targetCamera.orthographicSize;
            var cameraWidth = cameraHeight * targetCamera.aspect;
            
            // Account for padding and camera bounds
            var extents = bounds.extents;
            extents.x = Mathf.Max(cameraWidth, extents.x - clampPadding.x);
            extents.y = Mathf.Max(cameraHeight, extents.y - clampPadding.y);
            
            var min = bounds.center - extents;
            var max = bounds.center + extents;
            
            desired.x = Mathf.Clamp(desired.x, min.x, max.x);
            desired.y = Mathf.Clamp(desired.y, min.y, max.y);
            
            return desired;
        }

        private bool TryGetPointer(out Vector2 position, out bool dragActive)
        {
            position = default;
            dragActive = false;
            
            // Try new Input System first
            var mouse = Mouse.current;
            if (mouse != null)
            {
                position = mouse.position.ReadValue();
                dragActive = mouse.rightButton.isPressed || mouse.middleButton.isPressed;
                
                // WebGL fallback: If Input System fails, use legacy Input
                #if UNITY_WEBGL && !UNITY_EDITOR
                if (position == Vector2.zero)
                {
                    position = Input.mousePosition;
                    dragActive = Input.GetMouseButton(1) || Input.GetMouseButton(2);
                }
                #endif
                
                return true;
            }

            var touch = Touchscreen.current;
            if (touch != null)
            {
                var primary = touch.primaryTouch;
                dragActive = primary.press.isPressed;
                if (dragActive)
                {
                    position = primary.position.ReadValue();
                    return true;
                }
            }
            
            // WebGL ultimate fallback: Use legacy Input system
            #if UNITY_WEBGL && !UNITY_EDITOR
            position = Input.mousePosition;
            dragActive = Input.GetMouseButton(1) || Input.GetMouseButton(2);
            return true;
            #endif

            return false;
        }

        private void ApplyCameraState()
        {
            transform.position = Vector3.Lerp(transform.position, targetPosition, Time.deltaTime * focusLerpSpeed);
            targetCamera.orthographicSize = Mathf.Lerp(targetCamera.orthographicSize, targetOrthoSize, Time.deltaTime * focusLerpSpeed);
        }

#if UNITY_EDITOR
        private void OnDrawGizmosSelected()
        {
            if (!drawDebugBounds || mapController == null)
            {
                return;
            }

            var bounds = mapController.GetWorldBounds();
            Gizmos.color = Color.cyan;
            Gizmos.DrawWireCube(bounds.center, bounds.size + new Vector3(clampPadding.x * 2f, clampPadding.y * 2f, 0f));
        }
#endif
    }
}
