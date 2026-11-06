using System;
using System.Collections.Generic;
using System.Linq;
using PSMcp.Contracts;
using TMPro;
using UnityEngine;
using UnityEngine.EventSystems;
using UnityEngine.UI;
using Object = UnityEngine.Object;

namespace PSMcp.Mod
{
    // Discover rendered controls, then use their native callbacks. No arbitrary reflection or method names.
    internal static class VisibleUi
    {
        public static bool Visible(Component component)
        {
            if (component == null || !component.gameObject.activeInHierarchy) return false;
            foreach (var canvas in component.GetComponentsInParent<Canvas>())
                if (!canvas.isActiveAndEnabled) return false;
            foreach (var node in component.GetComponentsInParent<TreeNodeRender>())
            {
                if (RenderHandler.current != null && node == RenderHandler.current.tempRoot) return false;
                if (node.window != null && !node.window.IsVisible()) return false;
            }
            foreach (var group in component.GetComponentsInParent<CanvasGroup>())
            {
                if (group.alpha <= 0.01f) return false;
                if (group.ignoreParentGroups) break;
            }
            return true;
        }

        public static bool Reachable(Component component, out Vector2 position)
        {
            position = default;
            if (!Visible(component) || EventSystem.current == null) return false;
            foreach (var group in component.GetComponentsInParent<CanvasGroup>())
            {
                if (!group.interactable || !group.blocksRaycasts) return false;
                if (group.ignoreParentGroups) break;
            }
            var canvas = component.GetComponentInParent<Canvas>();
            var camera = canvas == null ? Camera.main : canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var points = new List<Vector2>();
            if (component.transform is RectTransform rect)
            {
                var rects = rect.rect.width > 0 && rect.rect.height > 0 ? new[] { rect }
                    : component.GetComponentsInChildren<Graphic>().Where(g => g.isActiveAndEnabled && g.raycastTarget && Visible(g))
                        .Select(g => g.rectTransform).Where(r => r.rect.width > 0 && r.rect.height > 0).Take(128).ToArray();
                // PixelElement roots have a zero-sized transform. The native hit areas
                // live on their rendered children, including the inventory cells.
                foreach (var area in rects)
                {
                    var corners = new Vector3[4];
                    area.GetWorldCorners(corners);
                    var center = (corners[0] + corners[2]) * 0.5f;
                    points.Add(RectTransformUtility.WorldToScreenPoint(camera, center));
                    foreach (var corner in corners) points.Add(RectTransformUtility.WorldToScreenPoint(camera, Vector3.Lerp(center, corner, 0.8f)));
                }
            }
            else
            {
                if (camera == null) return false;
                var collider2D = component.GetComponentInChildren<Collider2D>();
                var collider = component.GetComponentInChildren<Collider>();
                var renderer = component.GetComponentInChildren<Renderer>();
                var point = camera.WorldToScreenPoint(collider2D != null ? collider2D.bounds.center : collider != null ? collider.bounds.center
                    : renderer != null ? renderer.bounds.center : component.transform.position);
                if (point.z <= 0) return false;
                points.Add(point);
            }
            foreach (var point in points)
            {
                if (point.x < 0 || point.y < 0 || point.x >= Screen.width || point.y >= Screen.height) continue;
                var hits = new List<RaycastResult>();
                EventSystem.current.RaycastAll(new PointerEventData(EventSystem.current) { position = point }, hits);
                if (hits.Count == 0) continue;
                var hit = hits[0].gameObject.transform;
                if (hit == component.transform || hit.IsChildOf(component.transform)) { position = point; return true; }
            }
            return false;
        }

        internal static string Label(Component component) => string.Join(" ", component.GetComponentsInChildren<TMP_Text>()
            .Where(Visible).Select(t => t.text).Concat(component.GetComponentsInChildren<Text>().Where(Visible).Select(t => t.text))
            .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(8));

        internal static string WindowTitle(Component component)
        {
            foreach (var node in component.GetComponentsInParent<TreeNodeRender>())
                if (node.window != null) return node.window.titleString ?? "";
            return "";
        }

