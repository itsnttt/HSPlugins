using System.Collections.Generic;
using HarmonyLib;
using KKAPI.Studio.UI.Toolbars;
using Timeline.View;
using UILib.EventHandlers;
using UnityEngine;
using UnityEngine.UI;

namespace Timeline
{
    public partial class Timeline
    {
        internal sealed partial class View
        {
            private bool _docked;
            private float _dockHeight = 0.3f, _dockSideWidth = 0.25f;
            private Vector2 _floatingPosition, _floatingSize;
            private RectTransform _dockSplitter, _dockSideSplitter;
            private DockViewport _dockViewport;
            private bool _dockResizing;
            private float _dockLayoutAt = -1f;
            private Vector2 _dockScreenSize;
            private float _dockScale;
            private const float DockDivider = 6f;

            private void BuildDock()
            {
                _dockViewport = canvas.gameObject.AddComponent<DockViewport>();
                Image divider = Kit.Box("Dock divider", canvas.transform, Pal.C(0x44474C));
                divider.raycastTarget = true;
                _dockSplitter = divider.rectTransform;
                _dockSplitter.anchorMin = _dockSplitter.anchorMax = Vector2.zero;
                _dockSplitter.pivot = Vector2.zero;
                DragHandler drag = divider.gameObject.AddComponent<DragHandler>();
                drag.onBeginDrag = e => _dockResizing = true;
                drag.onEndDrag = e => EndDockResize();
                drag.onDrag = e =>
                {
                    ResizeDock(e.position.y / Mathf.Max(Screen.height, 1));
                };
                Image side = Kit.Box("Viewport divider", canvas.transform, Pal.C(0x44474C));
                side.raycastTarget = true;
                _dockSideSplitter = side.rectTransform;
                _dockSideSplitter.anchorMin = _dockSideSplitter.anchorMax = Vector2.zero;
                _dockSideSplitter.pivot = Vector2.zero;
                DragHandler sideDrag = side.gameObject.AddComponent<DragHandler>();
                sideDrag.onBeginDrag = e => _dockResizing = true;
                sideDrag.onEndDrag = e => EndDockResize();
                sideDrag.onDrag = e =>
                {
                    _dockSideWidth = Mathf.Clamp(1f - e.position.x / Mathf.Max(Screen.width, 1), 0f, 0.45f);
                    TickDock();
                };
                _dockSplitter.gameObject.SetActive(false);
                _dockSideSplitter.gameObject.SetActive(false);
            }

            private void ToggleDock()
            {
                if (_docked == false)
                {
                    _floatingPosition = _win.anchoredPosition;
                    _floatingSize = new Vector2(winW, winH);
                }
                _docked = !_docked;
                _dockResizing = false;
                if (_docked == false)
                {
                    winW = _floatingSize.x;
                    winH = _floatingSize.y;
                    _win.anchoredPosition = _floatingPosition;
                    ApplyWindowSize();
                }
                TickDock();
                Touch();
                if (_docked)
                    Toast("Drag the top divider to resize Timeline, or the vertical divider to resize the viewport.");
            }

            private void ResizeDock(float height)
            {
                _dockHeight = Mathf.Clamp(height, 0.15f, 0.65f);
                TickDock();
            }

            private void EndDockResize()
            {
                _dockResizing = false;
                TickDock();
            }

