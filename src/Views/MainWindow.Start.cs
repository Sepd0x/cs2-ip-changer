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

    /// <summary>
    /// The doors are still to open at this start (StartTourWhenFree waits for the window to be free). Set with the start page,
    /// before the window shows; cleared when they open or when their wait gives up. The Regions note waits for it.
    /// </summary>
    bool _doorsDue;

    /// <summary>The doors open at this start: as the tour did (TourDue), and not in the recording build.</summary>
    public static bool DoorsDue(bool seen, bool phoneSeen, bool demo, bool recording) => TourDue(seen, phoneSeen, demo) && !recording;

    /// <summary>The Regions note isn't shown behind the first start's doors, nor before they open (they show it when they close).</summary>
    public static bool PitchWaitsForDoors(bool doorsOpen, bool doorsDue) => doorsOpen || doorsDue;

    /// <summary>
    /// The page the window opens on, picked in the constructor, before the window is on screen (25 Nov: it was picked after the
    /// first sync, 1 to 3 s after the window showed, so at every start without a phone Connection jumped to Regions, also from a page
    /// clicked meanwhile). Nothing changes the page by itself after this.
    /// </summary>
    void PickStartPage()
    {
        _doorsDue = DoorsDue(_s.TourSeen, _s.PhoneSeen, _demo, Rec.On);
        if (StartOnRegions(_s.PhoneSeen, _s.StartDoor, _demo)) NavRegions.IsChecked = true;
    }

    /// <summary>What a screen reader says for each door: its title, in the app's language.</summary>
    public static (string Regions, string Phone) DoorNames() => (Loc.T(Texts.DoorRegions), Loc.T(Texts.DoorPhone));

    void OpenDoors()
    {
        _doorsDue = false;
        if (!_demo)
        {
            // Seen when it opens, as the tour: closing the app half way doesn't bring it back
            _s.TourSeen = true;
            try { SaveSettings(); } catch (Exception ex) { Log.Warn("Couldn't save that the first start's doors were seen: " + ex.Message); }
            Telemetry.Count("tour_door_shown");
        }
        // Screen readers get the doors' names in the app's language (Loc.Apply only swaps visible texts)
        var (regions, phone) = DoorNames();
        System.Windows.Automation.AutomationProperties.SetName(DoorRegionsBtn, regions);
        System.Windows.Automation.AutomationProperties.SetName(DoorPhoneBtn, phone);
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

    /// <summary>
    /// Later (or Esc): nothing picked, and the page stays the one you are on (the window already opened on Regions with no phone
    /// yet, and opens there from now on). On Regions, its note waited for the doors to close.
    /// </summary>
    void DoorLater_Click(object sender, RoutedEventArgs e)
    {
        CloseDoors(Texts.Door.Later);
        if (NavRegions.IsChecked == true) ShowRegionsPitch();
    }

    /// <summary>Esc is Later while the doors are open; no other key is theirs.</summary>
    public static bool DoorsEsc(bool doorsOpen, Key key) => doorsOpen && key == Key.Escape;

    bool DoorsKey(Key key)
    {
        if (!DoorsEsc(StartPane.Visibility == Visibility.Visible, key)) return false;
        DoorLater_Click(this, null);
        return true;
    }
}