        private static ScreenBounds? Bounds(Component component)
        {
            if (!(component.transform is RectTransform rect) || Screen.width <= 0 || Screen.height <= 0) return null;
            var canvas = component.GetComponentInParent<Canvas>();
            var camera = canvas == null ? Camera.main : canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
            var corners = new Vector3[4];
            rect.GetWorldCorners(corners);
            var points = corners.Select(p => RectTransformUtility.WorldToScreenPoint(camera, p)).ToArray();
            var left = points.Min(p => p.x); var right = points.Max(p => p.x);
            var bottom = points.Min(p => p.y); var top = points.Max(p => p.y);
            return new ScreenBounds { X = Math.Round(left / Screen.width, 4), Y = Math.Round(1 - top / Screen.height, 4),
                Width = Math.Round((right - left) / Screen.width, 4), Height = Math.Round((top - bottom) / Screen.height, 4) };
        }

        private static string NearbyText(Component component, string label)
        {
            // Read the nearest small rendered group, e.g. a save row with date/day/cash.
            // Do not infer identity from unstable hierarchy indices or inspect save files.
            var parent = component.transform.parent;
            for (var depth = 0; parent != null && depth < 3; depth++, parent = parent.parent)
            {
                var text = Label(parent);
                if (!string.IsNullOrWhiteSpace(text) && text != label) return text.Length > 1024 ? text.Substring(0, 1024) : text;
            }
            return "";
        }

