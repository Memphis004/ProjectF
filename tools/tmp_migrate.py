# TMP migration - bulk source rewrite (Text -> TMP_Text family).
# Controlled, idempotent; run from repo root: python tools/tmp_migrate.py
import os

ROOT = os.path.join("UnityProject", "Assets", "Main")

RUNTIME_FILES = [
    "Scripts/Infrastructure/Interaction/InteractionPromptView.cs",
    "Scripts/Infrastructure/UI/LoadingOverlay.cs",
    "Scripts/Infrastructure/UI/ToastView.cs",
    "Scripts/Infrastructure/UI/ToastService.cs",
    "Scripts/Presentation/Common/ConfirmDialog.cs",
    "Scripts/Presentation/Common/InventoryWindow.cs",
    "Scripts/Presentation/Common/HudView.cs",
    "Scripts/Presentation/Common/FishingView.cs",
    "Scripts/Presentation/Village/TaskBoardWindow.cs",
    "Scripts/Presentation/AuntieHouse/CraftWindow.cs",
    "Scripts/Presentation/Shop/ShopWindow.cs",
]

EDITOR_FILES = [
    "Editor/ProjectF/PrefabGenerator.cs",
    "Editor/ProjectF/UiPrefabGenerator.cs",
]

WORLD_FILES = [
    "Scripts/Presentation/Common/NameTagView.cs",
]


def migrate(rel, world_space):
    full = os.path.join(ROOT, rel)
    with open(full, "r", encoding="utf-8-sig", newline="") as fh:
        src = fh.read()
    orig = src
    newline = "\r\n" if "\r\n" in src else "\n"

    # Pull out the UnityEngine.UI using (decided again below).
    for pat in ("using UnityEngine.UI;" + newline, "using UnityEngine.UI;\n"):
        src = src.replace(pat, newline + "__UI_USING_REMOVED__" + newline, 1)

    if world_space:
        # 3D world-space labels: TextMesh -> TextMeshPro
        src = src.replace("TextMesh", "TextMeshPro")
    else:
        pairs = [
            ("private Text ", "private TMP_Text "),
            ("public Text ", "public TMP_Text "),
            ("Text? ", "TMP_Text? "),
            ("(Text)", "(TextMeshProUGUI)"),
            ("Text title = ", "TMP_Text title = "),
            ("Text name = ", "TMP_Text name = "),
            ("Text priceLabel = ", "TMP_Text priceLabel = "),
            ("Text lockLabel = ", "TMP_Text lockLabel = "),
            ("Text countLabel = ", "TMP_Text countLabel = "),
            ("Text message = ", "TMP_Text message = "),
            ("Text count = ", "TMP_Text count = "),
            ("Text text = ", "TMP_Text text = "),
            ("Text taskTitle = ", "TMP_Text taskTitle = "),
            ("Text progress = ", "TMP_Text progress = "),
            ("Text reward = ", "TMP_Text reward = "),
            ("Text craftTitle = ", "TMP_Text craftTitle = "),
            ("Text materials = ", "TMP_Text materials = "),
            ("GetComponent<Text>", "GetComponent<TMP_Text>"),
            ("AddComponent<Text>", "AddComponent<TextMeshProUGUI>"),
            ("GetComponentInChildren<Text>", "GetComponentInChildren<TMP_Text>"),
        ]
        for old, new in pairs:
            src = src.replace(old, new)

    # Does the file still need UnityEngine.UI (Image/Button/...)?
    import re
    needs_ui = bool(re.search(r"\b(Image|Button|Slider|Toggle|Scrollbar)\b", src))

    if needs_ui:
        src = src.replace(newline + "__UI_USING_REMOVED__" + newline,
                          newline + "using UnityEngine.UI;" + newline)
        src = src.replace("\n__UI_USING_REMOVED__\n", "\nusing UnityEngine.UI;\n")
    else:
        src = src.replace(newline + "__UI_USING_REMOVED__" + newline, newline)
        src = src.replace("\n__UI_USING_REMOVED__\n", "\n")

    # TMP using (idempotent), after `using UnityEngine;`
    if "using TMPro;" not in src:
        src = src.replace("using UnityEngine;" + newline,
                          "using UnityEngine;" + newline + "using TMPro;" + newline, 1)
        src = src.replace("using UnityEngine;\n",
                          "using UnityEngine;\nusing TMPro;\n", 1)

    if src != orig:
        with open(full, "w", encoding="utf-8", newline="") as fh:
            fh.write(src)
        print("MIGRATED  " + rel)
    else:
        print("unchanged " + rel)


for f in RUNTIME_FILES:
    migrate(f, world_space=False)
for f in EDITOR_FILES:
    migrate(f, world_space=False)
for f in WORLD_FILES:
    migrate(f, world_space=True)
