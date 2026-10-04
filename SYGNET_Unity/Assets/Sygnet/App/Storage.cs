using System;
using System.Collections.Generic;
using System.IO;
using Sygnet.Core;
using UnityEngine;

namespace Sygnet.App
{
    [Serializable]
    public class InboxEntry
    {
        public string frameHex;
        public string status;      // VerifyStatus jako nazwa z protokołu, np. "VERIFIED"
        public string reason;
        public long receivedAt;    // unix s
        public string source;      // "qr" | "audio"
    }

    /// <summary>
    /// Stan aplikacji w JSON w Application.persistentDataPath (CLIENT_UNITY.md §4.5): obszar użytkownika,
    /// skrzynka, zbiór seen (issuer, seq) i unieważnieni wydawcy. Wszystko offline.
    /// </summary>
    public class Storage
    {
        [Serializable]
        class Data
        {
            public int version = 1;
            public int userArea = 1261;                 // Kraków – miejsce demo (HackYeah)
            public bool onboarded;
            public List<InboxEntry> inbox = new List<InboxEntry>();
            public List<int> seenIssuer = new List<int>();
            public List<int> seenSeq = new List<int>();
            public List<int> revoked = new List<int>();
        }

        public const int InboxLimit = 200;

        readonly string path;
        Data data = new Data();

        public readonly HashSet<(int issuer, int seq)> Seen = new HashSet<(int issuer, int seq)>();
        public readonly HashSet<int> Revoked = new HashSet<int>();

        public Storage(string directory)
        {
            path = Path.Combine(directory, "sygnet_state.json");
        }

        public int UserArea
        {
            get => data.userArea;
            set { data.userArea = value; Save(); }
        }

        public bool Onboarded
        {
            get => data.onboarded;
            set { data.onboarded = value; Save(); }
        }

        /// <summary>Najnowsze na początku.</summary>
        public IReadOnlyList<InboxEntry> Inbox => data.inbox;

        public void Load()
        {
            try
            {
                if (File.Exists(path)) data = JsonUtility.FromJson<Data>(File.ReadAllText(path)) ?? new Data();
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Nie udało się wczytać stanu, zaczynam od zera: " + e.Message);
                data = new Data();
            }
            Seen.Clear();
            for (int i = 0; i < Mathf.Min(data.seenIssuer.Count, data.seenSeq.Count); i++)
                Seen.Add((data.seenIssuer[i], data.seenSeq[i]));
            Revoked.Clear();
            foreach (var r in data.revoked) Revoked.Add(r);
        }

        public void Save()
        {
            data.seenIssuer.Clear();
            data.seenSeq.Clear();
            foreach (var (issuer, seq) in Seen)
            {
                data.seenIssuer.Add(issuer);
                data.seenSeq.Add(seq);
            }
            data.revoked = new List<int>(Revoked);
            try
            {
                var tmp = path + ".tmp";
                File.WriteAllText(tmp, JsonUtility.ToJson(data));
                if (File.Exists(path)) File.Delete(path);
                File.Move(tmp, path);
            }
            catch (Exception e)
            {
                Debug.LogError("[SYGNET] Nie udało się zapisać stanu: " + e.Message);
            }
        }

        public InboxEntry AddToInbox(VerificationResult r, string source, long now)
        {
            var e = new InboxEntry
            {
                frameHex = Bytes.ToHex(r.RawFrame),
                status = r.StatusName,
                reason = r.ReasonCode,
                receivedAt = now,
                source = source,
            };
            data.inbox.Insert(0, e);
            if (data.inbox.Count > InboxLimit) data.inbox.RemoveRange(InboxLimit, data.inbox.Count - InboxLimit);
            return e;
        }

        /// <summary>Wpis skrzynki dla tego samego (issuer, seq) z poprawnym podpisem – do pokazania przy DUPLICATE.</summary>
        public InboxEntry FindAuthentic(int issuer, int seq)
        {
            foreach (var e in data.inbox)
            {
                if (e.status != "VERIFIED" && e.status != "VERIFIED_OTHER_AREA") continue;
                if (!Frame.TryParse(Bytes.FromHex(e.frameHex), out var f, out _)) continue;
                if (f.Payload.IssuerId == issuer && f.Payload.Sequence == seq) return e;
            }
            return null;
        }

        /// <summary>Do testów na scenie (panel debug): czyści skrzynkę, seen i unieważnienia, zostawia obszar.</summary>
        /// <summary>
        /// Czyści skrzynkę i pamięć odbioru. Unieważnione klucze tylko na życzenie (<paramref name="revokedToo"/>) –
        /// w wydaniu nigdy, inaczej skradziony, unieważniony klucz znów byłby zaufany.
        /// </summary>
        public void ClearMessages(bool revokedToo)
        {
            data.inbox.Clear();
            Seen.Clear();
            if (revokedToo) Revoked.Clear();
            Save();
        }
    }
}