            private void TickDock()
            {
                bool active = _docked && visible;
                _dockSplitter.gameObject.SetActive(active);
                _dockSideSplitter.gameObject.SetActive(active);
                _shadow.gameObject.SetActive(!active);
                if (active == false)
                {
                    _dockResizing = false;
                    _dockViewport.Restore();
                    return;
                }

                float scale = Mathf.Max(canvas.scaleFactor, 0.01f);
                Vector2 screenSize = new Vector2(Screen.width, Screen.height);
                // Post-processing reallocates its buffers when the viewport changes. Limit
                // intermediate sizes during a drag, then apply the final size on release.
                if (_dockResizing && _dockViewport.active && screenSize == _dockScreenSize &&
                    scale == _dockScale && Time.unscaledTime - _dockLayoutAt < 0.12f)
                {
                    _dockViewport.RaiseToolbar(canvas.sortingOrder + 1, canvas.sortingLayerID);
                    return;
                }
                _dockLayoutAt = Time.unscaledTime;
                _dockScreenSize = screenSize;
                _dockScale = scale;
                float width = Screen.width / scale;
                float height = Mathf.Round(Screen.height * _dockHeight / scale);
                if (winW != width || winH != height)
                {
                    winW = width;
                    winH = height;
                    ApplyWindowSize();
                }
                _win.anchoredPosition = new Vector2(-width / 2f, height);
                LayoutShadow();
                _dockSplitter.anchoredPosition = new Vector2(0f, height);
                _dockSplitter.sizeDelta = new Vector2(width, DockDivider);
                float viewportWidth = width * (1f - _dockSideWidth);
                _dockSideSplitter.anchoredPosition = new Vector2(viewportWidth, height + DockDivider);
                _dockSideSplitter.sizeDelta = new Vector2(DockDivider, Mathf.Max(1f, Screen.height / scale - height - DockDivider));
                _dockViewport.Apply(new Rect(0f, (height + DockDivider) * scale / Screen.height,
                    viewportWidth / width, 1f - (height + DockDivider) * scale / Screen.height));
                if (_dockViewport.active)
                {
                    _dockViewport.RaiseToolbar(canvas.sortingOrder + 1, canvas.sortingLayerID);
                    _dockViewport.PlaceGuideInput((height + DockDivider) * scale + 4f);
                    _dockViewport.PlaceToolbar((height + DockDivider) * scale + 4f);
                }
            }

            internal bool BlockDockCameraInput()
            {
                return _docked && visible && _dockViewport != null && _dockViewport.active &&
                    (_dockResizing || _drag != null || _gdrag != null || _sdrag != null ||
                    _dockViewport.viewport.Contains(new Vector2(Input.mousePosition.x / Mathf.Max(Screen.width, 1),
                        Input.mousePosition.y / Mathf.Max(Screen.height, 1))) == false || PointerOverAny());
            }
        }

        // The canvas owns the camera lease so hiding, rebuilding, or destroying it restores the viewport.
        private sealed class DockViewport : MonoBehaviour
        {
            private sealed class ToolbarLayer
            {
                public Canvas canvas;
                public GraphicRaycaster raycaster;
                public bool addedCanvas, overrideSorting;
                public int order, sortingLayer;
            }

            private readonly Dictionary<GameObject, ToolbarLayer> _toolbarLayers = new Dictionary<GameObject, ToolbarLayer>();
            private sealed class ToolbarPlacement
            {
                public Vector2 position;
                public Vector3 scale, appliedScale;
                public Vector2 appliedPosition, screenLow, screenHigh;
                public bool applied, followsDock;
            }

            private readonly Dictionary<RectTransform, ToolbarPlacement> _toolbarPlacements = new Dictionary<RectTransform, ToolbarPlacement>();
            private float _toolbarCheckedAt = -1f;
            private float _toolbarPlacedAt = -1f, _toolbarBottom = -1f;
            private Vector2 _toolbarScreen;
            private RectTransform _guideInput;
            private RectTransform _guideExtra;
            private Vector2 _guideExtraPosition;
            private Vector2 _guidePosition, _guideAppliedPosition;
            private float _guideBottom = -1f;
            private readonly Vector3[] _guideCorners = new Vector3[4];
            private readonly Dictionary<Camera, Rect> _originalRects = new Dictionary<Camera, Rect>();
            private Camera _mainCamera, _background;
            public Rect viewport;
            public bool active;


