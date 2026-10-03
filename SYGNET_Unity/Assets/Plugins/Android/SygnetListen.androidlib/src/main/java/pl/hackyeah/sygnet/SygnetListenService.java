package pl.hackyeah.sygnet;

import android.app.Notification;
import android.app.NotificationChannel;
import android.app.NotificationManager;
import android.app.PendingIntent;
import android.app.Service;
import android.content.Context;
import android.content.Intent;
import android.content.pm.ServiceInfo;
import android.media.AudioFormat;
import android.media.AudioManager;
import android.media.AudioRecord;
import android.media.MediaRecorder;
import android.os.Build;
import android.os.IBinder;
import android.os.PowerManager;
import android.util.Log;

/**
 * SYGNET: nasłuch mikrofonu także wtedy, gdy aplikacja jest w tle lub ekran jest zgaszony.
 *
 * Usługa pierwszoplanowa typu „microphone” nagrywa PCM 16-bit mono (źródło UNPROCESSED, jeśli telefon je ma –
 * bez tłumienia szumów i AGC, które psują tony modemu; inaczej VOICE_RECOGNITION) do bufora kołowego.
 * Dekodowanie i weryfikację podpisu robi kod C# (ten sam co w aplikacji) w swoim wątku: woła {@link #drain()}.
 * Wynik trafia z powrotem przez {@link #notifyResult}. Usługa niczego nie wysyła do sieci.
 */
public class SygnetListenService extends Service {
    static final String TAG = "SYGNET";
    static final String CH_LISTEN = "sygnet_listen";
    static final String CH_ALERT = "sygnet_alert";
    static final int ID_LISTEN = 1;
    static final int RATE = 48000;

    static final Object lock = new Object();
    static final short[] ring = new short[RATE * 4];   // 4 s zapasu, gdyby wątek C# się spóźnił
    static int ringWrite, ringPending;
    static volatile boolean running;
    static volatile int sampleRate;
    static volatile String source = "";
    static volatile Context appContext;

    private AudioRecord recorder;
    private Thread thread;
    private PowerManager.WakeLock wakeLock;

    // ───────────── API dla C# ─────────────

    /** Startuje usługę (wołać, gdy aplikacja jest na ekranie – wymóg Androida dla mikrofonu). */
    public static void start(Context ctx) {
        appContext = ctx.getApplicationContext();
        ctx.startForegroundService(new Intent(ctx, SygnetListenService.class));
    }

    public static void stop(Context ctx) {
        ctx.stopService(new Intent(ctx, SygnetListenService.class));
    }

    public static boolean isRunning() { return running; }

    public static int getSampleRate() { return sampleRate; }

    public static String getSource() { return source; }

    /** Próbki nagrane od poprzedniego wywołania (FIFO). Pusta tablica, gdy nic nowego. */
    public static short[] drain() {
        synchronized (lock) {
            short[] out = new short[ringPending];
            int start = (ringWrite - ringPending + ring.length) % ring.length;
            for (int i = 0; i < ringPending; i++) out[i] = ring[(start + i) % ring.length];
            ringPending = 0;
            return out;
        }
    }

    /** Powiadomienie o wyniku weryfikacji (dotknięcie otwiera aplikację z ekranem wyniku). */
    public static void notifyResult(int id, String title, String text, int color, boolean urgent) {
        Context ctx = appContext;
        if (ctx == null) return;
        createChannels(ctx);
        Notification n = new Notification.Builder(ctx, CH_ALERT)
                .setSmallIcon(smallIcon(ctx))
                .setContentTitle(title)
                .setContentText(text)
                .setStyle(new Notification.BigTextStyle().bigText(text))
                .setColor(color)
                .setAutoCancel(true)
                .setShowWhen(true)
                .setCategory(urgent ? Notification.CATEGORY_ALARM : Notification.CATEGORY_MESSAGE)
                .setVisibility(Notification.VISIBILITY_PUBLIC)
                .setContentIntent(openApp(ctx))
                .build();
        ctx.getSystemService(NotificationManager.class).notify(id, n);
    }

    // ───────────── usługa ─────────────

    @Override
    public void onCreate() {
        super.onCreate();
        appContext = getApplicationContext();
        createChannels(this);
    }

    @Override
    public int onStartCommand(Intent intent, int flags, int startId) {
        Notification n = new Notification.Builder(this, CH_LISTEN)
                .setSmallIcon(smallIcon(this))
                .setContentTitle("SYGNET nasłuchuje komunikatów")
                .setContentText("Działa w tle i bez internetu. Fałszywkę rozpoznasz od razu.")
                .setOngoing(true)
                .setCategory(Notification.CATEGORY_SERVICE)
                .setContentIntent(openApp(this))
                .build();
        if (Build.VERSION.SDK_INT >= 30) startForeground(ID_LISTEN, n, ServiceInfo.FOREGROUND_SERVICE_TYPE_MICROPHONE);
        else startForeground(ID_LISTEN, n);
        startRecording();
        return START_STICKY;
    }

