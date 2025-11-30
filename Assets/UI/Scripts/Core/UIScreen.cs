using UnityEngine;
using UnityEngine.UIElements;

namespace Zarus.UI
{
    /// <summary>
    /// Base class for all UI screens in the game.
    /// Handles showing/hiding and provides common functionality.
    /// </summary>
    public abstract class UIScreen : MonoBehaviour
    {
        [Header("UI Document")]
        [SerializeField]
        protected UIDocument uiDocument;

        [Header("Screen Settings")]
        [SerializeField]
        protected bool hideOnAwake = true;

        protected VisualElement rootElement;
        protected bool isInitialized;
        protected bool isVisible;
        private bool? requestedVisibility;

        public bool IsVisible => isVisible;

        protected virtual void Awake()
        {
            if (uiDocument == null)
            {
                uiDocument = GetComponent<UIDocument>();
            }

            TryInitializeDocument();
        }

        protected virtual void OnEnable()
        {
            if (!isInitialized)
            {
                TryInitializeDocument();
            }
        }

        protected virtual void Start()
        {
            // UIDocument may finish cloning its visual tree a frame after Awake/OnEnable.
            // Running TryInitializeDocument() here ensures derived screens always get a root.
            if (!isInitialized)
            {
                TryInitializeDocument();
            }
        }

        /// <summary>
        /// Override this to set up UI element references and event handlers.
        /// </summary>
        protected virtual void Initialize()
        {
            // Override in derived classes
        }

        /// <summary>
        /// Shows the screen with optional animation.
        /// </summary>
        public virtual void Show()
        {
            requestedVisibility = true;
            ApplyRequestedVisibility();
        }

        /// <summary>
        /// Hides the screen with optional animation.
        /// </summary>
        public virtual void Hide()
        {
            requestedVisibility = false;
            ApplyRequestedVisibility();
        }

        /// <summary>
        /// Called when the screen is shown. Override for custom behavior.
        /// </summary>
        protected virtual void OnShow()
        {
        }

        /// <summary>
        /// Called when the screen is hidden. Override for custom behavior.
        /// </summary>
        protected virtual void OnHide()
        {
        }

        /// <summary>
        /// Finds a UI element by name. Helper method.
        /// </summary>
        protected T Query<T>(string elementName = null) where T : VisualElement
        {
            return rootElement?.Q<T>(elementName);
        }

        /// <summary>
        /// Registers a button click handler.
        /// </summary>
        protected void RegisterButtonCallback(string buttonName, System.Action callback)
        {
            var button = Query<Button>(buttonName);
            if (button != null)
            {
                button.clicked += callback;
            }
            else
            {
                Debug.LogWarning($"[{GetType().Name}] Button '{buttonName}' not found.");
            }
        }

        private void TryInitializeDocument()
        {
            if (uiDocument == null || uiDocument.rootVisualElement == null || isInitialized)
            {
                return;
            }

            rootElement = uiDocument.rootVisualElement;
            rootElement.style.flexGrow = 1f;
            rootElement.style.flexShrink = 0f;
            rootElement.style.width = new Length(100, LengthUnit.Percent);
            rootElement.style.height = new Length(100, LengthUnit.Percent);
            rootElement.style.minHeight = new Length(100, LengthUnit.Percent);
            rootElement.StretchToParentSize();
            Initialize();
            isInitialized = true;

            if (!requestedVisibility.HasValue)
            {
                requestedVisibility = !hideOnAwake;
            }

            ApplyRequestedVisibility();
        }

        private void ApplyRequestedVisibility()
        {
            if (!requestedVisibility.HasValue || rootElement == null)
            {
                return;
            }

            bool targetVisible = requestedVisibility.Value;
            rootElement.style.display = targetVisible ? DisplayStyle.Flex : DisplayStyle.None;

            if (isVisible == targetVisible)
            {
                return;
            }

            isVisible = targetVisible;
            if (targetVisible)
            {
                OnShow();
            }
            else
            {
                OnHide();
            }
        }
    }
}
