using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Threading;
using UnityEngine;
using ZXing;
using ZXing.Common;
#if UNITY_ANDROID && !UNITY_EDITOR
using UnityEngine.Android;
#endif

namespace Sygnet.App
{
    /// <summary>
    /// Tylna kamera (WebCamTexture) + ZXing (CLIENT_UNITY.md §4.3). Co ~200 ms kopiuje klatkę, a dekodowanie
    /// idzie w wątku tła. Zwraca SUROWY tekst kodu – decyzję „SYGNET czy nie” podejmuje aplikacja.
    /// Nigdy nie otwiera URL-i.
    /// </summary>
    public class QrScanner : MonoBehaviour
    {
        public enum State { Idle, WaitingForPermission, PermissionDenied, NoCamera, Starting, Running }

        const float DecodeInterval = 0.2f;

        public State Status { get; private set; } = State.Idle;
        public WebCamTexture Texture { get; private set; }

        /// <summary>Odczytany tekst QR (wątek główny). Ten sam kod jest zgłaszany ponownie dopiero po 3 s.</summary>
        public event Action<string> TextScanned;

        readonly ConcurrentQueue<string> results = new ConcurrentQueue<string>();
        Color32[] pixels;
        byte[] gray;
        int busy;                 // 1 = wątek tła dekoduje
        float nextDecode;
        string lastText;
        float lastTextTime;
        BarcodeReaderGeneric reader;

        void Awake() => reader = CreateReader();

        public static BarcodeReaderGeneric CreateReader() => new BarcodeReaderGeneric
        {
            AutoRotate = false,
            Options = new DecodingOptions
            {
                PossibleFormats = new List<BarcodeFormat> { BarcodeFormat.QR_CODE },
                TryHarder = true,
            },
        };

        /// <summary>
        /// Klatka w układzie tekstury Unity (wiersz 0 na dole) → luminancja z odwróconymi wierszami (inaczej kod byłby
        /// lustrzany) → ZXing. Zwraca tekst albo null. Bezpieczne w wątku tła (o ile reader nie jest współdzielony).
        /// </summary>
        public static string DecodeFrame(BarcodeReaderGeneric reader, Color32[] px, byte[] gray, int w, int h)
        {
            for (int y = 0; y < h; y++)
            {
                int src = (h - 1 - y) * w, dst = y * w;
                for (int x = 0; x < w; x++)
                {
                    var c = px[src + x];
                    gray[dst + x] = (byte)((c.r * 77 + c.g * 150 + c.b * 29) >> 8);
                }
            }
            var result = reader.Decode(gray, w, h, RGBLuminanceSource.BitmapFormat.Gray8);
            return string.IsNullOrEmpty(result?.Text) ? null : result.Text;
        }

        public void StartCamera()
        {
            if (Status == State.Running || Status == State.Starting) return;
#if UNITY_ANDROID && !UNITY_EDITOR
            if (!Permission.HasUserAuthorizedPermission(Permission.Camera))
            {
                Status = State.WaitingForPermission;
                var cb = new PermissionCallbacks();
                cb.PermissionGranted += _ => OpenCamera();
                cb.PermissionDenied += _ => Status = State.PermissionDenied;
                Permission.RequestUserPermission(Permission.Camera, cb);
                return;
            }
#endif
            OpenCamera();
        }

        void OpenCamera()
        {
            var devices = WebCamTexture.devices;
            if (devices.Length == 0)
            {
                Status = State.NoCamera;
                return;
            }
            string name = devices[0].name;
            foreach (var d in devices)
                if (!d.isFrontFacing) { name = d.name; break; }

            Texture = new WebCamTexture(name, 1280, 720, 30);
            Texture.Play();
            Status = State.Starting;
            nextDecode = Time.unscaledTime + 0.3f;
        }

        public void StopCamera()
        {
            if (Texture != null)
            {
                Texture.Stop();
                Destroy(Texture);
                Texture = null;
            }
            if (Status != State.PermissionDenied) Status = State.Idle;
            lastText = null;
        }

        void Update()
        {
            while (results.TryDequeue(out var text))
            {
                if (text == lastText && Time.unscaledTime - lastTextTime < 3f) continue;
                lastText = text;
                lastTextTime = Time.unscaledTime;
                TextScanned?.Invoke(text);
            }

            if (Texture == null || !Texture.isPlaying) return;
            if (Texture.width < 100) return;                    // pierwsze klatki mają 16×16
            Status = State.Running;
            if (Time.unscaledTime < nextDecode || !Texture.didUpdateThisFrame) return;
            if (Interlocked.CompareExchange(ref busy, 1, 0) != 0) return;
            nextDecode = Time.unscaledTime + DecodeInterval;

            int w = Texture.width, h = Texture.height;
            if (pixels == null || pixels.Length != w * h)
            {
                pixels = new Color32[w * h];
                gray = new byte[w * h];
            }
            Texture.GetPixels32(pixels);
            var px = pixels;
            var g = gray;
            ThreadPool.QueueUserWorkItem(_ => DecodeWorker(px, g, w, h));
        }

        void DecodeWorker(Color32[] px, byte[] g, int w, int h)
        {
            try
            {
                var text = DecodeFrame(reader, px, g, w, h);
                if (text != null) results.Enqueue(text);
            }
            catch (Exception e)
            {
                Debug.LogWarning("[SYGNET] QR: " + e.Message);
            }
            finally
            {
                Interlocked.Exchange(ref busy, 0);
            }
        }

        void OnDisable() => StopCamera();
    }
}
