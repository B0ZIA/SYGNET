using System;
using Sygnet.App.UI;
using Sygnet.Core;
using UnityEngine;

namespace Sygnet.App
{
    /// <summary>
    /// Powiadomienie systemowe o wyniku weryfikacji komunikatu odebranego w tle (np. z telewizora, gdy telefon
    /// leży w kieszeni). Treść tylko z poprawnie podpisanego komunikatu; przy fałszywce – powód i ostrzeżenie,
    /// bez powielania treści atakującego. Wołane z wątku nasłuchu (bez API Unity poza AndroidJNI).
    /// </summary>
    public static class AlertNotification
    {
        public static (string title, string text) Compose(VerificationResult r, TrustStore trust)
        {
            var p = r.Payload;
            var type = AlertTypes.Get(p.Type);
            string issuer = r.IssuerName ?? "nieznany nadawca (ID " + p.IssuerId + ")";
            string note = p.Note.Length > 0 && p.Type != AlertTypes.KeyRevoke ? "„" + p.NoteText + "”\n" : "";
            switch (r.Status)
            {
                case VerifyStatus.Verified:
                    return ("✅ ZWERYFIKOWANO · " + type.Name,
                        note + issuer + " · " + Areas.Name(p.AreaCode) + "\nCo robić: " + type.Instruction);
                case VerifyStatus.VerifiedOtherArea:
                    return ("ℹ️ INNY OBSZAR · " + type.Name,
                        note + issuer + " · " + Areas.Name(p.AreaCode) + "\nPrawdziwy, ale nie dotyczy Twojego obszaru.");
                case VerifyStatus.Expired:
                    return ("⚠️ NIEAKTUALNY · " + type.Name,
                        "Prawdziwy, ale wygasł " + Time(p.ValidUntil) + ". Możliwe odtworzone nagranie.\n" +
                        "Ten komunikat już nie obowiązuje.");
                case VerifyStatus.Incomplete:
                    return ("⛔ NIEPEŁNY PODPIS · " + type.Name,
                        "Komunikat krytyczny bez drugiego, niezależnego podpisu.\nNIE WYKONUJ poleceń z tego komunikatu.");
                default:
                    return ("⛔ FAŁSZYWKA · podaje się za: " + type.Name,
                        (Messages.Reason(r, trust) ?? Messages.Summary(r.Status)) +
                        "\nNie wykonuj poleceń. Ufaj tylko komunikatom zweryfikowanym w SYGNET.");
            }
        }

        /// <summary>Pilne (wibracja jak alarm): zweryfikowany alarm/ewakuacja/chemia oraz każde odrzucenie.</summary>
        public static bool IsUrgent(VerificationResult r)
        {
            if (r.Status == VerifyStatus.Forged || r.Status == VerifyStatus.Incomplete) return true;
            int t = r.Payload.Type;
            return r.Status == VerifyStatus.Verified &&
                   (t == AlertTypes.AirRaid || t == AlertTypes.Evacuation || t == AlertTypes.Chemical);
        }

        /// <summary>Stały identyfikator cichego, zbiorczego powiadomienia o kolejnych fałszywkach (aktualizowane, nie dokładane).</summary>
        public const int MutedFakesId = 50;

        /// <summary>
        /// Kolejne fałszywki w krótkim czasie – bez dźwięku i wibracji, jedno powiadomienie aktualizowane licznikiem.
        /// Przeciwnik, który przejmie nadajnik i sypie śmieciowymi ramkami, nie zmęczy ludzi alarmami.
        /// </summary>
        public static void PostMutedFakes(int count, int windowMinutes)
        {
            string title = "⛔ Kolejne fałszywki: " + count;
            string text = "W ciągu " + windowMinutes + " min telefon odebrał więcej fałszywych komunikatów. " +
                          "Pominięte bez alarmu – są w skrzynce. Nie wykonuj poleceń z niezweryfikowanych komunikatów.";
            Color32 c = Theme.Danger;
            int argb = unchecked((int)(0xFF000000u | (uint)c.r << 16 | (uint)c.g << 8 | c.b));
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var service = new AndroidJavaClass("pl.hackyeah.sygnet.SygnetListenService"))
                    service.CallStatic("notifyQuiet", MutedFakesId, title, text, argb);
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Powiadomienie zbiorcze nie wysłane: " + e);
            }
#else
            Debug.Log("[SYGNET] Powiadomienie ciche #" + MutedFakesId + ": " + title + " | " + text);
#endif
        }

        /// <summary>Wysyła powiadomienie (Android; w edytorze tylko log). Wątek musi być podpięty do JVM.</summary>
        public static void Post(VerificationResult r, TrustStore trust, int id)
        {
            var (title, text) = Compose(r, trust);
            Color32 c = Theme.ForStatus(r.Status);
            int argb = unchecked((int)(0xFF000000u | (uint)c.r << 16 | (uint)c.g << 8 | c.b));
#if UNITY_ANDROID && !UNITY_EDITOR
            try
            {
                using (var service = new AndroidJavaClass("pl.hackyeah.sygnet.SygnetListenService"))
                    service.CallStatic("notifyResult", id, title, text, argb, IsUrgent(r));
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Powiadomienie nie wysłane: " + e);
            }
#else
            Debug.Log("[SYGNET] Powiadomienie #" + id + ": " + title + " | " + text.Replace("\n", " | ") + " | #" + argb.ToString("X8"));
#endif
        }

        static string Time(long unix) =>
            DateTimeOffset.FromUnixTimeSeconds(unix).ToLocalTime().ToString("dd.MM.yyyy HH:mm");
    }
}
