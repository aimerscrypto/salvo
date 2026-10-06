using System;
using System.Collections.Generic;
using Unity.Netcode;
using UnityEngine;
using UnityEngine.UIElements;

namespace Salvo
{
    /// <summary>
    /// Owns the single UIDocument: screen visibility, modal dialogs, toasts, the connecting
    /// overlay and UI sounds. Menu pages and the HUD are separate controllers.
    /// </summary>
    [RequireComponent(typeof(UIDocument))]
    public class UIRoot : MonoBehaviour
    {
        public static UIRoot I { get; private set; }
        public VisualElement Root { get; private set; }
        public MenuUI Menu { get; private set; }
        public Hud Hud { get; private set; }

        static readonly string[] MenuPages = { "main-menu", "play", "loadout", "shop", "profile", "settings" };
        VisualElement modal, connecting, toastLayer, spinner;
        Action modalOk, modalCancel;
        float spin;

        void Awake()
        {
            I = this;
        }

        void OnEnable()
        {
            var doc = GetComponent<UIDocument>();
            Root = doc.rootVisualElement.Q("root") ?? doc.rootVisualElement;
            modal = Root.Q("modal");
            connecting = Root.Q("connecting");
            toastLayer = Root.Q("toast-layer");
            spinner = Root.Q("spinner");
            Root.Q<Button>("modal-ok").clicked += () => { Show("modal", false); var a = modalOk; modalOk = null; a?.Invoke(); };
            Root.Q<Button>("modal-cancel").clicked += () => { Show("modal", false); var a = modalCancel; modalCancel = null; a?.Invoke(); };
            Root.Q<Button>("btn-connect-cancel").clicked += () => { Show("connecting", false); GameFlow.ReturnToMenu(); };
            WireSounds(Root);

            Menu = new MenuUI(this);
            Hud = GetComponent<Hud>() ?? gameObject.AddComponent<Hud>();
            Hud.Init(this);

            GameFlow.EnteredMenu += OnEnteredMenu;
            NetSession.Status += OnStatus;
            NetSession.Failed += OnFailed;
            Combatant.LocalSpawned += OnLocalSpawned;
        }

        void OnDisable()
        {
            GameFlow.EnteredMenu -= OnEnteredMenu;
            NetSession.Status -= OnStatus;
            NetSession.Failed -= OnFailed;
            Combatant.LocalSpawned -= OnLocalSpawned;
        }

        public void WireSounds(VisualElement scope)
        {
            scope.Query<Button>().ForEach(b =>
            {
                if (b.userData is string s && s == "wired") return;
                b.userData = "wired";
                b.RegisterCallback<MouseEnterEvent>(_ => { if (b.enabledInHierarchy) AudioManager.PlayUI(AudioManager.Bank?.uiHover, 0.35f); });
                b.RegisterCallback<ClickEvent>(_ => AudioManager.PlayUI(b.ClassListContains("secondary") && b.text == "BACK" ? AudioManager.Bank?.uiBack : AudioManager.Bank?.uiClick, 0.7f));
            });
        }

        // ------------------------------------------------------------------ screens
        public VisualElement Q(string name) => Root.Q(name);
        public T Q<T>(string name) where T : VisualElement => Root.Q<T>(name);

        public void Show(string screen, bool on)
        {
            var e = Root.Q(screen);
            if (e == null) return;
            if (on) e.AddToClassList("shown"); else e.RemoveFromClassList("shown");
        }

        public bool IsShown(string screen)
        {
            var e = Root.Q(screen);
            return e != null && e.ClassListContains("shown");
        }

        public void ShowMenuPage(string page)
        {
            foreach (var p in MenuPages) Show(p, p == page);
            MenuBackdrop.SetShowcase(page == "loadout");
            FocusFirst(page);
        }

        /// <summary>Give keyboard/gamepad navigation a starting point when a page opens.</summary>
        public void FocusFirst(string screen, Focusable preferred = null)
        {
            var e = Root.Q(screen);
            if (e == null) return;
            e.schedule.Execute(() =>
            {
                var target = preferred ?? e.Query<Button>().Where(b => b.enabledInHierarchy && b.resolvedStyle.display != DisplayStyle.None).First();
                target?.Focus();
            }).StartingIn(30);
        }

        public void HideMenuPages()
        {
            foreach (var p in MenuPages) Show(p, false);
        }

        public bool AnyOverlayOpen => IsShown("pause") || IsShown("settings") || IsShown("loadout") || IsShown("match-end") || IsShown("modal") || IsShown("connecting");

