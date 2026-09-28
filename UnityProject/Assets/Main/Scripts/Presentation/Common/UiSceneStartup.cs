using ProjectF.Infrastructure;
using ProjectF.Infrastructure.UI;
using UnityEngine;

// ReSharper disable CheckNamespace
namespace ProjectF.Presentation.Common
{
    /// <summary>
    /// Stage 9 scene-side UI glue (called by each scene's startup AFTER the
    /// scope is built): re-parents the scene's HudView instance under the
    /// app-lifetime UIRoot's HUD layer. Scene files cannot cross-reference
    /// the Persistent scene's UIRoot (Stage 8 lesson), so the Hud prefab
    /// instance is saved INSIDE each gameplay scene as a bare GameObject
    /// tree (no canvas) and moved under the live canvas here.
    /// Idempotent: calling twice re-parents twice harmlessly.
    /// </summary>
    public static class UiSceneStartup
    {
        public static void AttachHud(HudView hud)
        {
            if (hud is null)
            {
                return;
            }

            UIRoot? root = Object.FindObjectOfType<UIRoot>(true);
            if (root is null)
            {
                Debug.LogWarning("[ui-scene] no UIRoot alive — HUD stays unparented.");
                return;
            }

            Transform hudLayer = root.GetLayer(UILayer.Hud);
            if (hud.transform.parent != hudLayer)
            {
                // The Hud root STRETCHES over the HUD layer (anchors 0..1);
                // its rows anchor to its TOP-LEFT in the reference 320x180
                // space, so localScale must stay 1 and the canvas scaler
                // handles DPI. Identity TRS is what the stretch needs —
                // the anchors do the work.
                hud.transform.SetParent(hudLayer, false);
                hud.transform.localPosition = Vector3.zero;
                hud.transform.localRotation = Quaternion.identity;
                hud.transform.localScale = Vector3.one;
            }
        }
    }
}