        public static void Capture(GameSnapshot state, ActionRegistry registry, bool worldAllowed,
            bool uiAllowed = true, IReadOnlyCollection<GameObject>? modalRoots = null)
        {
            // Known modal panels restrict the scope even if their backdrop has holes.
            var roots = modalRoots?.Where(root => !modalRoots.Any(other => other != root && other.transform.IsChildOf(root.transform))).ToArray()
                ?? Array.Empty<GameObject>();
            bool InScope(Component component) => uiAllowed && (roots.Length == 0 || roots.Any(root =>
                component.transform == root.transform || component.transform.IsChildOf(root.transform)));
            var controls = new List<UiControl>();
            // Transit cards implement their own pointer handler, not Selectable.
            // Select the native card; the game's separate departure button still
            // performs travel and all destination availability checks.
            foreach (var card in Object.FindObjectsOfType<TransitCardView>().OrderBy(c => c.GetInstanceID()))
            {
                if (!TransitUI.IsOpen || !Visible(card) || card.location == null) continue;
                var enabled = InScope(card) && card.available && card.location.IsAvailable();
                var action = enabled ? registry.Add("afterhours_select_destination", card, "Select destination: " + card.location.GetTitle(),
                    _ => () => TransitUI.Select(card.location)) : null;
                controls.Add(new UiControl { EntityId = registry.Id(card), Kind = "destination", Label = card.location.GetTitle(),
                    NearbyText = Label(card), Bounds = Bounds(card), Enabled = enabled,
                    DisabledReason = enabled ? null : "disabled_by_game_or_modal",
                    Actions = action == null ? Array.Empty<string>() : new[] { action } });
            }
            foreach (var slot in Object.FindObjectsOfType<WildNetworkSlot>().OrderBy(s => s.GetInstanceID()))
            {
                if (!Visible(slot) || slot.onClick == null) continue;
                var control = MenuCards.Describe(slot);
                var menuCard = control != null;
                if (control == null)
                {
                    if (!WildNetworkUI.IsOpen || !PlayerStore.HasInstance) continue;
                    var label = PlayerStore.Instance.networkUpgrade.TryGetValue(slot.id, out var upgrade)
                        ? upgrade.GetLocalizedTitle() : Label(slot);
                    control = new UiControl { Kind = "upgrade", Label = label, Enabled = true };
                }
                var nativeEnabled = control.Enabled;
                var enabled = nativeEnabled && InScope(slot) && Reachable(slot, out _);
                var kind = menuCard ? "ui_select_" + control.Kind : "ui_select_upgrade";
                var description = control.Kind == "starting_perk" ? "Toggle starting perk: " + control.Label
                    : "View " + control.Kind + ": " + control.Label;
                var action = enabled ? registry.Add(kind, slot, description,
                    _ => () =>
                    {
                        var pointer = Pointer(slot);
                        // Perk removal distinguishes the hovered selected-row copy
                        // from the duplicate entry in the available grid.
                        if (menuCard) slot.OnPointerEnter(pointer);
                        try { slot.OnPointerClick(pointer); }
                        finally { if (menuCard && slot != null) slot.OnPointerExit(pointer); }
                    }, allowWithoutGame: !state.GameLoaded) : null;
                control.EntityId = registry.Id(slot);
                control.NearbyText = Label(slot);
                control.Bounds = Bounds(slot);
                control.Enabled = enabled;
                control.DisabledReason = enabled ? null : nativeEnabled ? "occluded_or_outside_view" : control.DisabledReason;
                control.Actions = action == null ? Array.Empty<string>() : new[] { action };
                controls.Add(control);
            }

            foreach (var selectable in Object.FindObjectsOfType<Selectable>().OrderBy(s => s.GetInstanceID()))
            {
                if (!Visible(selectable)) continue;
                var reachable = InScope(selectable) && Reachable(selectable, out _);
                var nativeAllowed = NativeControlAllowed(selectable);
                var control = new UiControl { EntityId = registry.Id(selectable), Label = Label(selectable),
                    WindowTitle = WindowTitle(selectable), Bounds = Bounds(selectable), Enabled = selectable.IsInteractable() && nativeAllowed && reachable };
                control.NearbyText = NearbyText(selectable, control.Label);
                if (string.IsNullOrWhiteSpace(control.Label)) control.Label = selectable.name;
                control.DisabledReason = !selectable.IsInteractable() || !nativeAllowed ? "disabled_by_game" : !reachable ? "occluded_or_outside_view" : null;
                var actions = new List<string>();
                void Add(string kind, Func<ActionArguments, Action> prepare, params ActionParameter[] parameters)
                {
                    if (control.Enabled) actions.Add(registry.Add(kind, selectable, control.Label, prepare, parameters, !state.GameLoaded));
                }
                if (selectable is Button button)
                {
                    control.Kind = "button"; MenuCards.DescribeCheckbox(button, control);
                    Add("ui_click", _ => () => button.OnPointerClick(Pointer(button)));
                }
                else if (selectable is Toggle toggle)
                {
                    control.Kind = "toggle"; control.IsOn = toggle.isOn;
                    Add("ui_toggle", a => () => toggle.isOn = a.Toggle!.Value,
                        new ActionParameter { Name = "toggle", Type = "boolean", Required = true });
                }
                else if (selectable is Slider slider)
                {
                    control.Kind = "slider"; control.Value = slider.value;
                    Add("ui_set_value", a => () => slider.value = (float)a.Value!.Value,
                        ActionRegistry.Number("value", slider.minValue, slider.maxValue, slider.wholeNumbers ? 1 : (double?)null));
                }
                else if (selectable is TMP_InputField input)
                {
                    control.Kind = "text_input";
                    if (input.contentType == TMP_InputField.ContentType.Password || input.contentType == TMP_InputField.ContentType.Pin)
                    { control.Enabled = false; control.DisabledReason = "private_input"; }
                    else
                    {
                        control.Text = input.text;
                        if (input.readOnly) { control.Enabled = false; control.DisabledReason = "read_only"; }
                        Add("ui_enter_text", a => () => EnterText(input, a.Text!),
                            new ActionParameter { Name = "text", Required = true, Max = input.characterLimit > 0 ? Math.Min(input.characterLimit, 4096) : 4096 });
                    }
                }
                else if (selectable is InputField legacyInput)
                {
                    control.Kind = "text_input";
                    if (legacyInput.contentType == InputField.ContentType.Password || legacyInput.contentType == InputField.ContentType.Pin)
                    { control.Enabled = false; control.DisabledReason = "private_input"; }
                    else
                    {
                        control.Text = legacyInput.text;
                        if (legacyInput.readOnly) { control.Enabled = false; control.DisabledReason = "read_only"; }
                        Add("ui_enter_text", a => () => { legacyInput.text = a.Text!; legacyInput.onEndEdit.Invoke(legacyInput.text); },
                            new ActionParameter { Name = "text", Required = true, Max = legacyInput.characterLimit > 0 ? Math.Min(legacyInput.characterLimit, 4096) : 4096 });
                    }
                }
                else if (selectable is TMP_Dropdown dropdown)
                {
                    control.Kind = "dropdown"; control.Value = dropdown.value; control.Options = dropdown.options.Select(o => o.text).ToArray();
                    if (dropdown.options.Count > 0) Add("ui_select_option", a => () => dropdown.value = a.Index!.Value,
                        ActionRegistry.Integer("index", 0, dropdown.options.Count - 1));
                }
                else if (selectable is Dropdown legacyDropdown)
                {
                    control.Kind = "dropdown"; control.Value = legacyDropdown.value; control.Options = legacyDropdown.options.Select(o => o.text).ToArray();
                    if (legacyDropdown.options.Count > 0) Add("ui_select_option", a => () => legacyDropdown.value = a.Index!.Value,
                        ActionRegistry.Integer("index", 0, legacyDropdown.options.Count - 1));
                }
                else if (selectable is Scrollbar scrollbar)
                {
                    control.Kind = "scrollbar"; control.Value = scrollbar.value;
                    Add("ui_scroll", a => () => scrollbar.value = (float)a.Value!.Value, ActionRegistry.Number("value", 0, 1));
                }
                else continue;
                control.Actions = actions.ToArray();
                controls.Add(control);
            }

            var seen = new HashSet<object>();
            foreach (var node in Object.FindObjectsOfType<TreeNodeRender>().OrderBy(n => n.GetInstanceID()))
            {
                var element = node.element;
                if (element == null || !seen.Add(element) || !Visible(node)) continue;
                var control = new UiControl { EntityId = registry.Id(element), WindowTitle = WindowTitle(node), Label = Label(node), Bounds = Bounds(node) };
                var reachable = InScope(node) && Reachable(node, out _);
                var actions = new List<string>();
                void Add(string kind, Func<ActionArguments, Action> prepare, params ActionParameter[] parameters)
                {
                    if (control.Enabled) actions.Add(registry.Add(kind, element, control.Label, prepare, parameters, !state.GameLoaded));
                }
                if (element is MachineryButton button)
                {
                    if (button.isHidden) continue;
                    control.Kind = "machine_button"; control.Label = button.text ?? button.description ?? control.Label;
                    control.Enabled = reachable && !button.isDisabled;
                    Add(button.requiresHold ? "ui_hold" : "ui_click", _ => () => Select(button));
                }
                else if (element is MachineryRadioButton radio)
                {
                    if (radio.isHidden) continue;
                    control.Kind = "machine_toggle"; control.Label = radio.text ?? radio.description ?? control.Label;
                    control.IsOn = radio.isOn; control.Enabled = reachable && !radio.isDisabled;
                    // Select preserves the radio group's native constraints.
                    Add(radio.requiresHold ? "ui_hold" : "ui_click", _ => () => Select(radio));
                }
                else if (element is MachinerySlider slider)
                {
                    control.Kind = "machine_slider"; control.Label = slider.description ?? control.Label;
                    control.Value = slider.value; control.Enabled = reachable && !slider.isDisabled;
                    Add("ui_set_value", a => () => { slider.SetValue((float)a.Value!.Value, true); slider.onReleased?.Invoke(slider.value); },
                        ActionRegistry.Number("value", slider.min, slider.max, slider.step > 0 ? slider.step : (double?)null));
                }
                else if (element is MachineryRangeSlider range)
                {
                    control.Kind = "machine_range"; control.Label = range.description ?? control.Label;
                    control.Value = range.low; control.UpperValue = range.high; control.Enabled = reachable && !range.isDisabled;
                    Add("ui_set_range", a =>
                    {
                        if (a.Value > a.UpperValue) throw new ActionRejectedException("value must not exceed upperValue.");
                        return () => { range.SetValues((float)a.Value!.Value, (float)a.UpperValue!.Value, true); range.onReleased?.Invoke(range.low, range.high); };
                    }, ActionRegistry.Number("value", range.min, range.max, range.step > 0 ? range.step : (double?)null),
                       ActionRegistry.Number("upperValue", range.min, range.max, range.step > 0 ? range.step : (double?)null));
                }
                else if (element is MachineryTabs tabs)
                {
                    control.Kind = "machine_tabs"; control.Value = tabs.selected; control.Enabled = reachable;
                    control.Options = Enumerable.Range(0, tabs.count).Select(tabs.GetCaption).ToArray();
                    if (tabs.count > 0) Add("ui_select_tab", a => () => tabs.Select(a.Index!.Value, true), ActionRegistry.Integer("index", 0, tabs.count - 1));
                    if (tabs.onRenameRequest != null && tabs.selected >= 0)
                        Add("ui_rename_tab", _ => () => tabs.onRenameRequest(tabs.selected));
                }
                else if (element is PushButtonElement push)
                {
                    control.Kind = "push_button"; control.Label = push.text ?? control.Label;
                    control.Enabled = reachable && !push.IsLocked(); control.IsOn = push.IsDown();
                    Add("ui_click", _ => () =>
                    {
                        var pointer = Pointer(node);
                        var hits = new List<RaycastResult>(); EventSystem.current.RaycastAll(pointer, hits);
                        if (hits.Count == 0) throw new InvalidOperationException("Button is no longer reachable.");
                        var receiver = ExecuteEvents.ExecuteHierarchy(hits[0].gameObject, pointer, ExecuteEvents.pointerDownHandler);
                        if (receiver != null) ExecuteEvents.Execute(receiver, pointer, ExecuteEvents.pointerUpHandler);
                    });
                }
                else if (element is SelectableElement legacy && (legacy is ButtonElement || legacy is AdvButtonElement || legacy is SelectableButtonElement))
                {
                    if (legacy.isHidden) continue;
                    control.Kind = "pixel_button"; control.Enabled = reachable && !legacy.isDisabled;
                    if (legacy is ButtonElement buttonElement) control.Label = buttonElement.text ?? buttonElement.description ?? control.Label;
                    if (legacy is AdvButtonElement advanced) control.Label = advanced.text ?? advanced.description ?? control.Label;
                    Add(legacy.requiresHold ? "ui_hold" : "ui_click", _ => () => Select(legacy));
                }
                else continue;
                if (!control.Enabled && control.DisabledReason == null) control.DisabledReason = reachable ? "disabled_by_game" : "occluded_or_outside_view";
                control.Actions = actions.ToArray(); controls.Add(control);
            }

            foreach (var scroll in Object.FindObjectsOfType<ScrollRect>().OrderBy(s => s.GetInstanceID()))
            {
                if (!Visible(scroll)) continue;
                var control = new UiControl { EntityId = registry.Id(scroll), Kind = "scroll_view", Label = "Scroll visible panel",
                    WindowTitle = WindowTitle(scroll), Enabled = scroll.isActiveAndEnabled && InScope(scroll) && Reachable(scroll, out _) };
                var actions = new List<string>();
                if (control.Enabled && scroll.vertical)
                    actions.Add(registry.Add("ui_scroll_vertical", scroll, "Scroll vertically: 0 = bottom, 1 = top.",
                        a => () => scroll.verticalNormalizedPosition = (float)a.Value!.Value, new[] { ActionRegistry.Number("value", 0, 1) }, !state.GameLoaded));
                if (control.Enabled && scroll.horizontal)
                    actions.Add(registry.Add("ui_scroll_horizontal", scroll, "Scroll horizontally: 0 = left, 1 = right.",
                        a => () => scroll.horizontalNormalizedPosition = (float)a.Value!.Value, new[] { ActionRegistry.Number("value", 0, 1) }, !state.GameLoaded));
                control.Actions = actions.ToArray(); controls.Add(control);
            }

            foreach (var window in WindowsHandler.current?.visibleWindows?.ToArray() ?? Array.Empty<PixelWindow>())
            {
                if (window == null || !Visible(window.handler) || !window.IsVisible() || !window.canMove) continue;
                var enabled = !window.isLocked && InScope(window.handler) && Reachable(window.handler, out _);
                var close = enabled ? registry.Add("ui_close_window", window, "Close " + window.titleString + " through the native window handler.",
                    _ => () => WindowsHandler.CloseWindow(window), allowWithoutGame: !state.GameLoaded) : null;
                controls.Add(new UiControl { EntityId = registry.Id(window), Kind = "window", Label = window.titleString ?? "",
                    WindowTitle = window.titleString ?? "", Enabled = enabled, DisabledReason = enabled ? null : "locked_or_occluded",
                    Actions = close == null ? Array.Empty<string>() : new[] { close } });
            }

            if (worldAllowed)
                foreach (var target in Object.FindObjectsOfType<Interactable>().OrderBy(t => t.GetInstanceID()))
                {
                    if (target.GetComponent<StoreBedroll>() != null || target.GetComponent<StoreDebugBell>() != null) continue;
                    if (!Reachable(target, out _) || target.isControlable || target.GetComponent<IClickable>() == null) continue;
                    var hold = target.GetComponent<IHoldClickable>();
                    var enabled = target.isAvailable && !Interactable.isPaused && WorldAllowed(target);
                    var requiresHold = hold != null && hold.ShouldRequireHold();
                    var label = WorldLabel(target);
                    var kind = requiresHold ? "world_hold" : target.GetComponent<StoreClientMono>() != null ? "trade_open" : "world_interact";
                    var action = enabled ? registry.Add(kind, target, "Interact with " + label, arguments => () =>
                    {
                        if (!requiresHold) { target.HandleClick(); return; }
                        var pointer = Pointer(target);
                        NativeInput.Hold(hold!.HoldDuration + 0.05f, () => target != null && target.isAvailable && !Interactable.isPaused && Reachable(target, out _),
                            () => { target.OnPointerEnter(pointer); target.OnPointerDown(pointer); },
                            () => target.OnPointerUp(pointer), () => { if (target != null) { target.OnPointerExit(pointer); target.OnPointerUp(pointer); } });
                    }) : null;
                    controls.Add(new UiControl { EntityId = registry.Id(target), Kind = "world_object", Label = label,
                        Enabled = enabled, DisabledReason = enabled ? null : "disabled_by_game", Actions = action == null ? Array.Empty<string>() : new[] { action } });
                }
            state.Controls = controls.OrderBy(c => c.EntityId, StringComparer.Ordinal).ToArray();
            state.ScreenText = Object.FindObjectsOfType<TMP_Text>().Where(Visible).Where(t => t.enabled && t.color.a > 0.01f)
                .OrderBy(t => t.GetInstanceID()).Select(t => t.text)
                .Concat(Object.FindObjectsOfType<Text>().Where(Visible).Where(t => t.enabled && t.color.a > 0.01f).OrderBy(t => t.GetInstanceID()).Select(t => t.text))
                .Where(t => !string.IsNullOrWhiteSpace(t)).Distinct().Take(256).Select(t => t.Length > 4096 ? t.Substring(0, 4096) : t).ToArray();
        }