            public void PlaceToolbar(float bottom)
            {
                bottom = AboveGuidePanel(bottom, _guideInput);
                bottom = AboveGuidePanel(bottom, _guideExtra);
                Vector2 screen = new Vector2(Screen.width, Screen.height);
                bool changed = false;
                foreach (KeyValuePair<RectTransform, ToolbarPlacement> pair in _toolbarPlacements)
                {
                    if (pair.Key != null && pair.Value.applied &&
                        (pair.Key.anchoredPosition != pair.Value.appliedPosition || pair.Key.localScale != pair.Value.appliedScale))
                        changed = true;
                }
                if (!changed && bottom == _toolbarBottom && screen == _toolbarScreen && Time.unscaledTime - _toolbarPlacedAt < 0.5f)
                    return;
                _toolbarPlacedAt = Time.unscaledTime;
                _toolbarBottom = bottom;
                _toolbarScreen = screen;

                float low = float.MaxValue, high = float.MinValue, left = float.MaxValue;
                foreach (KeyValuePair<RectTransform, ToolbarPlacement> pair in _toolbarPlacements)
                {
                    RectTransform rect = pair.Key;
                    ToolbarPlacement state = pair.Value;
                    if (rect == null)
                        continue;
                    // A KKAPI relayout or a user drag supplies a new saved placement.
                    if (state.applied && rect.anchoredPosition != state.appliedPosition)
                        state.position = rect.anchoredPosition;
                    if (state.applied && rect.localScale != state.appliedScale)
                        state.scale = rect.localScale;
                    rect.anchoredPosition = state.position;
                    rect.localScale = state.scale;
                    Canvas canvas = rect.GetComponentInParent<Canvas>();
                    Camera uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                    rect.GetWorldCorners(_guideCorners);
                    state.screenLow = RectTransformUtility.WorldToScreenPoint(uiCamera, _guideCorners[0]);
                    state.screenHigh = RectTransformUtility.WorldToScreenPoint(uiCamera, _guideCorners[2]);
                    state.followsDock = rect.gameObject.activeInHierarchy &&
                        (state.screenLow.x + state.screenHigh.x) * 0.5f < screen.x * 0.5f;
                    if (state.followsDock)
                    {
                        low = Mathf.Min(low, state.screenLow.y);
                        high = Mathf.Max(high, state.screenHigh.y);
                        left = Mathf.Min(left, state.screenLow.x);
                    }
                }

                float factor = Mathf.Min(1f, Mathf.Max(1f, screen.y - bottom - 4f) / Mathf.Max(1f, high - low));
                float targetLow = low == float.MaxValue ? 0f :
                    Mathf.Clamp(low, bottom, Mathf.Max(bottom, screen.y - 4f - (high - low) * factor));
                foreach (KeyValuePair<RectTransform, ToolbarPlacement> pair in _toolbarPlacements)
                {
                    RectTransform rect = pair.Key;
                    ToolbarPlacement state = pair.Value;
                    if (rect == null)
                        continue;
                    RectTransform parent = rect.parent as RectTransform;
                    if (state.followsDock && parent != null)
                    {
                        rect.localScale = new Vector3(state.scale.x * factor, state.scale.y * factor, state.scale.z);
                        Canvas canvas = rect.GetComponentInParent<Canvas>();
                        Camera uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                        rect.GetWorldCorners(_guideCorners);
                        Vector2 current = RectTransformUtility.WorldToScreenPoint(uiCamera, _guideCorners[0]);
                        Vector2 target = new Vector2(left + (state.screenLow.x - left) * factor,
                            targetLow + (state.screenLow.y - low) * factor);
                        Vector2 from, to;
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, current, uiCamera, out from);
                        RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, target, uiCamera, out to);
                        rect.anchoredPosition += to - from;
                    }
                    state.appliedPosition = rect.anchoredPosition;
                    state.appliedScale = rect.localScale;
                    state.applied = true;
                }
            }

            private float AboveGuidePanel(float bottom, RectTransform panel)
            {
                if (panel == null || panel.gameObject.activeInHierarchy == false)
                    return bottom;
                Canvas canvas = panel.GetComponentInParent<Canvas>();
                if (canvas != null && canvas.isActiveAndEnabled == false)
                    return bottom;
                Camera uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                panel.GetWorldCorners(_guideCorners);
                return Mathf.Max(bottom, RectTransformUtility.WorldToScreenPoint(uiCamera, _guideCorners[1]).y + 6f);
            }

