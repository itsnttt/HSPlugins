using Studio;
using UnityEngine;
#if BEPINEX
using HarmonyLib;
#endif

namespace Timeline
{
    /// <summary>
    /// Ctrl+Z over the Timeline window is Timeline's undo. Studio listens for the same keys everywhere,
    /// so without this one press undid a Timeline edit and a Studio one at once. Anywhere else, Studio
    /// keeps its undo as before.
    /// </summary>
    public partial class Timeline
    {
        /// <summary>Whether the pointer is over the window or one of its floating windows.</summary>
        internal bool PointerOverTimeline()
        {
            return _view != null && _view.visible && _view.PointerOverAny();
        }

        internal sealed partial class View
        {
            public bool PointerOverAny()
            {
                if (PointerOverWindow())
                    return true;
                if (_pwin != null && _pwin.gameObject.activeInHierarchy &&
                    RectTransformUtility.RectangleContainsScreenPoint(_pwin, Input.mousePosition, null))
                    return true;
                foreach (FloatWin w in _floats)
                {
                    if (w.win != null && w.win.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(w.win, Input.mousePosition, null))
                        return true;
                }
                return _twin != null && _twin.gameObject.activeInHierarchy && RectTransformUtility.RectangleContainsScreenPoint(_twin, Input.mousePosition, null);
            }
        }

#if BEPINEX
        private bool _hideUIShortcutPatched;

        private void InitHideUIShortcut()
        {
            if (_hideUIShortcutPatched)
                return;
            var type = AccessTools.TypeByName("HideAllUI.HideStudioUI");
            if (type == null)
                return;
            var toggle = AccessTools.DeclaredMethod(type, "ToggleUI");
            if (toggle == null)
                return;
            try
            {
                new Harmony(GUID).Patch(toggle,
                    prefix: new HarmonyMethod(AccessTools.DeclaredMethod(typeof(Timeline), nameof(AllowStudioUIHide))));
                _hideUIShortcutPatched = true;
            }
            catch (System.Exception e)
            {
                Logger.LogWarning("Could not isolate Timeline's Space shortcut from HideAllUI: " + e.Message);
            }
        }

        private static bool AllowStudioUIHide()
        {
            return Input.GetKeyDown(KeyCode.Space) == false || _self == null || _self.PointerOverTimeline() == false;
        }

        [HarmonyPatch(typeof(UndoRedoManager), nameof(UndoRedoManager.Undo))]
        private static class UndoRedoManager_Undo_Patches
        {
            private static bool Prefix()
            {
                return _self == null || _self.PointerOverTimeline() == false;
            }
        }

        [HarmonyPatch(typeof(UndoRedoManager), nameof(UndoRedoManager.Redo))]
        private static class UndoRedoManager_Redo_Patches
        {
            private static bool Prefix()
            {
                return _self == null || _self.PointerOverTimeline() == false;
            }
        }
#endif
    }
}
