// The project has UseWPF and UseWindowsForms on at the same time, so half a dozen type
// names exist twice. Pinning them here once keeps every other file free of the noise.
// WinForms is only in the build for NotifyIcon, which TrayService.cs spells out in full.

global using Application      = System.Windows.Application;
global using MessageBox       = System.Windows.MessageBox;
global using MessageBoxButton = System.Windows.MessageBoxButton;
global using MessageBoxImage  = System.Windows.MessageBoxImage;
global using MessageBoxResult = System.Windows.MessageBoxResult;
global using Color            = System.Windows.Media.Color;
global using Brush            = System.Windows.Media.Brush;
global using SolidColorBrush  = System.Windows.Media.SolidColorBrush;

// HttpClient shows up all over the place, so it gets a global using.
global using System.Net.Http;

// LegalTexts is needed on the about page and in the onboarding wizard.
global using SchulnetzSync.UI.Legal;
