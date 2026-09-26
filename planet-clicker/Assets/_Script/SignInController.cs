using System;
using System.Collections.Generic;
using Libplanet;
using Libplanet.Crypto;
using Libplanet.KeyStore;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace LibplanetUnity
{
    /// <summary>
    /// Drives the sign-in screen shown before the <c>Game</c> scene plays.
    ///
    /// Mirrors the pattern of gamejam-laylas-island's IntroCanvas: a dropdown of
    /// remembered addresses (backed by a Web3 Secret Storage keystore), a masked
    /// "Secret" field, and a Sign-in button that is only enabled once the secret
    /// validates against the selected entry (or a new key was generated).
    ///
    /// Three modes:
    /// - Keystore entry selected: the Secret field takes the keystore's passphrase.
    ///   Signing in decrypts the stored key ("unprotect").
    /// - "Enter private key" selected: the Secret field takes a raw hex private key.
    /// - "Create New One" selected: a fresh key is generated; whatever is typed into
    ///   the Secret field becomes its passphrase when it is saved to the keystore.
    /// </summary>
    public class SignInController : MonoBehaviour
    {
        [SerializeField] private TMP_Dropdown addressDropdown;
        [SerializeField] private TMP_InputField secretInputField;
        [SerializeField] private Button signInButton;
        [SerializeField] private TextMeshProUGUI buttonText;
        [SerializeField] private TextMeshProUGUI errorText;

        private readonly List<Web3KeyStoreEntry> _entries = new List<Web3KeyStoreEntry>();
        private bool _createNewOne;
        private bool _signedIn;

        private static System.Action _onSignIn;

        private struct Web3KeyStoreEntry
        {
            public ProtectedPrivateKey Key;
        }

        /// <summary>
        /// True when no sign-in screen exists in the current scene setup, i.e. the game
        /// was launched straight into the Game scene and should use the legacy key
        /// resolution (file / command line) without waiting for user input.
        /// </summary>
        public static bool AlreadySignedIn => FindObjectsOfType<SignInController>().Length == 0;

        public static void RegisterCallback(System.Action callback)
        {
            _onSignIn = callback;
        }

        private void Awake()
        {
            if (addressDropdown == null || secretInputField == null || signInButton == null)
            {
                Debug.LogError("SignInController is missing serialized UI references.");
                return;
            }

            signInButton.interactable = false;
            addressDropdown.onValueChanged.AddListener(_ => OnSelectionChanged());
            secretInputField.onValueChanged.AddListener(_ => ValidateSecret());
            signInButton.onClick.AddListener(OnSignInClicked);
        }

        private void Start()
        {
            // Automated/headless flow: when --private-key is supplied on the command
            // line, skip the interactive sign-in and go straight to the Game scene;
            // Agent.ResolvePrivateKey picks the key up from the command line / clo.json.
            string[] args = Environment.GetCommandLineArgs();
            for (int i = 0; i < args.Length - 1; i++)
            {
                if (args[i] == "--private-key")
                {
                    Debug.Log("--private-key provided; skipping the sign-in screen.");
                    UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
                    return;
                }
            }

            RefreshKeystoreEntries();
        }

        private void OnDestroy()
        {
            addressDropdown?.onValueChanged.RemoveAllListeners();
            secretInputField?.onValueChanged.RemoveAllListeners();
            signInButton?.onClick.RemoveAllListeners();
        }

        private void RefreshKeystoreEntries()
        {
            _entries.Clear();
            try
            {
                foreach (Guid id in Web3KeyStore.DefaultKeyStore.ListIds())
                {
                    try
                    {
                        _entries.Add(new Web3KeyStoreEntry { Key = Web3KeyStore.DefaultKeyStore.Get(id) });
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Skipping unreadable keystore entry {id}: {e.Message}");
                    }
                }
            }
            catch (Exception e)
            {
                Debug.LogWarning($"Failed to read the keystore: {e.Message}");
            }

            var options = new List<string>();
            foreach (var entry in _entries)
            {
                options.Add(ToShortAddress(entry.Key.Address));
            }

            options.Add("Enter private key");
            options.Add("Create New One");

            addressDropdown.ClearOptions();
            addressDropdown.AddOptions(options);
            addressDropdown.value = 0;
            addressDropdown.RefreshShownValue();
            OnSelectionChanged();
        }

        private static string ToShortAddress(Address address)
        {
            string hex = address.ToHex();
            return $"0x{hex.Substring(0, 6)}…{hex.Substring(hex.Length - 4)}";
        }

        private bool IsKeystoreSelected()
        {
            return addressDropdown.value < _entries.Count;
        }

        private bool IsRawKeySelected()
        {
            return !_createNewOne && addressDropdown.value == _entries.Count;
        }

        private bool IsCreateNewSelected()
        {
            return addressDropdown.value == _entries.Count + 1;
        }

        private void OnSelectionChanged()
        {
            if (_signedIn)
            {
                return;
            }

            _createNewOne = IsCreateNewSelected();
            if (errorText != null)
            {
                errorText.gameObject.SetActive(false);
            }

            secretInputField.text = string.Empty;
            secretInputField.contentType = IsRawKeySelected()
                ? TMP_InputField.ContentType.Standard
                : TMP_InputField.ContentType.Password;
            if (secretInputField.placeholder != null)
            {
                secretInputField.placeholder.GetComponent<TMP_Text>().text = IsRawKeySelected()
                    ? "64 hex characters (32 bytes)"
                    : "passphrase";
            }
            UpdateButtonLabel();
            ValidateSecret();
        }

        private void UpdateButtonLabel()
        {
            if (buttonText != null)
            {
                buttonText.text = _createNewOne ? "Sign-up" : "Sign-in";
            }
        }

        private void ValidateSecret()
        {
            if (_signedIn)
            {
                return;
            }

            string secret = secretInputField.text;
            bool valid;
            if (string.IsNullOrEmpty(secret))
            {
                valid = false;
            }
            else if (_createNewOne)
            {
                // Any non-empty passphrase is acceptable for a brand new key.
                valid = true;
            }
            else if (IsRawKeySelected())
            {
                valid = TryParsePrivateKey(secret, out _);
            }
            else
            {
                // The passphrase can only be verified at sign-in time.
                valid = secret.Length > 0;
            }

            signInButton.interactable = valid;
        }

        private void OnSignInClicked()
        {
            if (_signedIn)
            {
                return;
            }

            HideError();
            string secret = secretInputField.text;

            try
            {
                PrivateKey privateKey;
                if (_createNewOne)
                {
                    privateKey = new PrivateKey();
                    try
                    {
                        ProtectedPrivateKey ppk = ProtectedPrivateKey.Protect(privateKey, secret);
                        Web3KeyStore.DefaultKeyStore.Add(ppk);
                        Debug.Log($"Saved a new key to the keystore. (Address: {privateKey.PublicKey.ToAddress()})");
                    }
                    catch (Exception e)
                    {
                        Debug.LogWarning($"Could not save the new key to the keystore: {e.Message}");
                        // Sign in anyway; the key is only lost after this session.
                    }
                }
                else if (IsRawKeySelected())
                {
                    if (!TryParsePrivateKey(secret, out privateKey))
                    {
                        ShowError("Invalid private key. Expected 64 hex characters (32 bytes).");
                        return;
                    }
                }
                else
                {
                    if (!IsKeystoreSelected())
                    {
                        ShowError("Select an address first.");
                        return;
                    }

                    ProtectedPrivateKey ppk = _entries[addressDropdown.value].Key;
                    try
                    {
                        privateKey = ppk.Unprotect(secret);
                    }
                    catch (Exception)
                    {
                        ShowError("Wrong passphrase for the selected address.");
                        return;
                    }
                }

                _signedIn = true;
                CompleteSignIn(privateKey);
            }
            catch (Exception e)
            {
                Debug.LogException(e);
                ShowError($"Sign-in failed: {e.Message}");
            }
        }

        private static bool TryParsePrivateKey(string text, out PrivateKey privateKey)
        {
            privateKey = null;
            if (string.IsNullOrWhiteSpace(text))
            {
                return false;
            }

            string hex = text.Trim();
            if (hex.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
            {
                hex = hex.Substring(2);
            }

            if (hex.Length != 64)
            {
                return false;
            }

            try
            {
                privateKey = new PrivateKey(ByteUtil.ParseHex(hex));
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        private void ShowError(string message)
        {
            Debug.LogWarning($"Sign-in error: {message}");
            if (errorText != null)
            {
                errorText.gameObject.SetActive(true);
                errorText.text = message;
            }
        }

        private void HideError()
        {
            if (errorText != null)
            {
                errorText.gameObject.SetActive(false);
            }
        }

        private void CompleteSignIn(PrivateKey privateKey)
        {
            Agent.SetPendingPrivateKey(privateKey);
            var callback = _onSignIn;
            _onSignIn = null;
            if (callback != null)
            {
                callback.Invoke();
                return;
            }

            // Normal flow: the sign-in scene runs before the Game scene, so there is
            // no Game object yet to receive a callback.  Loading the Game scene makes
            // Game.Awake find Agent.SetPendingPrivateKey's key via SignInController.AlreadySignedIn.
            UnityEngine.SceneManagement.SceneManager.LoadScene("Game");
        }
    }
}