        // ------------------------------------------------------------------ flows
        void OnEnteredMenu()
        {
            Show("hud", false);
            Show("scoreboard", false);
            Show("pause", false);
            Show("match-end", false);
            Show("connecting", false);
            Hud.ResetHud();
            Menu.Refresh();
            ShowMenuPage("main-menu");
            if (!string.IsNullOrEmpty(GameFlow.PendingMessage))
            {
                Modal("NOTICE", GameFlow.PendingMessage, "OK", null);
                GameFlow.PendingMessage = null;
            }
            else if (Profile.Data.firstRun)
            {
                Profile.Data.firstRun = false;
                Profile.Save();
                Modal("WELCOME, RECRUIT", "WASD move  -  SPACE jump  -  CTRL crouch / slide\nMOUSE aim & fire  -  R reload  -  1/2/3 weapons  -  V knife\n\nEarn coins and XP every match. Spend them in the Shop.", "LET'S GO", null);
            }
            AudioManager.Music(AudioManager.Bank?.menuMusic, 0.5f);
        }

        public void SetConnecting(string text)
        {
            HideMenuPages();
            Show("connecting", true);
            Root.Q<Label>("connect-status").text = text;
        }

        void OnStatus(string s)
        {
            if (GameFlow.InMenu && !IsShown("connecting")) return;
            SetConnecting(s);
        }

        void OnFailed(string msg)
        {
            Show("connecting", false);
            GameFlow.ReturnToMenu(msg);
        }

        void OnLocalSpawned(Combatant c)
        {
            Show("connecting", false);
            HideMenuPages();
            GameFlow.OnMatchSceneReady();
            Hud.EnterMatch(c);
            AudioManager.Music(null);
        }

        // ------------------------------------------------------------------ modal & toast
        public void Modal(string title, string body, string ok, Action onOk, string cancel = null, Action onCancel = null)
        {
            Root.Q<Label>("modal-title").text = title;
            Root.Q<Label>("modal-body").text = body;
            var okB = Root.Q<Button>("modal-ok");
            okB.text = ok ?? "OK";
            var cB = Root.Q<Button>("modal-cancel");
            cB.text = cancel ?? "CANCEL";
            cB.style.display = cancel != null ? DisplayStyle.Flex : DisplayStyle.None;
            modalOk = onOk;
            modalCancel = onCancel;
            Show("modal", true);
            modal.BringToFront();
            FocusFirst("modal", okB);
            toastLayer.BringToFront();
        }

        public void Toast(string text, float seconds = 2.4f)
        {
            var t = new Label(text);
            t.AddToClassList("toast");
            t.AddToClassList("out");
            toastLayer.Add(t);
            t.schedule.Execute(() => t.RemoveFromClassList("out")).StartingIn(20);
            t.schedule.Execute(() => t.AddToClassList("out")).StartingIn((long)(seconds * 1000));
            t.schedule.Execute(() => t.RemoveFromHierarchy()).StartingIn((long)(seconds * 1000) + 300);
            while (toastLayer.childCount > 4) toastLayer.RemoveAt(0);
        }

        void Update()
        {
            if (spinner != null && IsShown("connecting"))
            {
                spin = (spin + Time.unscaledDeltaTime * 360f) % 360f;
                spinner.style.rotate = new Rotate(spin);
            }
        }

        // ------------------------------------------------------------------ control builders
        public static VisualElement Row(string label, VisualElement control)
        {
            var row = new VisualElement();
            row.AddToClassList("setting-row");
            var l = new Label(label);
            l.AddToClassList("setting-name");
            row.Add(l);
            row.Add(control);
            return row;
        }

        public static VisualElement Cycler(string[] options, Func<int> get, Action<int> set)
        {
            var c = new VisualElement();
            c.AddToClassList("cycler");
            var left = new Button { text = "<" };
            left.AddToClassList("arrow");
            var right = new Button { text = ">" };
            right.AddToClassList("arrow");
            var val = new Label();
            val.AddToClassList("cycler-value");
            void Refresh() => val.text = options[Mathf.Clamp(get(), 0, options.Length - 1)];
            left.clicked += () => { set((get() - 1 + options.Length) % options.Length); Refresh(); AudioManager.PlayUI(AudioManager.Bank?.uiClick, 0.5f); };
            right.clicked += () => { set((get() + 1) % options.Length); Refresh(); AudioManager.PlayUI(AudioManager.Bank?.uiClick, 0.5f); };
            c.Add(left); c.Add(val); c.Add(right);
            Refresh();
            c.userData = (Action)Refresh;
            return c;
        }

        public static VisualElement SliderControl(float min, float max, Func<float> get, Action<float> set, string format = "0.00", float display = 1f)
        {
            var wrap = new VisualElement();
            wrap.AddToClassList("slider-wrap");
            var s = new Slider(min, max) { value = get() };
            var v = new Label((get() * display).ToString(format));
            v.AddToClassList("slider-value");
            s.RegisterValueChangedCallback(e => { set(e.newValue); v.text = (e.newValue * display).ToString(format); });
            wrap.Add(s); wrap.Add(v);
            return wrap;
        }

        public static Button Btn(string text, string cls, Action onClick)
        {
            var b = new Button(onClick) { text = text };
            b.AddToClassList("btn");
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) b.AddToClassList(c);
            return b;
        }

        public static Label Lbl(string text, string cls)
        {
            var l = new Label(text);
            if (!string.IsNullOrEmpty(cls)) foreach (var c in cls.Split(' ')) l.AddToClassList(c);
            return l;
        }
    }
}