    private void startRecording() {
        if (running) return;
        int src = MediaRecorder.AudioSource.VOICE_RECOGNITION;
        String name = "VOICE_RECOGNITION";
        AudioManager am = (AudioManager) getSystemService(AUDIO_SERVICE);
        if ("true".equals(am.getProperty(AudioManager.PROPERTY_SUPPORT_AUDIO_SOURCE_UNPROCESSED))) {
            src = MediaRecorder.AudioSource.UNPROCESSED;
            name = "UNPROCESSED";
        }
        int min = AudioRecord.getMinBufferSize(RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT);
        try {
            recorder = new AudioRecord(src, RATE, AudioFormat.CHANNEL_IN_MONO, AudioFormat.ENCODING_PCM_16BIT, Math.max(min, RATE));
        } catch (SecurityException e) {
            Log.w(TAG, "Brak uprawnienia do mikrofonu", e);
            stopSelf();
            return;
        }
        if (recorder.getState() != AudioRecord.STATE_INITIALIZED) {
            Log.w(TAG, "AudioRecord nie wystartował");
            recorder.release();
            recorder = null;
            stopSelf();
            return;
        }
        sampleRate = recorder.getSampleRate();
        source = name;
        PowerManager pm = (PowerManager) getSystemService(POWER_SERVICE);
        wakeLock = pm.newWakeLock(PowerManager.PARTIAL_WAKE_LOCK, "sygnet:listen");
        wakeLock.acquire();
        recorder.startRecording();
        running = true;
        thread = new Thread(this::recordLoop, "sygnet-audio");
        thread.start();
        Log.i(TAG, "Nasłuch w tle: " + name + " @ " + sampleRate + " Hz");
    }

    private void recordLoop() {
        short[] chunk = new short[RATE / 50];                      // 20 ms
        while (running) {
            int n = recorder.read(chunk, 0, chunk.length);
            if (n <= 0) continue;
            synchronized (lock) {
                for (int i = 0; i < n; i++) {
                    ring[ringWrite] = chunk[i];
                    ringWrite = (ringWrite + 1) % ring.length;
                }
                ringPending = Math.min(ring.length, ringPending + n);
            }
        }
    }

    @Override
    public void onDestroy() {
        running = false;
        try {
            if (thread != null) thread.join(500);
        } catch (InterruptedException ignored) {
        }
        if (recorder != null) {
            try {
                recorder.stop();
            } catch (IllegalStateException ignored) {
            }
            recorder.release();
            recorder = null;
        }
        if (wakeLock != null && wakeLock.isHeld()) wakeLock.release();
        super.onDestroy();
    }

    @Override
    public IBinder onBind(Intent intent) { return null; }

    // ───────────── pomocnicze ─────────────

    static void createChannels(Context ctx) {
        NotificationManager nm = ctx.getSystemService(NotificationManager.class);
        NotificationChannel listen = new NotificationChannel(CH_LISTEN, "Nasłuch komunikatów", NotificationManager.IMPORTANCE_LOW);
        listen.setDescription("Stałe powiadomienie, gdy SYGNET słucha w tle");
        listen.setShowBadge(false);
        NotificationChannel alert = new NotificationChannel(CH_ALERT, "Komunikaty SYGNET", NotificationManager.IMPORTANCE_HIGH);
        alert.setDescription("Wynik weryfikacji odebranego komunikatu");
        alert.enableVibration(true);
        alert.setVibrationPattern(new long[] { 0, 600, 300, 600, 300, 600 });
        alert.setLockscreenVisibility(Notification.VISIBILITY_PUBLIC);
        nm.createNotificationChannel(listen);
        nm.createNotificationChannel(alert);
    }

    static int smallIcon(Context ctx) {
        int id = ctx.getResources().getIdentifier("sygnet_notify", "drawable", ctx.getPackageName());
        return id != 0 ? id : ctx.getApplicationInfo().icon;
    }

    static PendingIntent openApp(Context ctx) {
        Intent launch = ctx.getPackageManager().getLaunchIntentForPackage(ctx.getPackageName());
        launch.addFlags(Intent.FLAG_ACTIVITY_NEW_TASK | Intent.FLAG_ACTIVITY_SINGLE_TOP);
        return PendingIntent.getActivity(ctx, 0, launch, PendingIntent.FLAG_IMMUTABLE | PendingIntent.FLAG_UPDATE_CURRENT);
    }
}
