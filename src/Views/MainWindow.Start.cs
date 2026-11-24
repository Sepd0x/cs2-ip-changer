using System.Windows;
using System.Windows.Input;
using SRT.Core;

namespace SRT;

// The first start's two doors (24 Nov 2026, usability study, publish/plano-1-0-0/1-app-usabilidade.md): 46 of 57 real installs never
// plugged in a phone and Regions is the most used page, while the tour and Phone setup pushed everyone to the phone. The doors open
// where the tour opened by itself (StartTourWhenFree: once, before any phone, when nothing else is on top); the tour is still there
// behind Show me around and in Settings > App.
public partial class MainWindow
{
    /// <summary>
    /// The window opens on Regions while no phone was ever checked on this PC, unless the phone door was picked (Phone setup is on
    /// Connection). Not in the demo (its captures pick their page). Someone with a phone keeps Connection, as before.
    /// </summary>
    public static bool StartOnRegions(bool phoneSeen, string door, bool demo) => !phoneSeen && !demo && door != "phone";

    void OpenDoors()
    {
        if (!_demo)
        {
            // Seen when it opens, as the tour: closing the app half way doesn't bring it back
            _s.TourSeen = true;
            try { SaveSettings(); } catch (Exception ex) { Log.Warn("Couldn't save that the first start's doors were seen: " + ex.Message); }
            Telemetry.Count("tour_door_shown");
        }
        // Screen readers get the doors' names in the app's language (Loc.Apply only swaps visible texts)
        System.Windows.Automation.AutomationProperties.SetName(DoorRegionsBtn, Loc.T(Texts.DoorRegions));
        System.Windows.Automation.AutomationProperties.SetName(DoorPhoneBtn, Loc.T(Texts.DoorPhone));
        StartPane.Visibility = Visibility.Visible;
        Dispatcher.BeginInvoke(() => DoorRegionsBtn.Focus(), System.Windows.Threading.DispatcherPriority.Loaded);
    }

    void CloseDoors(Texts.Door door)
    {
        if (StartPane.Visibility != Visibility.Visible) return;
        StartPane.Visibility = Visibility.Collapsed;
        if (_demo) return;
        Telemetry.Count(Texts.DoorCount(door));
        Log.Info("First start: " + door switch { Texts.Door.Regions => "Pick my regions.", Texts.Door.Phone => "Play through my phone.", Texts.Door.Tour => "Show me around.", _ => "Later." });
        if (door is Texts.Door.Regions or Texts.Door.Phone)
        {
            _s.StartDoor = door == Texts.Door.Phone ? "phone" : "regions";
            try { SaveSettings(); } catch (Exception ex) { Log.Warn("Couldn't save the first start's choice: " + ex.Message); }
        }
    }

    void DoorRegions_Click(object sender, RoutedEventArgs e)
    {
        CloseDoors(Texts.Door.Regions);
        NavRegions.IsChecked = true;
        ShowRegionsPitch();   // the Regions note waited for the doors to close
    }

    async void DoorPhone_Click(object sender, RoutedEventArgs e)
    {
        CloseDoors(Texts.Door.Phone);
        NavNet.IsChecked = true;
        await RunPhoneSetup();
    }

    void DoorTour_Click(object sender, RoutedEventArgs e)
    {
        CloseDoors(Texts.Door.Tour);
        OpenTour(auto: true);
    }

    /// <summary>Later (or Esc): nothing picked; with no phone yet the window goes to Regions, where it opens from now on.</summary>
    void DoorLater_Click(object sender, RoutedEventArgs e)
    {
        CloseDoors(Texts.Door.Later);
        if (StartOnRegions(_s.PhoneSeen, _s.StartDoor, _demo)) { NavRegions.IsChecked = true; ShowRegionsPitch(); }
    }

    /// <summary>Esc is Later while the doors are open.</summary>
    bool DoorsKey(Key key)
    {
        if (StartPane.Visibility != Visibility.Visible) return false;
        if (key != Key.Escape) return false;
        DoorLater_Click(this, null);
        return true;
    }
}
