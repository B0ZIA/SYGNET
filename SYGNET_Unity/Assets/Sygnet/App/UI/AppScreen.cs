using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>Ekran aplikacji: pełnoekranowe tło + obszar bezpieczny (wycięcie aparatu, paski systemowe).</summary>
    public abstract class AppScreen
    {
        const float FadeSeconds = 0.18f;
        const float SlideUnits = 28f;

        protected readonly SygnetApp App;
        public readonly RectTransform Root;
        public readonly Image Background;
        public readonly RectTransform Safe;
        readonly CanvasGroup group;
        readonly RectTransform content;   // przesuwany przy wejściu (obszar bezpieczny ma kotwice z safeArea)
        float shown = 1;

        protected AppScreen(SygnetApp app, Transform canvas, string name, Color background)
        {
            App = app;
            Root = Ui.Rect(canvas, name);
            group = Root.gameObject.AddComponent<CanvasGroup>();
            Background = Ui.Image(Root, "Background", background);
            content = Ui.Rect(Root, "Content");
            Safe = Ui.Rect(content, "Safe");
            app.RegisterSafeArea(Safe);
            Root.gameObject.SetActive(false);
        }

        public bool Visible => Root.gameObject.activeSelf;

        /// <summary>Start animacji wejścia – woła SygnetApp.Show.</summary>
        public void BeginEnter()
        {
            shown = 0;
            Animate(0);
        }

        /// <summary>Krok animacji wejścia (ease-out).</summary>
        public void AnimateEnter(float dt)
        {
            if (shown >= 1) return;
            shown = Mathf.Min(1, shown + dt / FadeSeconds);
            Animate(shown);
        }

        void Animate(float t)
        {
            float e = 1 - (1 - t) * (1 - t);
            group.alpha = e;
            content.anchoredPosition = new Vector2(0, -SlideUnits * (1 - e));
        }

        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick() { }

        /// <summary>Przycisk „wstecz” Androida. false = ekran nie obsłużył (np. ekran główny).</summary>
        public virtual bool OnBack() => false;
    }
}
