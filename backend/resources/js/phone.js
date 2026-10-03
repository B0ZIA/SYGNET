// Podgląd ekranu telefonu – te same teksty i kolory co ekran wyniku w aplikacji (ResultScreen.cs, Messages.cs).
import { clock, dateTime } from './frame';

export const STATUS_COLOR = {
    VERIFIED: '#0E7A3E',
    VERIFIED_OTHER_AREA: '#1F4E8C',
    EXPIRED: '#B7791F',
    INCOMPLETE: '#B42318',
    FORGED: '#B42318',
};

const TITLE = {
    VERIFIED: 'ZWERYFIKOWANO',
    VERIFIED_OTHER_AREA: 'INNY OBSZAR',
    EXPIRED: 'NIEAKTUALNY',
    INCOMPLETE: 'NIEPEŁNY PODPIS',
    FORGED: 'FAŁSZYWKA',
    DUPLICATE: 'JUŻ OTRZYMANY',
};

const STATUS_ICON = { VERIFIED: 'check', VERIFIED_OTHER_AREA: 'check', EXPIRED: 'warning' };

export const covers = (c, a) => c === 0 || c === a || (c < 100 && Math.floor(a / 100) === c);

function statusLine(status, b) {
    switch (status) {
        case 'VERIFIED': return 'Podpis sprawdzony. Dotyczy Twojego obszaru.';
        case 'VERIFIED_OTHER_AREA': return `Prawdziwy komunikat dla innego obszaru: ${b.area_name}.`;
        case 'EXPIRED': return `Prawdziwy, ale wygasł ${dateTime(b.valid_until)}. Możliwe odtworzone nagranie.`;
        case 'INCOMPLETE': return 'Brakuje drugiego, niezależnego podpisu.';
        case 'FORGED': return 'Ten komunikat jest fałszywy.';
        default: return '';
    }
}

function instruction(status, b) {
    switch (status) {
        case 'VERIFIED': return b.instruction;
        case 'VERIFIED_OTHER_AREA': return `Nie dotyczy Twojego obszaru. ${b.instruction}`;
        case 'EXPIRED': return 'Ten komunikat już nie obowiązuje. Słuchaj nowych, aktualnych komunikatów.';
        case 'INCOMPLETE': return 'Nie wykonuj poleceń z tego komunikatu. Komunikat krytyczny musi mieć dwa podpisy.';
        default: return 'Nie wykonuj poleceń z tego komunikatu. Ufaj tylko komunikatom zweryfikowanym w SYGNET.';
    }
}

/**
 * Model ekranu telefonu dla ramki z backendu (b) i wyniku kontrolnej weryfikacji (b.check).
 * null = telefon nic nie pokaże (MALFORMED – ignoruje; brak ramki).
 */
export function phoneView(b, userArea = null) {
    if (!b?.check) return null;
    let status = b.check.status;
    // obszar odbiorcy to ostatni krok weryfikacji – można go przełączyć w podglądzie bez ponownego podpisu
    if (userArea !== null && (status === 'VERIFIED' || status === 'VERIFIED_OTHER_AREA')) {
        status = covers(b.area_code, userArea) ? 'VERIFIED' : 'VERIFIED_OTHER_AREA';
    }
    if (status === 'MALFORMED') return null;
    const rejected = status === 'FORGED' || status === 'INCOMPLETE';
    const revoke = b.type === 250;
    const signers = (b.check.signer_names ?? []).filter(Boolean);

    return {
        status,
        color: STATUS_COLOR[status] ?? '#1D232B',
        icon: STATUS_ICON[status] ?? 'cross',
        title: TITLE[status] ?? status,
        line: statusLine(status, b),
        reason: rejected ? b.check.explanation : null,
        label: rejected ? 'Podaje się za' : revoke ? 'Komunikat systemowy' : 'Komunikat',
        typeName: b.type_name,
        typeIcon: b.type_icon,
        note: !rejected && !revoke && b.note ? `„${b.note}”` : null,
        instruction: instruction(status, b),
        time: clock(Date.now() / 1000),
        details: rejected
            ? [['Nadawca', b.issuer_name]]
            : [
                ['Nadawca', b.issuer_name],
                ...(signers.length > 1 ? [['Podpisali', signers.join(' + ')]] : []),
                ['Obszar', b.area_name],
                ['Wydano', dateTime(b.timestamp), true],
                [status === 'EXPIRED' ? 'Wygasł' : 'Ważne do', dateTime(b.valid_until), true],
            ],
        userArea: b.check.user_area_name,
    };
}

/** Przewidywany ekran przed podpisaniem (konsola pozwala podpisać tylko poprawny komunikat). */
export function previewView(form, ctx) {
    const now = Math.floor(Date.now() / 1000);
    const type = ctx.types.find((t) => t.id === form.type) ?? ctx.types[0];
    const area = ctx.areas.find((a) => a.code === form.area_code);
    const issuer = ctx.issuers.find((i) => i.id === form.issuer_id);
    const second = ctx.issuers.find((i) => i.id === form.second_signer_id);
    const fake = {
        type: type.id,
        type_name: type.name,
        type_icon: type.icon,
        instruction: type.instruction,
        area_code: form.area_code,
        issuer_name: issuer?.name ?? '–',
        area_name: area?.name ?? '–',
        note: form.note,
        timestamp: now,
        valid_until: now + form.valid_minutes * 60,
        check: {
            status: covers(form.area_code, ctx.userArea) ? 'VERIFIED' : 'VERIFIED_OTHER_AREA',
            signer_names: type.critical && second ? [issuer?.name, second.name] : [issuer?.name],
            user_area_name: ctx.areas.find((a) => a.code === ctx.userArea)?.name,
        },
    };
    return phoneView(fake);
}
