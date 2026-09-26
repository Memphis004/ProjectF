using System.Collections.Generic;
using System.IO;
using System.Linq;
using LibplanetUnity;
using TMPro;
using UnityEditor;
using UnityEditor.SceneManagement;
using UnityEngine;
using UnityEngine.UI;

namespace LibplanetUnity.Editor
{
    /// <summary>
    /// Generates Assets/Scenes/SignIn.unity programmatically: a dark full-screen panel,
    /// a "Planet Clicker" title, an Address dropdown, a masked Secret input field, an
    /// error label, and a Sign-in button, all wired to <see cref="SignInController"/>.
    /// Also registers the scene at the top of Build Settings.  Re-running the menu item
    /// rebuilds the scene from scratch.
    /// </summary>
    public static class SignInSceneBuilder
    {
        private const string ScenePath = "Assets/Scenes/SignIn.unity";

        [MenuItem("Tools/Libplanet/Build Sign-In Scene")]
        public static void BuildSignInScene()
        {
            var scene = EditorSceneManager.NewScene(NewSceneSetup.EmptyScene, NewSceneMode.Single);

            var canvasGo = new GameObject("Canvas", typeof(Canvas), typeof(CanvasScaler), typeof(GraphicRaycaster));
            canvasGo.GetComponent<Canvas>().renderMode = RenderMode.ScreenSpaceOverlay;
            canvasGo.GetComponent<CanvasScaler>().uiScaleMode = CanvasScaler.ScaleMode.ScaleWithScreenSize;
            canvasGo.GetComponent<CanvasScaler>().referenceResolution = new Vector2(1024f, 768f);

            var cameraGo = new GameObject("Main Camera");
            var cam = cameraGo.AddComponent<Camera>();
            cam.clearFlags = CameraClearFlags.SolidColor;
            cam.backgroundColor = new Color32(30, 32, 48, 255);
            cameraGo.tag = "MainCamera";

            var eventSystem = new GameObject("EventSystem", typeof(UnityEngine.EventSystems.EventSystem));
            eventSystem.AddComponent<UnityEngine.EventSystems.StandaloneInputModule>();

            // Root panel.
            var panel = new GameObject("Panel", typeof(RectTransform), typeof(CanvasRenderer), typeof(Image));
            panel.transform.SetParent(canvasGo.transform, false);
            var panelRect = (RectTransform)panel.transform;
            panelRect.anchorMin = Vector2.zero;
            panelRect.anchorMax = Vector2.one;
            panelRect.offsetMin = Vector2.zero;
            panelRect.offsetMax = Vector2.zero;
            panel.GetComponent<Image>().color = new Color32(30, 32, 48, 255);

            // Title.
            var title = CreateLabel(panel.transform, "Title", new Vector2(0f, -95f), new Vector2(600f, 70f), TextAlignmentOptions.Center);
            title.text = "Planet Clicker";
            title.fontSize = 44;
            title.fontStyle = FontStyles.Bold;
            title.color = new Color32(240, 240, 255, 255);
            title.rectTransform.anchorMin = new Vector2(0.5f, 1f);
            title.rectTransform.anchorMax = new Vector2(0.5f, 1f);
            title.rectTransform.pivot = new Vector2(0.5f, 1f);

            // Form container.
            var form = new GameObject("Form", typeof(RectTransform));
            form.transform.SetParent(panel.transform, false);
            var formRect = (RectTransform)form.transform;
            formRect.anchorMin = new Vector2(0.5f, 0.5f);
            formRect.anchorMax = new Vector2(0.5f, 0.5f);
            formRect.anchoredPosition = new Vector2(0f, -10f);
            formRect.sizeDelta = new Vector2(520f, 300f);

            CreateLabel(form.transform, "Address Label", new Vector2(-260f, 132f), new Vector2(200f, 24f), TextAlignmentOptions.Left).text = "Address";
            var dropdown = CreateDropdown(form.transform, new Vector2(0f, 100f), new Vector2(520f, 44f));

            CreateLabel(form.transform, "Secret Label", new Vector2(-260f, 62f), new Vector2(200f, 24f), TextAlignmentOptions.Left).text = "Secret";
            var secret = CreateInputField(form.transform, new Vector2(0f, 30f), new Vector2(520f, 44f));
            secret.contentType = TMP_InputField.ContentType.Password;

            var button = CreateButton(form.transform, new Vector2(0f, -50f), new Vector2(520f, 52f));
            button.GetComponentInChildren<TextMeshProUGUI>().text = "Sign-in";

            var error = CreateLabel(form.transform, "Error Text", new Vector2(0f, -100f), new Vector2(520f, 30f), TextAlignmentOptions.Center);
            error.text = string.Empty;
            error.color = new Color32(255, 120, 120, 255);
            error.gameObject.SetActive(false);

            // Wire the controller via SerializedObject so the private [SerializeField]
            // fields are set exactly like an inspector assignment.
            var controller = form.gameObject.AddComponent<SignInController>();
            var serialized = new SerializedObject(controller);
            serialized.FindProperty("addressDropdown").objectReferenceValue = dropdown;
            serialized.FindProperty("secretInputField").objectReferenceValue = secret;
            serialized.FindProperty("signInButton").objectReferenceValue = button;
            serialized.FindProperty("buttonText").objectReferenceValue = button.GetComponentInChildren<TextMeshProUGUI>();
            serialized.FindProperty("errorText").objectReferenceValue = error;
            serialized.ApplyModifiedPropertiesWithoutUndo();

            EditorSceneManager.SaveScene(scene, ScenePath);

            // Make SignIn the first scene in build settings (keep Game as the second).
            var scenes = new List<EditorBuildSettingsScene>();
            foreach (EditorBuildSettingsScene existing in EditorBuildSettings.scenes)
            {
                if (!existing.path.Replace('\\', '/').Equals(ScenePath, System.StringComparison.OrdinalIgnoreCase))
                {
                    scenes.Add(existing);
                }
            }

            scenes.Insert(0, new EditorBuildSettingsScene(ScenePath, true));
            EditorBuildSettings.scenes = scenes.ToArray();
            Debug.Log($"Sign-in scene written to {ScenePath} and registered first in Build Settings.");
        }

