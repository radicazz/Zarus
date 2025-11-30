using UnityEngine;
using UnityEngine.InputSystem;
using UnityEngine.SceneManagement;
using Zarus.Map;
#if UNITY_EDITOR
using UnityEditor;
#endif

namespace Zarus.UI
{
    /// <summary>
    /// Central manager for all UI screens in the game.
    /// Singleton pattern ensures only one instance exists.
    /// </summary>
    public class UIManager : MonoBehaviour
    {
        private static UIManager instance;
        public static UIManager Instance
        {
            get
            {
                if (instance == null)
                {
                    instance = FindFirstObjectByType<UIManager>();
                    if (instance == null)
                    {
                        Debug.LogError("[UIManager] No UIManager found in scene. Please add one.");
                    }
                }
                return instance;
            }
        }

        [Header("UI Screens")]
        [SerializeField]
        private PauseMenu pauseMenu;

        [SerializeField]
        private GameHUD gameHUD;

        [Header("Gameplay Systems")]
        [SerializeField]
        private RegionMapController[] managedMapControllers;

        [Header("Cursor")]
        [SerializeField]
        private Texture2D cursorTexture;

        [SerializeField]
        private Vector2 cursorHotspot = new Vector2(8f, 4f);

        [SerializeField, Range(0.01f, 1f)]
        private float cursorScale = 0.033333335f;

        [Header("Input")]
        [SerializeField]
        private InputActionAsset inputActions;

        [Header("Scene Flow")]
        [SerializeField]
        private string startSceneName = "Start";

        [SerializeField]
        private string gameplaySceneName = "Main";

        [SerializeField]
        private string endSceneName = "End";

        private InputAction pauseAction;
        private InputActionMap playerActionMap;
        private InputActionMap uiActionMap;

        private bool isPaused;
        public bool IsPaused => isPaused;
        private RegionMapController[] cachedMapControllers;
        private Texture2D runtimeCursorTexture;

        private void Awake()
        {
            // Singleton pattern
            if (instance != null && instance != this)
            {
                Destroy(gameObject);
                return;
            }
            instance = this;

            // Initialize input system
            InitializeInput();
            SetupCursor();
        }

        private void OnEnable()
        {
            if (pauseAction != null)
            {
                pauseAction.Enable();
                pauseAction.performed += OnPausePerformed;
            }
        }

        private void OnDisable()
        {
            if (pauseAction != null)
            {
                pauseAction.performed -= OnPausePerformed;
                pauseAction.Disable();
            }

            if (runtimeCursorTexture != null)
            {
                Destroy(runtimeCursorTexture);
                runtimeCursorTexture = null;
            }
        }

        private void Start()
        {
            // Show HUD on game start
            EnsureGameHUDReference();
            gameHUD?.Show();

            SetMapInteractionEnabled(true);
        }

        private void InitializeInput()
        {
            if (inputActions == null)
            {
                Debug.LogWarning("[UIManager] InputActionAsset not assigned. Looking for default...");
                inputActions = Resources.Load<InputActionAsset>("InputSystem_Actions");
            }

            if (inputActions != null)
            {
                playerActionMap = inputActions.FindActionMap("Player");
                uiActionMap = inputActions.FindActionMap("UI");

                // Listen for pause input (ESC key)
                pauseAction = inputActions.FindAction("UI/Cancel");
                
                if (pauseAction == null)
                {
                    Debug.LogWarning("[UIManager] Pause action not found in Input System.");
                }

                // Enable player controls by default and keep UI map active so pause works anytime
                playerActionMap?.Enable();
                uiActionMap?.Enable();
                pauseAction?.Enable();
            }
        }

        private void OnPausePerformed(InputAction.CallbackContext context)
        {
            TogglePause();
        }

        /// <summary>
        /// Toggles the pause state of the game.
        /// </summary>
        public void TogglePause()
        {
            if (isPaused)
            {
                Resume();
            }
            else
            {
                Pause();
            }
        }

        /// <summary>
        /// Pauses the game and shows the pause menu.
        /// </summary>
        public void Pause()
        {
            if (isPaused) return;

            isPaused = true;
            Time.timeScale = 0f;

            // Switch input to UI mode
            playerActionMap?.Disable();
            uiActionMap?.Enable();

            // Show pause menu
            if (pauseMenu != null)
            {
                pauseMenu.Show();
            }

            SetMapInteractionEnabled(false);
            Debug.Log("[UIManager] Game paused.");
        }

        /// <summary>
        /// Resumes the game and hides the pause menu.
        /// </summary>
        public void Resume()
        {
            if (!isPaused) return;

            isPaused = false;
            Time.timeScale = 1f;

            // Switch input back to player mode
            playerActionMap?.Enable();
            uiActionMap?.Enable();

            // Hide pause menu
            if (pauseMenu != null)
            {
                pauseMenu.Hide();
            }

            SetMapInteractionEnabled(true);
            Debug.Log("[UIManager] Game resumed.");
        }

        /// <summary>
        /// Quits the game application.
        /// </summary>
        public void QuitGame()
        {
            Debug.Log("[UIManager] Quitting game...");

#if UNITY_EDITOR
            UnityEditor.EditorApplication.isPlaying = false;
#else
            Application.Quit();
#endif
        }

