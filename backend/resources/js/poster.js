// Plakat A4: kod QR jako wektorowy SVG (ostry w druku), poziom korekcji M jak w PROTOCOL.md §3.
import QRCode from 'qrcode';

import.meta.glob(['../images/**'], { eager: true });

const box = document.getElementById('poster-qr');
if (box) {
    QRCode.toString(box.dataset.qr, { type: 'svg', errorCorrectionLevel: 'M', margin: 4, color: { dark: '#000000', light: '#ffffff' } })
        .then((svg) => {
            box.innerHTML = svg;
            document.body.dataset.ready = '1';            // dla druku do PDF bez przeglądarki (headless)
        });
}