            public void PlaceGuideInput(float bottom)
            {
                var manager = Studio.GuideObjectManager.Instance;
                RectTransform panel = manager == null || manager.guideInput == null ? null :
                    manager.guideInput.transform as RectTransform;
                if (panel != _guideInput)
                {
                    RestoreGuideInput();
                    _guideInput = panel;
                    if (panel != null)
                        _guidePosition = panel.anchoredPosition;
                }
                RectTransform extra = panel == null || panel.parent == null ? null :
                    panel.parent.Find("Additional Operations") as RectTransform;
                if (extra != _guideExtra)
                {
                    if (_guideExtra != null)
                        _guideExtra.anchoredPosition = _guideExtraPosition;
                    _guideExtra = extra;
                    if (extra != null)
                        _guideExtraPosition = extra.anchoredPosition;
                    _guideBottom = -1f;
                }
                if (panel == null || (bottom == _guideBottom && panel.anchoredPosition == _guideAppliedPosition))
                    return;
                RectTransform parent = panel.parent as RectTransform;
                if (parent == null)
                    return;
                Canvas canvas = panel.GetComponentInParent<Canvas>();
                Camera uiCamera = canvas == null || canvas.renderMode == RenderMode.ScreenSpaceOverlay ? null : canvas.worldCamera;
                panel.anchoredPosition = _guidePosition;
                panel.GetWorldCorners(_guideCorners);
                Vector2 screenBottom = RectTransformUtility.WorldToScreenPoint(uiCamera, _guideCorners[0]);
                float top = RectTransformUtility.WorldToScreenPoint(uiCamera, _guideCorners[1]).y;
                float delta = Mathf.Min(Mathf.Max(0f, bottom - screenBottom.y), Mathf.Max(0f, Screen.height - 4f - top));
                Vector2 from, to;
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenBottom, uiCamera, out from);
                RectTransformUtility.ScreenPointToLocalPointInRectangle(parent, screenBottom + Vector2.up * delta, uiCamera, out to);
                panel.anchoredPosition += to - from;
                if (_guideExtra != null)
                    _guideExtra.anchoredPosition = _guideExtraPosition + panel.anchoredPosition - _guidePosition;
                _guideAppliedPosition = panel.anchoredPosition;
                _guideBottom = bottom;
            }

            private void RestoreGuideInput()
            {
                if (_guideExtra != null)
                    _guideExtra.anchoredPosition = _guideExtraPosition;
                _guideExtra = null;
                if (_guideInput != null)
                    _guideInput.anchoredPosition = _guidePosition;
                _guideInput = null;
                _guideBottom = -1f;
            }

            public void RaiseToolbar(int order, int sortingLayer)
            {
                if (Time.unscaledTime - _toolbarCheckedAt < 0.5f)
                    return;
                _toolbarCheckedAt = Time.unscaledTime;
                foreach (ToolbarControlBase button in ToolbarManager.GetAllButtons(true))
                {
                    if (button.ButtonObject == null)
                        continue;
                    GameObject obj = button.ButtonObject.gameObject;
                    RectTransform root = obj.transform as RectTransform;
                    if (root != null && _toolbarPlacements.ContainsKey(root) == false)
                    {
                        _toolbarPlacements.Add(root, new ToolbarPlacement { position = root.anchoredPosition, scale = root.localScale });
                        _toolbarBottom = -1f;
                    }
                    ToolbarLayer layer;
                    if (_toolbarLayers.TryGetValue(obj, out layer) == false)
                    {
                        Canvas canvas = obj.GetComponent<Canvas>();
                        layer = new ToolbarLayer { addedCanvas = canvas == null };
                        layer.canvas = canvas != null ? canvas : obj.AddComponent<Canvas>();
                        layer.overrideSorting = layer.canvas.overrideSorting;
                        layer.order = layer.canvas.sortingOrder;
                        layer.sortingLayer = layer.canvas.sortingLayerID;
                        if (obj.GetComponent<GraphicRaycaster>() == null)
                            layer.raycaster = obj.AddComponent<GraphicRaycaster>();
                        _toolbarLayers.Add(obj, layer);
                    }
                    layer.canvas.overrideSorting = true;
                    layer.canvas.sortingLayerID = sortingLayer;
                    layer.canvas.sortingOrder = order;
                }
            }

            public void Apply(Rect rect)
            {
                Studio.CameraControl control = Studio.Studio.Instance == null ? null : Studio.Studio.Instance.cameraCtrl;
                Camera main = control == null ? null : control.mainCmaera;
                if (main == null)
                {
                    Restore();
                    return;
                }
                if (_mainCamera != main)
                    RestoreCameras();
                _mainCamera = main;
                if (_background == null)
                {
                    var background = new GameObject("Dock workspace background");
                    background.transform.SetParent(transform, false);
                    _background = background.AddComponent<Camera>();
                    _background.clearFlags = CameraClearFlags.SolidColor;
                    _background.renderingPath = RenderingPath.Forward;
                    _background.allowHDR = false;
                    _background.allowMSAA = false;
                    _background.useOcclusionCulling = false;
                    _background.backgroundColor = new Color(0.07f, 0.08f, 0.09f, 1f);
                    _background.cullingMask = 0;
                }
                _background.depth = main.depth - 1f;
                _background.enabled = true;
                viewport = rect;
                active = true;
                ApplyCamera(main);
                if (control.subCamera != null && control.subCamera.targetTexture == null)
                    ApplyCamera(control.subCamera);
            }

            private void ApplyCamera(Camera camera)
            {
                if (_originalRects.ContainsKey(camera) == false)
                    _originalRects.Add(camera, camera.rect);
                if (camera.rect != viewport)
                    camera.rect = viewport;
            }

            public void Restore()
            {
                RestoreGuideInput();
                foreach (KeyValuePair<RectTransform, ToolbarPlacement> pair in _toolbarPlacements)
                {
                    if (pair.Key == null)
                        continue;
                    pair.Key.anchoredPosition = pair.Value.position;
                    pair.Key.localScale = pair.Value.scale;
                }
                _toolbarPlacements.Clear();
                _toolbarBottom = -1f;
                foreach (ToolbarLayer layer in _toolbarLayers.Values)
                {
                    if (layer.raycaster != null)
                        Destroy(layer.raycaster);
                    if (layer.canvas == null)
                        continue;
                    if (layer.addedCanvas)
                        Destroy(layer.canvas);
                    else
                    {
                        layer.canvas.sortingOrder = layer.order;
                        layer.canvas.sortingLayerID = layer.sortingLayer;
                        layer.canvas.overrideSorting = layer.overrideSorting;
                    }
                }
                _toolbarLayers.Clear();
                _toolbarCheckedAt = -1f;
                RestoreCameras();
            }

            private void RestoreCameras()
            {
                foreach (KeyValuePair<Camera, Rect> pair in _originalRects)
                {
                    if (pair.Key != null)
                        pair.Key.rect = pair.Value;
                }
                _originalRects.Clear();
                if (_background != null)
                    _background.enabled = false;
                _mainCamera = null;
                active = false;
            }

            private void OnDisable() { Restore(); }
            private void OnDestroy() { Restore(); }
        }

        private static bool AllowDockCameraInput(Studio.CameraControl control)
        {
            return _self == null || _self._view == null || Studio.Studio.Instance == null ||
                control != Studio.Studio.Instance.cameraCtrl || _self._view.BlockDockCameraInput() == false;
        }

        [HarmonyPatch(typeof(Studio.CameraControl), "InputMouseWheelZoomProc")]
        private static class DockCameraWheelInput
        {
            private static bool Prefix(Studio.CameraControl __instance) { return AllowDockCameraInput(__instance); }
        }

        [HarmonyPatch(typeof(Studio.CameraControl), "InputMouseProc")]
        private static class DockCameraMouseInput
        {
            private static bool Prefix(Studio.CameraControl __instance) { return AllowDockCameraInput(__instance); }
        }

        [HarmonyPatch(typeof(Studio.CameraControl), "InputKeyProc")]
        private static class DockCameraKeyInput
        {
            private static bool Prefix(Studio.CameraControl __instance) { return AllowDockCameraInput(__instance); }
        }
    }
}
