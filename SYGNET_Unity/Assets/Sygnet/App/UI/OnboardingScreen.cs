using Sygnet.Core;
using TMPro;
using UnityEngine;
using UnityEngine.UI;

namespace Sygnet.App.UI
{
    /// <summary>
    /// Pierwsze uruchomienie (CLIENT_UNITY.md §5.1): 1) czym jest SYGNET, 2) wybór obszaru,
    /// 3) odcisk klucza głównego do porównania z wydrukiem + prośba o mikrofon. Potem nasłuch.
    /// </summary>
    public class OnboardingScreen : AppScreen
    {
        const int Steps = 3;

        readonly RectTransform page;
        readonly Image[] dots = new Image[Steps];
        readonly Button next;
        int step;
        int area;

        public OnboardingScreen(SygnetApp app, Transform canvas) : base(app, canvas, "Onboarding", Theme.Bg)
        {
            var dotsRow = Ui.Rect(Safe, "Dots");
            Ui.Top(dotsRow, 64, 20, Theme.Margin);
            Ui.HStack(dotsRow, 14, TextAnchor.MiddleCenter);
            for (int i = 0; i < Steps; i++)
            {
                dots[i] = Ui.Card(dotsRow, "Dot", Theme.Dim, 10);
                Ui.Layout(dots[i], 20, 20, 20);
            }

            page = Ui.Rect(Safe, "Page");
            Ui.Stretch(page, 0, 120, 0, 40 + Theme.ButtonHeight + 40);

            next = Ui.Button(Safe, "Dalej", ButtonKind.Primary, Next);
            Ui.Bottom((RectTransform)next.transform, 48, Theme.ButtonHeight, Theme.Margin);
        }

        public override void OnShow()
        {
            step = 0;
            area = App.Store.UserArea;
            Build();
        }

        public override bool OnBack()
        {
            if (step == 0) return false;
            step--;
            Build();
            return true;
        }

        void Next()
        {
            if (step < Steps - 1)
            {
                step++;
                Build();
                BeginEnter();
                return;
            }
            App.Store.UserArea = area;
            App.Store.Onboarded = true;
            App.Mic.StartListening();
            App.Show(App.Home);
        }

        void Build()
        {
            for (int i = page.childCount - 1; i >= 0; i--) Object.Destroy(page.GetChild(i).gameObject);
            for (int i = 0; i < Steps; i++)
            {
                dots[i].color = i == step ? Theme.Text : Theme.Dim;
                dots[i].GetComponent<LayoutElement>().preferredWidth = i == step ? 48 : 20;
            }
            var content = Ui.Scroll(page, "Scroll", 28, new RectOffset(64, 64, 40, 40));
            switch (step)
            {
                case 0: Welcome(content); break;
                case 1: Area(content); break;
                default: RootKey(content); break;
            }
            Ui.SetLabel(next, step == Steps - 1 ? "Zacznij nasłuch" : "Dalej");
        }

        void Welcome(RectTransform c)
        {
            var hero = Ui.Rect(c, "Hero");
            Ui.Layout(hero, 360, 360);
            var mark = Ui.Image(hero, "Mark", Theme.Text, Resources.Load<Sprite>("sygnet_logo"));
            mark.preserveAspect = true;
            Ui.Center(mark.rectTransform, new Vector2(260, 260), new Vector2(0, 20));
            Ui.Label(c, "Sygnet", TextStyle.Wordmark, Theme.Text, TextAlignmentOptions.Center).fontSize = 72;
            Ui.Label(c, "Podpisane komunikaty kryzysowe, które sprawdzisz bez internetu.", TextStyle.Headline, Theme.Text,
                TextAlignmentOptions.Center);
            Ui.Layout(Ui.Rect(c, "Gap"), 24, 24);
            Feature(c, Icons.Mic, "Słucha komunikatów z radia, telewizji, megafonu i telefonów sąsiadów – także w tle.");
            Feature(c, Icons.Qr, "Skanuje kody z plakatów i ekranów.");
            Feature(c, Icons.Key, "Sprawdza podpis offline. Fałszywkę widać od razu.");
        }

        void Area(RectTransform c)
        {
            Ui.Label(c, "Gdzie jesteś?", TextStyle.Title, Theme.Text);
            Ui.Label(c, "Pokażemy, które komunikaty dotyczą Ciebie. Zmienisz to później na ekranie głównym.",
                TextStyle.Body, Theme.Muted);
            new AreaPicker(c, area, code => area = code);
        }

        void RootKey(RectTransform c)
        {
            Ui.Label(c, "Sprawdź klucz główny", TextStyle.Title, Theme.Text);
            Ui.Label(c, "To „pieczęć pieczęci”: nim podpisani są wszyscy nadawcy. Porównaj odcisk z wydrukiem w urzędzie " +
                        "albo w innym oficjalnym źródle – jeszcze przed kryzysem.", TextStyle.Body, Theme.Muted);
            var card = Ui.Card(c, "Fingerprint", Theme.Surface2);
            Ui.VStack(card.rectTransform, 12, new RectOffset(44, 44, 40, 44), TextAnchor.UpperCenter);
            Ui.Label(card.transform, "Odcisk klucza ROOT", TextStyle.Overline, Theme.Muted, TextAlignmentOptions.Center);
            var fp = Ui.Label(card.transform, App.Trust.RootFingerprint, TextStyle.Mono, Theme.Text, TextAlignmentOptions.Center);
            fp.font = Theme.MonoBold;
            fp.enableAutoSizing = true;
            fp.fontSizeMin = 36;
            fp.fontSizeMax = 62;
            fp.textWrappingMode = TextWrappingModes.NoWrap;
            if (SygnetApp.RootIsTestKey)
                Ui.Label(card.transform, "klucz testowy z wektorów", TextStyle.Caption, Theme.Expired, TextAlignmentOptions.Center);
            Ui.Layout(Ui.Rect(c, "Gap"), 8, 8);
            Feature(c, Icons.Mic, "Za chwilę poprosimy o mikrofon i powiadomienia. Dźwięk analizujemy tylko w telefonie – nic nie wysyłamy.");
            Feature(c, Icons.Plane, "Aplikacja nie ma dostępu do internetu i działa w trybie samolotowym.");
        }

        static void Feature(Transform parent, Sprite icon, string text)
        {
            var row = Ui.Rect(parent, "Feature");
            var h = Ui.HStack(row, 28, TextAnchor.UpperLeft);
            h.childForceExpandWidth = false;
            var circle = Ui.Image(row, "Circle", Theme.Surface2, Icons.Circle);
            Ui.Layout(circle, 96, 96, 96);
            var ic = Ui.Image(circle.transform, "Icon", Theme.Text, icon);
            Ui.Center(ic.rectTransform, new Vector2(50, 50));
            var t = Ui.Label(row, text, TextStyle.Body, Theme.Text, TextAlignmentOptions.MidlineLeft);
            Ui.Layout(t, 96, -1, -1, 1);
        }
    }
}
