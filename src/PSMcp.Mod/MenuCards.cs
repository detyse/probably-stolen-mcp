using System;
using System.Linq;
using PSMcp.Contracts;
using UnityEngine;
using UnityEngine.UI;

namespace PSMcp.Mod
{
    // These menu cards reuse the upgrade pointer component, but have their own
    // native selection rules. Only describe cards that are actually rendered.
    internal static class MenuCards
    {
        public static UiControl? Describe(WildNetworkSlot slot)
        {
            if (NewGameSpecializationUI.IsOpen && slot.GetComponentInParent<NewGameSpecializationFrame>() != null)
            {
                var scenarios = StartingScenario.GetVisible();
                if (!int.TryParse(slot.id, out var index) || index < 0 || index >= scenarios.Count) return null;
                var scenario = scenarios[index];
                return new UiControl
                {
                    Kind = "specialization", Label = scenario.GetTitle(),
                    WindowTitle = slot.GetComponentInParent<NewGameSpecializationFrame>().headerTitle.text,
                    IsOn = NewGameSpecializationUI.Selected?.id == scenario.id, Enabled = true
                };
                // Viewing a coming-soon card is legal. The native Continue button
                // remains responsible for CanStart; this action never starts a run.
            }
            if (NewGameDifficultyUI.IsOpen && slot.GetComponentInParent<NewGameDifficultyFrame>() != null)
            {
                var difficulties = Difficulty.GetAll();
                if (!int.TryParse(slot.id, out var index) || index < 0 || index >= difficulties.Count) return null;
                var difficulty = difficulties[index];
                return new UiControl
                {
                    Kind = "difficulty", Label = difficulty.GetTitle(),
                    WindowTitle = slot.GetComponentInParent<NewGameDifficultyFrame>().headerTitle.text,
                    IsOn = NewGameDifficultyUI.Selected?.id == difficulty.id, Enabled = true
                };
            }
            var perksFrame = slot.GetComponentInParent<NewGamePerksFrame>();
            if (NewGamePerksUI.IsOpen && perksFrame != null)
            {
                var perk = StartingPerkList.Perks?.FirstOrDefault(p => p.id == slot.id);
                if (perk == null) return null;
                // The menu dims first-run and already-selected entries, and renders
                // a lock for incompatible perks. Selected-row removal is undimmed.
                var group = slot.GetComponent<CanvasGroup>();
                var lockIcon = slot.transform.Find("lock");
                var enabled = (group == null || group.alpha >= 0.99f)
                    && (lockIcon == null || !lockIcon.gameObject.activeInHierarchy);
                return new UiControl
                {
                    Kind = "starting_perk", Label = perk.GetLocalizedDisplayName(),
                    WindowTitle = perksFrame.headerTitle.text,
                    IsOn = NewGameData.Instance != null && NewGameData.Instance.startingPerks.Any(p => p.id == perk.id),
                    Enabled = enabled, DisabledReason = enabled ? null : "disabled_by_game"
                };
            }
            return null;
        }

        public static void DescribeCheckbox(Button button, UiControl control)
        {
            var frame = button.GetComponentInParent<NewGameDifficultyFrame>();
            if (!NewGameDifficultyUI.IsOpen || frame == null) return;
            var checkbox = frame.skipIntro.button == button ? frame.skipIntro : frame.endless.button == button ? frame.endless : null;
            if (checkbox == null) return;
            control.Kind = "checkbox";
            control.Label = checkbox.label.text;
            control.IsOn = checkbox.mark.gameObject.activeInHierarchy;
        }
    }
}