        /// <summary>
        /// Returns to the start menu scene.
        /// </summary>
        public void ReturnToMenu()
        {
            Debug.Log("[UIManager] Returning to start menu...");
            Time.timeScale = 1f;
            LoadScene(startSceneName);
        }

        /// <summary>
        /// Restarts the gameplay scene.
        /// </summary>
        public void RestartGame()
        {
            Debug.Log("[UIManager] Restarting gameplay scene...");
            Time.timeScale = 1f;
            LoadScene(gameplaySceneName);
        }

        /// <summary>
        /// Loads the end/game over scene.
        /// </summary>
        public void ShowEndScreen()
        {
            Debug.Log("[UIManager] Loading end scene...");
            Time.timeScale = 1f;
            LoadScene(endSceneName);
        }

        /// <summary>
        /// Shows the game HUD.
        /// </summary>
        public void ShowHUD()
        {
            EnsureGameHUDReference();
            gameHUD?.Show();
        }

        /// <summary>
        /// Hides the game HUD.
        /// </summary>
        public void HideHUD()
        {
            EnsureGameHUDReference();
            gameHUD?.Hide();
        }

        /// <summary>
        /// Opens the upgrade panel via the HUD.
        /// </summary>
        public void OpenUpgradePanel()
        {
            EnsureGameHUDReference();
            gameHUD?.OpenUpgradePanel();
        }

        private void SetupCursor()
        {
            if (cursorTexture == null)
            {
                cursorTexture = Resources.Load<Texture2D>("UI/Cursors/ModernCursor");
            }

            if (cursorTexture == null)
            {
                Debug.LogWarning("[UIManager] Custom cursor texture not found.");
                return;
            }

            ApplyCursorTexture(cursorTexture);
        }

        private void ApplyCursorTexture(Texture2D sourceTexture)
        {
            if (runtimeCursorTexture != null)
            {
                Destroy(runtimeCursorTexture);
                runtimeCursorTexture = null;
            }

            var textureToUse = sourceTexture;
            float scale = Mathf.Clamp(cursorScale, 0.01f, 1f);
            if (!Mathf.Approximately(scale, 1f))
            {
                textureToUse = ScaleCursorTexture(sourceTexture, scale);
                runtimeCursorTexture = textureToUse;
            }

            var scaledHotspot = cursorHotspot * scale;
            Cursor.SetCursor(textureToUse, scaledHotspot, CursorMode.Auto);
        }

        private Texture2D ScaleCursorTexture(Texture2D source, float scale)
        {
            int width = Mathf.Max(1, Mathf.RoundToInt(source.width * scale));
            int height = Mathf.Max(1, Mathf.RoundToInt(source.height * scale));

            var rt = RenderTexture.GetTemporary(width, height, 0, RenderTextureFormat.ARGB32, RenderTextureReadWrite.Linear);
            var previous = RenderTexture.active;
            Texture2D scaled = null;

            try
            {
                Graphics.Blit(source, rt);
                RenderTexture.active = rt;

                scaled = new Texture2D(width, height, TextureFormat.RGBA32, false);
                scaled.ReadPixels(new Rect(0, 0, width, height), 0, 0);
                scaled.Apply();
            }
            finally
            {
                // Ensure the temporary RT is no longer active before releasing it.
                if (RenderTexture.active == rt)
                {
                    RenderTexture.active = previous;
                }

                if (RenderTexture.active == rt)
                {
                    RenderTexture.active = null;
                }

                RenderTexture.ReleaseTemporary(rt);
            }

            scaled.name = $"{source.name}_Scaled_{Mathf.RoundToInt(scale * 100f)}";
            return scaled;
        }

        private void LoadScene(string sceneName)
        {
            if (string.IsNullOrEmpty(sceneName))
            {
                Debug.LogWarning("[UIManager] Scene name not configured.");
                return;
            }

            SceneManager.LoadScene(sceneName);
        }

        private void SetMapInteractionEnabled(bool enabled)
        {
            var controllers = GetMapControllers();
            if (controllers == null)
            {
                return;
            }

            foreach (var controller in controllers)
            {
                if (controller != null)
                {
                    controller.SetInteractionEnabled(enabled);
                }
            }
        }

        private RegionMapController[] GetMapControllers()
        {
            if (managedMapControllers != null && managedMapControllers.Length > 0)
            {
                return managedMapControllers;
            }

            if (cachedMapControllers == null || cachedMapControllers.Length == 0)
            {
                cachedMapControllers = FindObjectsByType<RegionMapController>(FindObjectsInactive.Exclude, FindObjectsSortMode.None);
            }

            return cachedMapControllers;
        }

        private void EnsureGameHUDReference()
        {
            if (gameHUD != null)
            {
                return;
            }

#if UNITY_2023_1_OR_NEWER
            gameHUD = UnityEngine.Object.FindFirstObjectByType<GameHUD>();
#else
            gameHUD = UnityEngine.Object.FindObjectOfType<GameHUD>();
#endif
        }
    }
}
