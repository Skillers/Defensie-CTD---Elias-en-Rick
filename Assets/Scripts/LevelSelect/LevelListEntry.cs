using System;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

/// <summary>
/// One selectable row in a level list. Uses a Toggle (not a Button) so the
/// selection state persists visually after the click. Wire the prefab with:
///   - Toggle on the root (its Graphic field should be a highlight overlay,
///     e.g. a brighter background image, that is shown while selected)
///   - TMP_Text child for the file-name label
/// The controller passes a shared ToggleGroup on Bind so only one entry can
/// be highlighted at a time.
/// </summary>
public class LevelListEntry : MonoBehaviour
{
    [SerializeField] Toggle toggle;
    [SerializeField] TMP_Text label;

    public void Bind(string displayName, string fileName, Action<string> onSelected, ToggleGroup group)
    {
        // Templates kept in the scene for layout preview are often saved
        // inactive — make sure each clone comes up active regardless.
        gameObject.SetActive(true);

        if (label != null) label.text = displayName;
        if (toggle == null) return;
        toggle.group = group;
        toggle.SetIsOnWithoutNotify(false);
        toggle.onValueChanged.RemoveAllListeners();
        toggle.onValueChanged.AddListener(isOn => { if (isOn) onSelected?.Invoke(fileName); });
    }
}