        private static TextMeshProUGUI CreateLabel(Transform parent, string name, Vector2 position, Vector2 size, TextAlignmentOptions alignment)
        {
            var go = new GameObject(name, typeof(RectTransform), typeof(CanvasRenderer));
            go.transform.SetParent(parent, false);
            var text = go.AddComponent<TextMeshProUGUI>();
            text.fontSize = 20;
            text.alignment = alignment;
            text.color = new Color32(220, 220, 235, 255);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return text;
        }

        private static TMP_InputField CreateInputField(Transform parent, Vector2 position, Vector2 size)
        {
            var resources = new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };

            GameObject go = TMP_DefaultControls.CreateInputField(resources);
            go.name = "Secret Input Field";
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;
            return go.GetComponent<TMP_InputField>();
        }

        private static TMP_Dropdown CreateDropdown(Transform parent, Vector2 position, Vector2 size)
        {
            var resources = new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };

            GameObject go = TMP_DefaultControls.CreateDropdown(resources);
            go.name = "Address Dropdown";
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            // The dropdown template must be inactive by convention.
            var template = go.transform.Find("Template");
            if (template != null)
            {
                template.gameObject.SetActive(false);
            }

            return go.GetComponent<TMP_Dropdown>();
        }

        private static Button CreateButton(Transform parent, Vector2 position, Vector2 size)
        {
            var resources = new TMP_DefaultControls.Resources
            {
                standard = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UISprite.psd"),
                background = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Background.psd"),
                inputField = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/InputFieldBackground.psd"),
                knob = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Knob.psd"),
                checkmark = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/Checkmark.psd"),
                dropdown = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/DropdownArrow.psd"),
                mask = AssetDatabase.GetBuiltinExtraResource<Sprite>("UI/Skin/UIMask.psd"),
            };

            GameObject go = TMP_DefaultControls.CreateButton(resources);
            go.name = "Sign In Button";
            go.transform.SetParent(parent, false);
            var rect = (RectTransform)go.transform;
            rect.anchoredPosition = position;
            rect.sizeDelta = size;

            var label = go.GetComponentInChildren<TextMeshProUGUI>();
            label.text = "Sign-in";
            label.fontSize = 24;

            var buttonImage = go.GetComponent<Image>();
            buttonImage.color = new Color32(88, 101, 164, 255);

            return go.GetComponent<Button>();
        }
    }
}
