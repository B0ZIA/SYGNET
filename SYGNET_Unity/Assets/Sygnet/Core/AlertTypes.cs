using System.Collections.Generic;

namespace Sygnet.Core
{
    public sealed class AlertTypeInfo
    {
        public readonly int Id;
        public readonly string Code;
        public readonly string Name;
        public readonly bool Critical;
        public readonly string Instruction;

        public AlertTypeInfo(int id, string code, string name, bool critical, string instruction)
        {
            Id = id;
            Code = code;
            Name = name;
            Critical = critical;
            Instruction = instruction;
        }
    }

    /// <summary>
    /// Typy komunikatów (PROTOCOL.md §4). Polityka „krytyczny = 2 podpisy” jest zaszyta TUTAJ, a nie w ramce,
    /// więc atakujący nie może jej wyłączyć.
    /// </summary>
    public static class AlertTypes
    {
        public const int AirRaid = 1;
        public const int AllClear = 2;
        public const int Evacuation = 3;
        public const int WaterContamination = 4;
        public const int PowerOutage = 5;
        public const int Chemical = 6;
        public const int DisinfoWarning = 7;
        public const int General = 8;
        public const int KeyRevoke = 250;

        static readonly Dictionary<int, AlertTypeInfo> ById = new Dictionary<int, AlertTypeInfo>();

        static AlertTypes()
        {
            Add(AirRaid, "AIR_RAID", "Alarm lotniczy", false,
                "Natychmiast udaj się do najbliższego schronu lub piwnicy. Odsuń się od okien.");
            Add(AllClear, "ALL_CLEAR", "Odwołanie alarmu", false,
                "Zagrożenie minęło. Zachowaj ostrożność i słuchaj kolejnych komunikatów.");
            Add(Evacuation, "EVACUATION", "Ewakuacja", true,
                "Opuść wskazany obszar wyznaczonym kierunkiem. Zabierz dokumenty, wodę, leki.");
            Add(WaterContamination, "WATER_CONTAMINATION", "Skażenie wody", false,
                "Nie pij wody z kranu. Używaj wody butelkowanej lub przegotowanej.");
            Add(PowerOutage, "POWER_OUTAGE", "Awaria zasilania", false,
                "Oszczędzaj baterię telefonu. Przygotuj latarkę i radio na baterie.");
            Add(Chemical, "CHEMICAL", "Zagrożenie chemiczne", false,
                "Zostań w budynku, zamknij okna i wentylację, uszczelnij drzwi.");
            Add(DisinfoWarning, "DISINFO_WARNING", "Ostrzeżenie przed dezinformacją", false,
                "Krążą fałszywe komunikaty. Ufaj tylko komunikatom zweryfikowanym w SYGNET.");
            Add(General, "GENERAL", "Komunikat ogólny", false,
                "Zapoznaj się z treścią komunikatu.");
            Add(KeyRevoke, "KEY_REVOKE", "Unieważnienie klucza", false,
                "Klucz wskazanego nadawcy został unieważniony. Jego komunikaty nie będą już uznawane.");
        }

        static void Add(int id, string code, string name, bool critical, string instruction) =>
            ById[id] = new AlertTypeInfo(id, code, name, critical, instruction);

        public static bool IsCritical(int type) => ById.TryGetValue(type, out var t) && t.Critical;

        /// <summary>Zawsze zwraca opis; nieznany typ dostaje ogólną instrukcję.</summary>
        public static AlertTypeInfo Get(int type) =>
            ById.TryGetValue(type, out var t)
                ? t
                : new AlertTypeInfo(type, "UNKNOWN_" + type, "Komunikat (typ " + type + ")", false,
                    "Zapoznaj się z treścią komunikatu.");
    }
}
