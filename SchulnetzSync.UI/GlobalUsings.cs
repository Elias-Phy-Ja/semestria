// Resolve WPF-vs-WinForms ambiguities (both UseWPF + UseWindowsForms enabled).
// WinForms is only used for NotifyIcon (fully qualified in TrayService.cs).

global using Application      = System.Windows.Application;
global using MessageBox       = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage  = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using Color            = System.Windows.Media.Color;
global using Brush            = System.Windows.Media.Brush;
global using SolidColorBrush  = System.Windows.Media.SolidColorBrush;

// Convenience: HttpClient without explicit using in every file.
global using System.Net.Http;

// LegalTexts namespace shortcut used in AboutPage and OnboardingWindow.
global using SchulnetzSync.UI.Legal;
