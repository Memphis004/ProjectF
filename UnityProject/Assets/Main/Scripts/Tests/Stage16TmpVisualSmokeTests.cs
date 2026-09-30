using System;
using System.Collections;
using System.IO;
using System.Text;
using NUnit.Framework;
using TMPro;
using UnityEngine;
using UnityEngine.SceneManagement;
using UnityEngine.TestTools;

// ReSharper disable CheckNamespace
namespace ProjectF.Tests
{
    /// <summary>
    /// Stage 16 TMP smoke test — proves the Thai font pipeline END TO END in
    /// play mode:
    ///   1. load the bootstrapped game (Persistent → bootstraps Village),
    ///   2. WAIT IN WALL-CLOCK TIME for Village to go live (chain bootstrap
    ///      can take 10s+ offline; frames spin at 800fps meanwhile, so frame
    ///      counts are meaningless — the 1.89s false failure),
    ///   3. assert EVERY TMP text binds Sarabun SDF and every character in
    ///      every string has a glyph (Thai + ASCII),
    ///   4. open the inventory, re-assert, capture PNGs of both views to
    ///      TestResults/ (paths logged for humans).
    ///
    /// Requires no infra: the game degrades to offline visual mode when the
    /// chain/hub are down — the HUD and windows still render.
    /// </summary>
    public class Stage16TmpVisualSmokeTests
    {
        private const string OutputDir = "TestResults";

        [UnityTest]
        public IEnumerator Hud_and_inventory_render_Thai_with_Sarabun()
        {
            if (!Application.isPlaying)
            {
                // Run from the EditMode runner: skip silently (the attribute
                // for this, RequirePlayMode, only exists in Unity 2023+).
                Assert.Ignore("play-mode-only visual smoke test");
            }

            SceneManager.LoadScene("Persistent", LoadSceneMode.Single);
            yield return null; // allow the load to start

            // --- wait (WALL CLOCK) for Village + a rendered Sarabun text ----
            TMP_Text? probe = null;
            float deadline = Time.realtimeSinceStartup + 90f;
            while (Time.realtimeSinceStartup < deadline)
            {
                yield return null;

                if (SceneManager.GetActiveScene().name != "Village")
                {
                    continue; // still bootstrapping (tables/chain/preload…)
                }

                foreach (TMP_Text t in UnityEngine.Object.FindObjectsByType<TMP_Text>(
                    FindObjectsInactive.Exclude, FindObjectsSortMode.None))
                {
                    if (t.isActiveAndEnabled && t.font != null
                        && t.font.name == "Sarabun SDF" && t.textInfo.characterCount > 0)
                    {
                        probe = t;
                        break;
                    }
                }

                if (probe != null)
                {
                    break;
                }
            }

            Assert.That(probe, Is.Not.Null,
                "Village never produced a rendered Sarabun text within 90s — boot failed?");

            // One more second: HUD presenter binds + full layout settles.
            yield return new WaitForSecondsRealtime(1f);

            // --- 1. every text: Sarabun font + full glyph coverage
            AssertAllTextsCovered("HUD");

            yield return Capture("hud");

            // --- 2. open the inventory (the UiInputDriver's exact path)
            yield return OpenInventory();

            AssertAllTextsCovered("inventory");

            yield return Capture("inventory");
        }

        private static void AssertAllTextsCovered(string phase)
        {
            TMP_Text[] texts = UnityEngine.Object.FindObjectsByType<TMP_Text>(
                FindObjectsInactive.Include, FindObjectsSortMode.None);
            Assert.That(texts.Length, Is.GreaterThan(0), $"{phase}: no TMP texts in scene");

            var failures = new StringBuilder();
            foreach (TMP_Text t in texts)
            {
                if (t.font is null || t.font.name != "Sarabun SDF")
                {
                    failures.AppendLine($"{t.name}: font={(t.font != null ? t.font.name : "NULL")}");
                    continue;
                }

                string s = t.text ?? string.Empty;
                foreach (char c in s)
                {
                    if (!char.IsWhiteSpace(c) && !t.font.HasCharacter(c, true))
                    {
                        failures.AppendLine($"{t.name}: missing U+{((int)c):X4} in \"{s}\"");
                    }
                }
            }

            Assert.That(failures.Length, Is.EqualTo(0),
                $"{phase}: font/glyph failures:\n{failures}");
        }

        private static IEnumerator OpenInventory()
        {
            // Same path the UiInputDriver uses (reflection keeps the test
            // decoupled from the driver's MonoBehaviour internals).
            var driverType = Type.GetType(
                "ProjectF.Presentation.Common.UiInputDriver, ProjectF.Unity");
            Assert.That(driverType, Is.Not.Null, "UiInputDriver type not found");

            Component? driver = UnityEngine.Object.FindFirstObjectByType(
                driverType, FindObjectsInactive.Include) as Component;
            Assert.That(driver, Is.Not.Null, "UiInputDriver not in scene");

            var windowsField = driverType.GetField("windows",
                System.Reflection.BindingFlags.NonPublic
                | System.Reflection.BindingFlags.Instance);
            Assert.That(windowsField, Is.Not.Null, "windows field missing");

            object windows = windowsField.GetValue(driver);
            Assert.That(windows, Is.Not.Null, "IWindowService null");

            var windowType = Type.GetType(
                "ProjectF.Presentation.Common.InventoryWindow, ProjectF.Unity");
            var paramType = Type.GetType(
                "ProjectF.Infrastructure.UI.EmptyWindowParam, ProjectF.Unity");
            var resultType = Type.GetType(
                "ProjectF.Infrastructure.UI.NoWindowResult, ProjectF.Unity");
            Assert.That(windowType, Is.Not.Null);
            Assert.That(paramType, Is.Not.Null);
            Assert.That(resultType, Is.Not.Null);

            var opened = windows.GetType().GetMethod("OpenAsync");
            Assert.That(opened, Is.Not.Null);
            Assert.That(opened.IsGenericMethod, Is.True);
            opened = opened.MakeGenericMethod(windowType, paramType, resultType);

            object param = paramType.GetField("Instance").GetValue(null);
            var cancelToken = (System.Threading.CancellationToken)(
                typeof(Component).GetProperty("destroyCancellationToken")?.GetValue(driver)
                ?? System.Threading.CancellationToken.None);
            opened.Invoke(windows, new[] { param, cancelToken });

            // Let the window instantiate + rebuild its grid.
            for (int i = 0; i < 120; i++)
            {
                yield return null;
                bool isOpen = (bool)windows.GetType().GetMethod("IsOpen")
                    .MakeGenericMethod(windowType).Invoke(windows, null);
                if (isOpen)
                {
                    yield return new WaitForSecondsRealtime(0.3f);
                    yield break;
                }
            }

            Assert.Fail("inventory window did not open within 120 frames");
        }

        private static IEnumerator Capture(string name)
        {
            try
            {
                Directory.CreateDirectory(OutputDir);
                string path = Path.GetFullPath($"{OutputDir}/tmp-smoke-{name}.png");
                ScreenCapture.CaptureScreenshot(path);
                Debug.Log($"[tmp-smoke] captured {name} -> {path}");
            }
            catch (Exception e)
            {
                // Screenshot is evidence, not a gate — never fail the test on it.
                Debug.LogWarning($"[tmp-smoke] capture {name} failed: {e.Message}");
            }

            // CaptureScreenshot lands at end of frame — give it a moment.
            yield return new WaitForSecondsRealtime(0.5f);
        }
    }
}