        private static PointerEventData Pointer(Component component)
        {
            if (!Reachable(component, out var point)) throw new InvalidOperationException("Control is no longer reachable.");
            return new PointerEventData(EventSystem.current) { button = PointerEventData.InputButton.Left, position = point, clickCount = 1 };
        }

        private static void Select(SelectableElement element)
        {
            if (!element.requiresHold) { NativeInput.Select(element); return; }
            NativeInput.Hold(element.holdDuration, () => !element.isDisabled && !element.isHidden && Reachable(element.handler, out _),
                () => element.Press(), () => NativeInput.Select(element), () => element.Defocus());
        }

        private static void EnterText(TMP_InputField input, string text)
        {
            // TMP's text setter skips its normal character validators. Process bounded
            // text-input events so numeric, regex and custom validators still run.
            input.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.A, modifiers = EventModifiers.Control });
            input.ProcessEvent(new Event { type = EventType.KeyDown, keyCode = KeyCode.Backspace });
            foreach (var character in text)
            {
                if (input == null || !input.isActiveAndEnabled || !input.IsInteractable())
                    throw new InvalidOperationException("The input closed while editing; inspect the current state.");
                input.ProcessEvent(new Event { type = EventType.KeyDown, character = character });
            }
            input.onEndEdit.Invoke(input.text);
        }

        private static string WorldLabel(Component target)
        {
            if (target.GetComponent<StoreClientMono>() != null) return "current customer";
            if (target.GetComponent<StoreDoor>() != null) return "shop door";
            if (target.GetComponent<StoreBedroll>() != null) return "bedroll";
            if (target.GetComponent<StorePhoneObject>() != null) return "telephone";
            if (target.GetComponent<StorePC>() != null) return "computer";
            if (target.GetComponent<StoreNotebook>() != null) return "notebook";
            if (target.GetComponent<StoreCalendar>() != null) return "calendar";
            if (target.GetComponent<StorePoster>() != null) return "poster";
            if (target.GetComponent<Shotgun>() != null) return "shotgun";
            if (target.GetComponent<StoreMagnifyingGlass>() != null) return "magnifying glass";
            var tooltip = target.GetComponent<TooltipInterface>();
            if (tooltip != null && tooltip.IsTooltipEnabled()) return tooltip.GetTooltip();
            return "visible scene object";
        }

        private static bool NativeControlAllowed(Selectable control)
        {
            // The native dumping-ground button remains interactable after injury
            // or exhausted attempts; its callback silently returns. Its public
            // gameplay predicate, not Button.interactable alone, is authoritative.
            var map = MapUIManager.Instance;
            if (control is Button && map != null && map.Current != null && map.Current.Id == "dumping_ground"
                && Label(control) == LocHelper.GetLocalizedUI("afterhour_scavenge_action"))
                return ScavHelper.CanScavenge();
            return true;
        }

        private static bool WorldAllowed(Component target)
        {
            var store = PlayerStore.Instance;
            if (target.GetComponent<StoreDoor>() != null)
                return store.storeState == PlayerStore.StoreState.CLOSED && store.isAfterhoursIntroduced && store.visitLeftTonight > 0
                    && !HealthData.IsSeriouslyWounded() && !StartingPerkContent.IsAgoraphobiaResting()
                    && !StoreStation.Instance.storeEventManager.IsEventActive("crackdown");
            var security = target.GetComponent<StoreSecButton>();
            if (security != null) return store.IsClientPresent() && security.IsClientArrestable()
                && !store.isClientBeingArrested && !store.currentClientInstance.GetClientBlueprint().reported
                && !StoreReputation.IsPerkUnlocked("SEC_ALARM_IGNORED");
            if (target.GetComponent<StoreBedroll>() != null) return store.storeState == PlayerStore.StoreState.CLOSED;
            if (target.GetComponent<StoreShutterButton>() != null)
                return store.lockShutterButtonFlag == 0 && !store.isClientBeingArrested && !store.isHandlingShootout
                    && (store.storeState == PlayerStore.StoreState.MORNING && store.CanStartDay()
                        || store.storeState == PlayerStore.StoreState.OPEN && store.CanEndDay());
            if (target.GetComponent<StoreBell>() != null) return store.storeState == PlayerStore.StoreState.OPEN && store.lockNextBellFlag == 0;
            if (target.GetComponent<StoreDismissButton>() != null) return store.isClientArrived && store.currentClientInstance != null
                && store.lockClientClickFlag == 0 && store.IsClientDimissable();
            return true;
        }
    }
}
