using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>Ekran aplikacji: pełnoekranowe tło + obszar bezpieczny (wycięcie aparatu, paski systemowe).</summary>
    public abstract class AppScreen
    {
        protected readonly SygnetApp App;
        public readonly RectTransform Root;
        public readonly Image Background;
        public readonly RectTransform Safe;

        protected AppScreen(SygnetApp app, Transform canvas, string name, Color background)
        {
            App = app;
            Root = Ui.Rect(canvas, name);
            Background = Ui.Image(Root, "Background", background);
            Safe = Ui.Rect(Root, "Safe");
            app.RegisterSafeArea(Safe);
            Root.gameObject.SetActive(false);
        }

        public bool Visible => Root.gameObject.activeSelf;

        public virtual void OnShow() { }
        public virtual void OnHide() { }
        public virtual void Tick() { }

        /// <summary>Przycisk „wstecz” Androida. false = ekran nie obsłużył (np. ekran główny).</summary>
        public virtual bool OnBack() => false;
    }
}
