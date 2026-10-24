using System.Windows;
using SchulnetzSync.UI.Controls;

namespace SchulnetzSync.UI.Pages;

// The short tours of the pages, one place for all texts. The grade page keeps its own,
// since its tour depends on the chosen rule (see GradesPage).

/// <summary>Helpers for pointing a tour at a part of a page that has no name of its own.</summary>
internal static class TourTargets
{
    /// <summary>The nearest enclosing element of type <typeparamref name="T"/>, e.g. a column's border.</summary>
    public static T? Ancestor<T>(DependencyObject? element) where T : FrameworkElement
    {
        for (var d = element; d is not null; )
        {
            d = System.Windows.Media.VisualTreeHelper.GetParent(d) ?? LogicalTreeHelper.GetParent(d);
            if (d is T match) return match;
        }
        return null;
    }
}

public partial class DashboardPage : ITourPage
{
    public string TourKey => "dashboard";

    /// <summary>
    /// The first tour of all: a quick look at every area in the sidebar, without opening
    /// them (each explains itself on its first visit), then the dashboard in two steps.
    /// </summary>
    public IReadOnlyList<TourStep> TourSteps()
    {
        var main = MainWindow.Current;
        return
        [
            new(() => null, "Willkommen bei Semestria",
                "Ein kurzer Überblick über die Bereiche. Jeder Bereich zeigt dir beim ersten Öffnen selbst, wie er funktioniert."),
            new(() => main?.NavEvents, "Prüfungen & Termine",
                "Dein Kalender: Stundenplan, Prüfungen und Schultermine, dazu eigene Einträge."),
            new(() => main?.NavTasks, "Aufgaben",
                "Hausaufgaben und Abgaben, mit Erinnerung."),
            new(() => main?.NavGrades, "Noten",
                "Noten eintragen, Schnitt ausrechnen und sehen, ob die Promotion reicht."),
            new(() => main?.NavSettings, "Einstellungen",
                "Schulnetz-Link, Outlook und Darstellung."),
            new(() => main?.BtnProfile, "Profile",
                "Hier wechselst du das Profil oder legst ein neues an, zum Beispiel um auch den Stundenplan deines Kumpels anzuzeigen. Jedes Profil hat seine eigenen Daten."),
            new(() => main?.NavHelp, "Hilfe",
                "Alle Anleitungen findest du hier wieder."),
            new(() => main?.NavSynapkey, "Synapkey",
                "Synapkey ist eine weitere Web-App des Entwicklers. Der Eintrag öffnet sie im Browser; Semestria gibt dabei nichts von dir weiter."),
            new(() => BtnSync.Parent as FrameworkElement, "Laden",
                "«Feed laden» holt deinen Stundenplan aus dem Schulnetz. Mit Outlook synchronisieren ist freiwillig."),
            new(() => StatGrid, "Dein Tag auf einen Blick",
                "Oben die Zahlen, darunter die nächsten Prüfungen, dein Stundenplan und die offenen Aufgaben."),
        ];
    }
}

public partial class EventsPage : ITourPage
{
    public string TourKey => "calendar";

    /// <summary>The calendar itself, or the hint shown while there is no data yet.</summary>
    private FrameworkElement CalendarArea => MainLayout.IsVisible ? MainLayout : NoDataHint;

    public IReadOnlyList<TourStep> TourSteps() =>
    [
        new(() => BtnViewWeek.Parent as FrameworkElement, "Woche oder Monat",
            "Die Woche zeigt deinen Stundenplan wie im Schulzimmer, der Monat den Überblick über Prüfungen und Termine."),
        new(() => BtnPrevMonth.Parent as FrameworkElement, "Blättern",
            "Mit den Pfeilen vor und zurück, «Heute» springt zum aktuellen Tag."),
        new(() => BtnFilterAll.Parent as FrameworkElement, "Filter",
            "Nur Prüfungen, nur Termine oder nur Stunden anzeigen."),
        new(() => CalendarArea, "Einträge",
            "Ein Klick auf einen Eintrag zeigt Details, Farbe und Kommentare zum Fach. Ausgefallene Stunden erscheinen grau und durchgestrichen."),
        new(() => BtnAddEvent, "Eigene Einträge",
            "Eine Lerngruppe, ein Termin beim Coach: eigene Einträge legst du hier an und kannst sie später bearbeiten."),
        new(() => BtnCalSettings, "Farben",
            "Farben für Prüfungen, Termine und jedes Fach, dazu das Aufräumen ausgeblendeter Einträge."),
    ];
}

public partial class TasksPage : ITourPage
{
    public string TourKey => "tasks";

    /// <summary>The task list, or the empty state while there is nothing to show.</summary>
    private FrameworkElement TaskArea => TaskList.IsVisible ? TaskList : EmptyState;

    public IReadOnlyList<TourStep> TourSteps() =>
    [
        new(() => TourTargets.Ancestor<System.Windows.Controls.Border>(ListRail), "Listen",
            "Ordne Aufgaben in Listen, zum Beispiel eine pro Fach. Fächer aus deinem Stundenplan werden vorgeschlagen."),
        new(() => TxtQuickAdd, "Schnell erfassen",
            "Aufgabe eintippen und Enter drücken. Sie landet in der gewählten Liste."),
        new(() => BtnQuickDetails, "Mit Details",
            "Abgabedatum, Erinnerung, Liste und «wichtig». An die Erinnerung denkt Semestria, solange die App läuft."),
        new(() => RbOpen.Parent as FrameworkElement, "Offen und erledigt",
            "Wechsle zwischen offenen, erledigten und allen Aufgaben."),
        new(() => TaskArea, "Abhaken",
            "Ein Klick auf den Kreis hakt ab, ein Klick auf die Aufgabe öffnet sie zum Bearbeiten."),
        new(() => BtnListOptions, "Liste anpassen",
            "Farbe der Liste wählen oder erledigte Aufgaben aufräumen."),
    ];
}

public partial class SettingsPage : ITourPage
{
    public string TourKey => "settings";

    public IReadOnlyList<TourStep> TourSteps() =>
    [
        new(() => CardAppearance, "Darstellung",
            "Hell, dunkel oder wie Windows."),
        new(() => CardFeed, "Schulnetz",
            "Dein persönlicher Kalender-Link aus dem Schulnetz. Er ist verschlüsselt gespeichert und wird nie angezeigt oder geteilt."),
        new(() => CardOutlook, "Outlook (freiwillig)",
            "Melde dich mit deinem Microsoft-Konto an, wenn Prüfungen auch in Outlook, auf dem Handy oder in Teams erscheinen sollen."),
        new(() => CardOptions, "Synchronisation",
            "Was nach Outlook geht: Prüfungen ja, Schultermine nur wenn du willst. Dazu der Feed beim Start und die Nachrichten vom Entwickler."),
        new(() => CardCleanup, "Aufräumen",
            "Entfernt alles, was Semestria in Outlook angelegt hat. Deine eigenen Outlook-Termine bleiben."),
    ];
}

public partial class AboutPage : ITourPage
{
    public string TourKey => "about";

    public IReadOnlyList<TourStep> TourSteps() =>
    [
        new(() => CardLegal, "Rechtliches",
            "Nutzungsbedingungen und Datenschutzerklärung jederzeit zum Nachlesen."),
        new(() => CardData, "Deine Daten",
            "Was Semestria speichert und wo. Hier setzt du auch das aktuelle Profil zurück."),
    ];
}
