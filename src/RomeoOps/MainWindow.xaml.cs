using System.ComponentModel;
using System.Diagnostics;
using System.Windows;
using System.Windows.Automation;
using System.Windows.Controls;
using System.Windows.Controls.Primitives;
using System.Windows.Input;
using System.Windows.Media;
using System.Windows.Media.Imaging;
using Microsoft.Win32;
using RomeoOps.Installer;

namespace RomeoOps;

public partial class MainWindow : Window
{
    private static readonly Brush Ink = BrushesFrom("#202020"), Gray = BrushesFrom("#999999"), Red = BrushesFrom("#A13E40");
    private static readonly FontFamily Condensed = new(new Uri("pack://application:,,,/"), "./Fonts/#Barlow Condensed");
    private static readonly FontFamily ActionFont = new(new Uri("pack://application:,,,/"), "./Fonts/BarlowCondensed-SemiBold.ttf#Barlow Condensed SemiBold");
    private readonly AppSettings settings = SettingsStore.Load();
    private readonly Dictionary<string, Button> nav = new();

    private string current = "Settings", status = "", lastLog = "";
    private int percent;
    private CancellationTokenSource? operation;
    private RunJournal journal = new();
    private TextBlock? statusView;
    private ProgressBar? meter;
    private bool closing;

    public MainWindow()
    {
        InitializeComponent();
        ContentRendered += async (_, _) =>
        {
            if (IsPortableRelease) await CheckLauncherUpdate(false);
        };
        Width = Height = Math.Min(720, Math.Min(SystemParameters.WorkArea.Height, SystemParameters.WorkArea.Width));
        MouseLeftButtonDown += (_, e) => { if (e.ButtonState == MouseButtonState.Pressed && !e.Handled && (e.GetPosition(Frame).Y < 23 || e.GetPosition(Frame).X < 164)) DragMove(); };
        int index = 0;
        foreach (string name in new[] { "Play", "Install", "Verify", "Settings", "Support", "Add-ons", "About" })
        {
            var b = Action(Frame, 12, 174 + 42 * index++, 152, 38, name.ToUpperInvariant(), () => Show(name));
            b.Style = (Style)FindResource("SidebarButton");
            b.Padding = new Thickness(0); AutomationProperties.SetName(b,name);
            b.HorizontalContentAlignment = HorizontalAlignment.Right;
            nav.Add(name, b);
        }
        Put(Frame, Picture("chrome"), 666, 7, 46, 17);
        Hit(658, 1, 33, 25, "Minimize", () => WindowState = WindowState.Minimized);
        Hit(691, 1, 28, 25, "Close", Close);
        Hit(49, 650, 30, 32, "ROMEO on Patreon", () => Open(Pins.PatreonUrl));
        Hit(79, 650, 34, 32, "ROMEO Discord", () => Open(Pins.DiscordUrl));
        Show(Pending()?.Request.Mode.ToString() ?? "Settings");
    }

    private static Brush BrushesFrom(string hex) => (Brush)new BrushConverter().ConvertFromString(hex)!;
    private static Image Picture(string name) => new() { Source = new BitmapImage(new Uri($"pack://application:,,,/Art/{name}.png")), Stretch = Stretch.Fill };
    private static T Put<T>(Canvas host, T view, double x, double y, double width, double height) where T : FrameworkElement
    {
        Canvas.SetLeft(view, x); Canvas.SetTop(view, y);
        view.Width = width; view.Height = height; host.Children.Add(view);
        return view;
    }
    private static Button Action(Canvas host, double x, double y, double w, double h, string text, Action callback)
    {
        var b = Put(host, new Button { FontFamily = ActionFont, FontSize = h <= 29 ? 14 : 16 }, x,y,w,h);
        if (text.Length > 0)
        {
            // The mock uses 50/1000 em tracking on uppercase action labels.
            var label = new StackPanel { Orientation = Orientation.Horizontal };
            for (int i = 0; i < text.Length; i++)
                label.Children.Add(new TextBlock { Text = text[i].ToString(),
                    Margin = new Thickness(i == 0 ? 0 : b.FontSize * 0.05, 0, 0, 0) });
            b.Content = label;
            AutomationProperties.SetName(b, text);
        }
        b.Click += (_,_) => callback();
        return b;
    }
    private static Button Outline(Button button)
    {
        button.Background = Brushes.White;
        button.Foreground = Ink;
        button.BorderBrush = Ink;
        return button;
    }

    private void Hit(int x,int y,int w,int h,string name,Action callback)
    {
        var b = Action(Frame,x,y,w,h,"",callback);
        b.Background = Brushes.Transparent; b.ToolTip = name; AutomationProperties.SetName(b,name);
    }
    private TextBlock Label(double x,double y,double width,double height,string value,double size=16,bool bold=false)
        => Put(Page,new TextBlock { Text=value, FontFamily=Condensed, FontSize=size, Foreground=Ink,
            FontWeight=bold ? FontWeights.Bold : FontWeights.Normal, TextWrapping=TextWrapping.Wrap },x,y,width,height);

    private void Info(double x, double y, string subject, string explanation)
    {
        var icon = Put(Page, new TextBlock
        {
            Text = "ⓘ", FontFamily = new FontFamily("Segoe UI Symbol"), FontSize = 15,
            TextAlignment = TextAlignment.Center, Foreground = Brushes.DimGray,
            Background = Brushes.Transparent, Cursor = Cursors.Help, Focusable = true,
            ToolTip = new ToolTip { Content = new TextBlock { Text = explanation, MaxWidth = 285,
                TextWrapping = TextWrapping.Wrap, FontFamily = new FontFamily("Segoe UI"), FontSize = 13 } }
        }, x, y, 20, 21);
        KeyboardNavigation.SetIsTabStop(icon, true);
        ToolTipService.SetShowsToolTipOnKeyboardFocus(icon, true);
        ToolTipService.SetInitialShowDelay(icon, 250);
        ToolTipService.SetShowDuration(icon, 30000);
        AutomationProperties.SetName(icon, subject + " information");
        AutomationProperties.SetHelpText(icon, explanation);
    }

    private void Show(string page)
    {
        addonChecks?.Cancel();
        current=page;
        Page.Children.Clear(); statusView=null; meter=null;
        foreach (var (name,button) in nav)
        {
            button.Content = Picture(name + (name == page ? "On" : "Off"));
            button.Background = Brushes.White;
            button.Foreground = name == page ? Brushes.White : Ink;
            button.IsEnabled = operation == null || name == page;
        }
        if (page == "Settings") { SettingsPage(); return; }
        Label(0,16,500,52,page.ToUpperInvariant(),36,true);
        Put(Page,new Border { Background=Ink },0,74,530,2);
        if (page == "Add-ons") AddonsPage();
        if (page == "About") AboutPage();
        if (page == "Install" || page == "Verify") OperationPage(page == "Verify");
        if (page == "Play")
        {
            var existing=DetectInstallation();
            bool ready=existing!=null || SetupGate.LooksInstalled(settings);
            Label(0,84,530,30,ready ? "Open Mod Organizer, select the ROMEO profile and run Anomaly (DX11)." : "Choose your sources in Settings, then install the ROMEO stack.",20);
            if (ready)
                Action(Page,0,120,190,36,"OPEN MOD ORGANIZER",()=>Open(Path.Combine(existing?.Layout.Gamma ?? SetupGate.GammaFolder(settings),"ModOrganizer.exe")));
            else Action(Page,0,120,130,36,"OPEN SETTINGS",()=>Show("Settings"));
            if (ready) Info(195,127,"Mod Organizer","This opens Mod Organizer 2 from your GAMMA installation. In its profile selector, choose ROMEO, then run Anomaly (DX11).");
            if(existing!=null)
            {
                Put(Page,new Border { Background=Brushes.LightGray },0,215,530,1);
                Label(0,230,530,34,"CO-OP SETUP",23,true);
                Info(106,234,"Co-op setup","Export saves a JSON file with your xrRazom build, engine checksum, active add-ons and mod order. Compare reads another player's export and lists differences.");
                Label(0,264,520,40,"Share your active profile or compare it with a friend's exported setup.",18);
                Outline(Action(Page,0,300,178,35,"EXPORT CO-OP SETUP",()=>ShareSetup(existing,false)));
                Outline(Action(Page,186,300,178,35,"COMPARE WITH FRIEND",()=>ShareSetup(existing,true)));
            }
            if (ready && settings.CoOp) Label(0,171,520,32,"Set your xrRazom player name and network options in the game settings.");
        }
        if (page == "Support")
        {
            Label(0,86,515,28,"Get the ROMEO archive or join the community for support.",20);
            Action(Page,0,120,205,34,"ROMEO DOWNLOADS",ShowRomeoDownloads);
            Outline(Action(Page,213,120,96,34,"DISCORD",()=>Open(Pins.DiscordUrl)));
            Outline(Action(Page,317,120,96,34,"PATREON",()=>Open(Pins.PatreonUrl)));
            Label(0,176,515,25,"Installer logs include the failing step and the paths used for the run.",18);
            Info(150,214,"Installer logs","Open the latest run’s log folder. The last entries can help identify which step failed when asking for support.");
            Action(Page,0,207,145,34,"OPEN LOG FOLDER",OpenLogs);
            Label(0,275,530,28,$"Romeo Ops Launcher {InstalledVersion.ToString(3)}",20);
            if (IsPortableRelease)
                Outline(Action(Page,0,310,200,34,"CHECK FOR UPDATES",async () => await CheckLauncherUpdate(true)));
        }
    }

    private void AboutPage()
    {
        const string repository = "https://github.com/itszuff/Romeo-Ops-Launcher";
        Label(0,90,295,40,"ROMEO OPS LAUNCHER",30,true);
        Label(310,96,220,30,$"Installed version {InstalledVersion.ToString(3)}",22).Foreground = Red;
        Label(0,142,510,56,"Install, verify and play the ROMEO stack for S.T.A.L.K.E.R. Anomaly, with GAMMA and optional xrRazom co-op.",20);
        Action(Page,0,210,120,36,"CHANGELOG",()=>Open(repository + "/releases"));
        if (IsPortableRelease)
            Outline(Action(Page,126,210,157,36,"CHECK FOR UPDATES",async () => await CheckLauncherUpdate(true)));
        Outline(Action(Page,289,210,114,36,"JOIN DISCORD",()=>Open(Pins.DiscordUrl)));
        Outline(Action(Page,409,210,121,36,"VIEW ON GITHUB",()=>Open(repository)));
        Put(Page,new Border { Background=Brushes.LightGray },0,271,530,1);
        Label(0,287,530,35,"CREDITS",26,true);
        var credits = new[] {
            ("S.T.A.L.K.E.R. Anomaly", "Anomaly Developers"),
            ("G.A.M.M.A.", "Grokitach and the GAMMA mod authors"),
            ("ROMEO", "CLIKRAD Productions and contributors"),
            ("xrRazom", "Harbingerr and the xrRazom team"),
            ("stalker-gamma-cli", "FaithBeam and contributors")
        };
        int y = 336;
        foreach (var (project, creators) in credits)
        {
            Label(0,y,205,30,project,20,true);
            Label(210,y,320,30,creators,20);
            y += 35;
        }
        Outline(Action(Page,0,534,218,36,"FULL CREDITS & LINKS",()=>Open(repository + "#credits")));
    }

    private ExistingStack? DetectInstallation()
    {
        var detected = ExistingStack.Detect(settings.InstallFolder);
        if (detected != null) return detected;
        if (settings.AnomalySource != "Installed" && settings.GammaSource != "Installed") return null;
        if (!SetupGate.LooksInstalled(settings)) return null;
        var layout = InstallLayout.Create(SetupGate.AnomalyFolder(settings), SetupGate.GammaFolder(settings));
        return new ExistingStack(layout, layout.Modlist, File.ReadLines(layout.Modlist).Contains("+" + Pins.XrModName, StringComparer.OrdinalIgnoreCase));
    }

    private TextBlock? detectedLabel;
    private Button? detectedAction;
    private void RefreshDetection()
    {
        if (current != "Settings" || detectedLabel == null || detectedAction == null) return;
        var exists = RepairTarget() != null;
        detectedLabel.Visibility = detectedAction.Visibility = exists ? Visibility.Visible : Visibility.Collapsed;
    }

    private void SettingsPage()
    {
        Put(Page,Picture("directory"),0,0,170,12);
        Info(163,-4,"Installation directory","Choose the parent folder for Anomaly and GAMMA. To verify an existing installation, select the folder that contains both games.");
        PathField(0,20,true,"Installation directory",settings.InstallFolder,v=>{ settings.InstallFolder=v; RefreshDetection(); },true);
        Component(62,"anomaly",settings.AnomalySource, v=>settings.AnomalySource=v,settings.AnomalySource=="Installed" ? settings.AnomalyPath : settings.AnomalyZip,v=>settings.AnomalyZip=v);
        Component(124,"gamma",settings.GammaSource,v=>settings.GammaSource=v,settings.GammaSource=="Installed" ? settings.GammaPath : settings.GammaZip,v=>settings.GammaZip=v);
        Component(186,"romeo","Local",_=>{},settings.RomeoArchive,v=>settings.RomeoArchive=v);
        Component(248,"xrrazom",settings.CoOp ? settings.XrSource : "Disable",v=>{settings.CoOp=v!="Disable"; settings.XrSource=v=="Local" ? "Local":"Download";},settings.XrArchive,v=>settings.XrArchive=v);
        Put(Page,Picture("instructions"),0,319,140,14);
        string[] lines = { "Choose the installation folder for downloaded game files.",
            "Download Anomaly and G.A.M.M.A. or choose local .zip or .7z archives.",
            "Select the ROMEO .7z. For xrRazom, download, choose a ZIP, or disable co-op.",
            "Open INSTALL to review your choices and begin installation." };
        for (int i=0;i<lines.Length;i++)
        {
            Put(Page,new Border { Background=Ink },0,340+i*26,19,19);
            Label(4,336+i*26,15,26,(i+1).ToString(),21,true).Foreground=Brushes.White;
            Label(31,339+i*26,499,25,lines[i],16);
        }
        PreferenceControls();
        detectedLabel=Label(0,540,530,30,"Existing Anomaly, GAMMA and ROMEO installation detected.",18,true);
        detectedAction=Action(Page,0,575,205,34,"VERIFY / FIX INSTALLATION",()=>Show("Verify"));
        RefreshDetection();
    }
    private void Component(int y,string id,string mode,Action<string> select,string path,Action<string> change)
    {
        Put(Page,Picture(id),0,y-1,140,14);
        var (offset, help) = id switch
        {
            "anomaly" => (61, "Download fetches the base game during installation. Local uses the Anomaly archive you select."),
            "gamma" => (64, "Download fetches GAMMA during installation. Local uses the GAMMA archive you select."),
            "romeo" => (44, "Choose a download source, save the ROMEO .7z, then select it with Browse."),
            _ => (101, "xrRazom adds co-op support. Download fetches it during installation, Local uses a ZIP, and Disable skips co-op.")
        };
        Info(offset,y-5,id,help);
        var choices=id=="xrrazom" ? new[]{"Disable","Download","Local"} : new[]{"Download","Local"};
        int x=0;
        foreach (var choice in choices)
        {
            bool active=mode==choice || choice=="Local" && mode=="Installed";
            int width = choice == "Download" ? 85 : choice == "Disable" ? 65 : 54;
            var b=Action(Page,x,y+20,width,29,choice.ToUpperInvariant(),()=>{
                if (id=="romeo" && choice=="Download") { ShowRomeoDownloads(); return; }
                select(choice); Save(); Show(current);
            });
            if (!active) Outline(b);
            b.ToolTip=id=="romeo" && choice=="Download" ? "Choose a source for the ROMEO .7z" : choice+" "+id;
            AutomationProperties.SetName(b,id+" "+choice);
            x+=width+4;
        }
        PathField(x,y+20,false,id,mode=="Installed" ? "" : path,change,mode=="Local");
    }
    private void PathField(int x,int y,bool folder,string name,string value,Action<string> change,bool enabled)
    {
        var box=Put(Page,new TextBox { Text=enabled ? value : "", IsEnabled=enabled, VerticalContentAlignment=VerticalAlignment.Center,
            FontFamily=new FontFamily("Consolas"), FontSize=11, Foreground=Brushes.DarkGreen, BorderBrush=enabled ? Ink:Gray,
            Padding=new Thickness(5,0,2,0), Background=Brushes.White },x,y,460-x,29);
        AutomationProperties.SetName(box,name+" path"); box.ToolTip=folder ? "Choose a folder" : "Choose the "+name+" archive";
        box.TextChanged+=(_,_)=>change(box.Text.Trim());
        box.LostKeyboardFocus+=(_,_)=>Save();
        var browse=Action(Page,464,y,66,29,"BROWSE",()=>{
            if (folder) { var picker=new OpenFolderDialog { Title="Choose "+name+" folder" }; if(picker.ShowDialog(this)==true) box.Text=picker.FolderName; }
            else { var picker=new OpenFileDialog {Title="Choose "+name+" archive",Filter=name=="romeo" ? "ROMEO archive (*.7z)|*.7z" : name=="xrrazom" ? "xrRazom archive (*.zip)|*.zip" : "Game archives (*.zip, *.7z)|*.zip;*.7z"}; if(picker.ShowDialog(this)==true) box.Text=picker.FileName; }
            Save();
        });
        browse.IsEnabled=enabled; browse.Background=enabled ? Ink:Gray; AutomationProperties.SetName(browse,"Browse "+name);
    }
    private ExistingStack? RepairTarget() => DetectInstallation() ?? ExistingStack.Detect(settings.InstallFolder,allowIncomplete:true);
    private SavedRun? Pending()
    {
        try { return SavedRun.Read(settings.InstallFolder); }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException or ArgumentException) { return null; }
    }

    private void PreferenceControls()
    {
        void Row(int y,string label,bool user)
        {
            var check=Put(Page,new CheckBox {Content=label,FontFamily=Condensed,FontSize=16,
                IsChecked=user ? settings.PreserveUser : settings.PreserveMcm},0,y,350,25);
            check.ToolTip="Checked keeps current values during install and verify. Unchecked applies packaged defaults on completion.";
            Info(user ? 116 : 217,y-1,label,
                (user ? "Keep your current user.ltx game settings" : "Keep your current Mod Configuration Menu options and keybinds") +
                " when installation or repair finishes. Clear the box to apply defaults then. Reset to Fresh Defaults backs up and resets this group immediately.");
            check.Click+=(_,_)=>{ if(user) settings.PreserveUser=check.IsChecked==true; else settings.PreserveMcm=check.IsChecked==true; Save(); };
            Outline(Action(Page,341,y-3,189,28,"RESET TO FRESH DEFAULTS",()=>
            {
                try
                {
                    var stack=DetectInstallation() ?? throw new IOException("Select an installed ROMEO stack first.");
                    using var gate=SavedRun.Lock(stack.Layout);
                    CoopAddon.RequireIdle(stack.Layout);
                    PlayerOptions.Capture(stack.Layout,Guid.NewGuid());
                    if (user) PlayerOptions.Reset(stack.Layout,stack.CoOp,"user.ltx");
                    else PlayerOptions.Reset(stack.Layout,stack.CoOp,"axr_options.ltx");
                    MessageBox.Show(this,user ? "Restored the GAMMA user.ltx." : "Restored ROMEO MCM options and keybinds.","ROMEO OPS");
                }
                catch(Exception ex) {MessageBox.Show(this,ex.Message,"Player settings");}
            }));
        }
        Row(463,"Preserve user.ltx",true);
        Row(496,"Preserve MCM options and keybinds",false);
        Info(317,582,"Restore from backup","Choose a dated player-settings backup, then select which settings to restore.");
        Outline(Action(Page,341,575,189,34,"RESTORE FROM BACKUP",PickBackup));
    }

    private CancellationTokenSource? addonChecks;

    private async void AddonsPage()
    {
        var stack=DetectInstallation();
        bool ready=stack!=null && stack.CoOp && Directory.Exists(Path.Combine(stack.Layout.XrModDir,"gamedata"));
        var byline = Label(0,86,530,29,"Co-op add-ons by ",21);
        var author = new System.Windows.Documents.Hyperlink(new System.Windows.Documents.Run("Mee (MeeMaster)"))
        {
            NavigateUri = new Uri("https://www.moddb.com/members/meemaster"), Foreground = Red,
            ToolTip = "Open Mee's ModDB profile"
        };
        author.Click += (_,_) => Open(author.NavigateUri.AbsoluteUri);
        byline.Inlines.Add(author);
        Label(0,122,530,51,ready ? "Check a mod to enable it. Missing mods and required dependencies download from ModDB." :
            "Select a ROMEO installation with xrRazom enabled in Settings.",18);
        Info(109,35,"Co-op add-ons","These mods use xrRazom. Enabling DANTE, Persistent Stalkers or Med Bag also enables Master Core. Disable those mods before disabling Master Core.");
        var feedback=Label(0,522,530,75,"",17);
        using var checks=new CancellationTokenSource();
        addonChecks=checks;
        var pending=new List<Task>();
        foreach(var (addon,index) in CoopAddon.Catalog.Select((item,i)=>(item,i)))
        {
            int y=181+index*65;
            var local=stack==null ? null : InstalledAddon.Detect(stack,addon);
            var check=Put(Page,new CheckBox { Content=addon.Name, FontSize=21,FontFamily=Condensed,
                IsChecked=local?.Enabled==true, IsEnabled=ready },0,y,350,30);
            var modPage = Outline(Action(Page,351,y,81,26,"MODDB",()=>Open(addon.PageUrl)));
            AutomationProperties.SetName(modPage,addon.Name+" on ModDB");
            modPage.ToolTip = "Open "+addon.Name+" on ModDB";
            var line=Label(21,y+28,400,24,local?.Caption ?? "Not installed",15);
            check.Click+=async (_,_)=>await Change(addon,check.IsChecked==true,false);
            if(local!=null && ready) pending.Add(CheckRelease(addon,local,line,y));
        }
        try { await Task.WhenAll(pending); }
        finally { if(addonChecks==checks) addonChecks=null; }

        async Task CheckRelease(CoopAddon addon,InstalledAddon local,TextBlock line,int y)
        {
            using var timeout=CancellationTokenSource.CreateLinkedTokenSource(checks.Token);
            timeout.CancelAfter(TimeSpan.FromSeconds(30));
            line.Text=local.Caption+" · Checking ModDB...";
            try
            {
                var result=await Task.Run(()=>ModDbRelease.Check(addon,local,timeout.Token),timeout.Token);
                if(checks.IsCancellationRequested) return;
                line.Text=local.Caption+" · "+result;
                if(result=="Different release available" && Path.GetFileName(local.Folder).Equals(addon.Folder,StringComparison.OrdinalIgnoreCase))
                    Action(Page,440,y+7,90,32,"UPDATE",async ()=>await Change(addon,local.Enabled,true));
            }
            catch(Exception) { if(!checks.IsCancellationRequested) line.Text=local.Caption+" · Update status unavailable"; }
        }

        async Task Change(CoopAddon addon,bool enabled,bool update)
        {
            if(operation!=null) return;
            addonChecks?.Cancel();
            using var stop=new CancellationTokenSource();
            operation=stop;
            Page.IsEnabled=false;
            foreach(var button in nav.Values) button.IsEnabled=false;
            try
            {
                await Task.Run(()=>CoopAddon.Install(stack!,[addon],
                    (item,path,token)=>ModDbRelease.Fetch(item,path,PickBrowserDownload,token),
                    message=>Dispatcher.Invoke(()=>feedback.Text=message),stop.Token,enabled,update));
            }
            catch(OperationCanceledException) { }
            catch(Exception ex) { if(!closing) MessageBox.Show(this,ex.Message,"MeeMaster add-ons"); }
            finally { operation=null; Page.IsEnabled=true; }
            if(closing) Close(); else Show("Add-ons");
        }
    }

    private Task<string?> PickBrowserDownload(CoopAddon addon)
    {
        return Dispatcher.InvokeAsync(()=>
        {
            if(closing || operation?.IsCancellationRequested==true) return null;
            if(MessageBox.Show(this,$"The ModDB page for {addon.Name} will open in your browser. Download the ZIP, then select it in the launcher.","Download add-on",MessageBoxButton.OKCancel)!=MessageBoxResult.OK) return null;
            Open(addon.PageUrl);
            var picker=new OpenFileDialog {Title="Select the current "+addon.Name+" ZIP from ModDB",Filter="ZIP archives (*.zip)|*.zip"};
            return picker.ShowDialog(this)==true ? picker.FileName : null;
        }).Task;
    }

    private void OperationPage(bool verify)
    {
        var existing=RepairTarget();
        var pending=Pending();
        bool resume=pending?.Request.Mode==(verify ? PipelineMode.Verify : PipelineMode.Install);
        if(operation==null && pending!=null && !resume)
        {
            string target=pending.Request.Mode.ToString();
            Label(0,95,530,55,"A "+target.ToLowerInvariant()+" is in progress for this folder.",20);
            Action(Page,0,164,190,36,"OPEN "+target.ToUpperInvariant(),()=>Show(target));
            return;
        }
        if (!verify && !resume && operation==null && existing!=null)
        {
            Label(0,95,530,55,"An installation already exists in this folder.",20);
            Info(194,171,"Verify and fix","Scan the existing installation and review the findings before choosing a repair.");
            Action(Page,0,164,190,36,"OPEN VERIFY / FIX",()=>Show("Verify"));
            return;
        }
        var problems=verify ? new List<string>() : SetupGate.Validate(settings);
        if (verify && existing==null && !resume) problems.Add("Choose the folder containing the installed Anomaly and GAMMA games and a ROMEO profile in Settings.");
        string archive=verify ? existing?.RomeoArchive(settings.RomeoArchive) ?? "" : settings.RomeoArchive;

        if (resume) problems.Clear();
        bool fullRepair=verify && archive.Length>0;
        string sources=$"Anomaly   {settings.AnomalySource}     /     GAMMA   {settings.GammaSource}     /     xrRazom   {(settings.CoOp ? settings.XrSource : "Disabled")}";
        Label(0,86,530,25,verify ? existing?.Layout.Root ?? "Installation directory" : sources,16,true);
        Label(0,115,530,46,verify && !fullRepair ? "Check installed files and fix the MO2 profile. Select a ROMEO .7z in Settings to also restore game and mod files." : archive.Length==0 ? "Choose the ROMEO archive in Settings." : archive,15);
        if (verify)
        {
            void Option(int x,int y,string name,string help,bool initial,Action<bool> change)
            {
                var c=Put(Page,new CheckBox {Content=name,IsChecked=initial,IsEnabled=operation==null && fullRepair && !resume,FontFamily=Condensed,FontSize=15},x,y,double.NaN,25);
                c.Measure(new Size(245,25));
                Info(x+c.DesiredSize.Width+4,y-2,name,help);
                c.Click+=(_,_)=>{change(c.IsChecked==true);Save();};
            }
            Option(0,212,"Create per-mod reinstall archives","Create missing per-mod archives for later reinstallation. This uses additional disk space.",settings.CreateSnapshots,v=>settings.CreateSnapshots=v);
            Option(0,160,"Check MD5","Hash each GAMMA add-on archive during repair. Archives with a mismatched checksum are downloaded again.",settings.VerifyCheckMd5,v=>settings.VerifyCheckMd5=v);
            Option(260,160,"Recheck large files","Refresh the GAMMA large-files repository during repair. This can require additional downloads.",settings.VerifyLargeFiles,v=>settings.VerifyLargeFiles=v);
            Option(0,186,"Force Git refresh","Download GitHub add-on archives again and refresh the setup repositories during repair.",settings.VerifyForceGit,v=>settings.VerifyForceGit=v);
            Option(260,186,"Force extraction","Extract GAMMA archives again during repair, including archives whose checksums already match.",settings.VerifyForceExtract,v=>settings.VerifyForceExtract=v);
        }
        int row=verify ? 245:160;
        if(operation!=null) Action(Page,0,row,130,35,scanning ? "CANCEL SCAN":"STOP AND SAVE",()=>operation?.Cancel());
        else if(problems.Count>0) Action(Page,0,row,130,35,"OPEN SETTINGS",()=>Show("Settings"));
        else Action(Page,0,row,168,35,resume ? "RESUME "+pending!.Request.Mode.ToString().ToUpperInvariant() : verify ? "SCAN INSTALLATION" : "START INSTALLATION",()=>{if(verify && !resume) Scan(existing!); else Run(verify);});
        if (resume && operation==null)
            Action(Page,180,row,192,35,"CLEAR SAVED PROGRESS",()=>
            {
                if (MessageBox.Show(this,"Clear this checkpoint and choose new settings? Installed files and player-settings backups stay on disk.","Saved progress",MessageBoxButton.YesNo)!=MessageBoxResult.Yes) return;
                try
                {
                    var layout=InstallLayout.Create(pending!.Request.AnomalyPath,pending.Request.GammaPath);
                    using var gate=SavedRun.Lock(layout);
                    var latest=SavedRun.Read(layout.Root);
                    if(latest?.Id==pending.Id)
                    {
                        var backup=PlayerOptions.Capture(layout,latest.Id);
                        PlayerOptions.Apply(layout,backup,true,true,false);
                        latest.Finish(layout);
                    }
                    status=""; Show(current);
                }
                catch(Exception ex){MessageBox.Show(this,ex.Message,"Saved progress");}
            });
        if (resume && operation==null)
            Info(377,row+7,"Saved progress","Resume continues from the saved checkpoint. Clear Saved Progress discards that checkpoint and keeps installed files and player-settings backups.");
        else if (operation!=null)
            Info(135,row+7,scanning ? "Cancel scan" : "Stop and save",scanning ? "Stop reading the installation files." : "Stop this run and keep its checkpoint so you can resume later.");
        else if (problems.Count==0)
            Info(173,row+7,verify ? "Scan installation" : "Start installation",verify ? "Scan reads your installation and shows a repair plan. Select a ROMEO archive in Settings to enable full repair and the GAMMA options above." : "Begin installing into the selected directory with the sources shown above. You can stop the run and resume from its checkpoint.");
        statusView=Label(0,row+46,528,58,problems.Count>0 ? string.Join(" ",problems) : resume && operation==null ? status+"  Saved steps: "+(pending!.Done.Count==0 ? "starting stage" : string.Join(", ",pending.Done)) : status,16);
        meter=Put(Page,new ProgressBar {Minimum=0,Maximum=100,Value=percent,Foreground=Red,Background=Brushes.WhiteSmoke},0,row+108,530,5);
        Put(Page,journal,0,row+127,530,595-row-127);
    }
    private async void Run(bool verify)
    {
        if(operation!=null) return;
        var saved=Pending();
        var existing=verify ? RepairTarget() : null;
        if (verify && existing==null && saved==null) { Show("Verify"); return; }
        using var cancel=new CancellationTokenSource();
        operation=cancel; percent=0; status="Preparing installation tools…"; journal = new RunJournal(); Show(current);
        var request=saved?.Request ?? new PipelineRequest { Mode=verify ? PipelineMode.Verify:PipelineMode.Install, AnomalyPath=existing?.Layout.Anomaly ?? SetupGate.AnomalyFolder(settings),
            GammaPath=existing?.Layout.Gamma ?? SetupGate.GammaFolder(settings),RomeoArchive=existing?.RomeoArchive(settings.RomeoArchive) ?? settings.RomeoArchive,
            AnomalyZip=!verify && settings.AnomalySource=="Local" ? settings.AnomalyZip:null,
            GammaZip=!verify && settings.GammaSource=="Local" ? settings.GammaZip:null,
            XrArchive=verify ? RetainedXr(existing!) : settings.CoOp && settings.XrSource=="Local" ? settings.XrArchive:null,
            InstallCoOp=existing?.CoOp ?? settings.CoOp, DownloadGamma=verify || settings.GammaSource=="Download", DownloadAnomaly=verify || settings.AnomalySource=="Download",
            CreateSnapshots=settings.CreateSnapshots,PreserveUser=settings.PreserveUser,PreserveMcm=settings.PreserveMcm,
            VerifyCheckMd5=settings.VerifyCheckMd5,VerifyLargeFiles=settings.VerifyLargeFiles,VerifyForceExtract=settings.VerifyForceExtract,VerifyForceGit=settings.VerifyForceGit };
        var engine=new InstallPipeline(new PipelineCallbacks {
            Log=line=>Dispatcher.Invoke(()=>journal.Add(line.Display)),
            Progress=p=>Dispatcher.Invoke(()=>{percent=Math.Clamp(p.Percent,0,100); status=$"{percent}%   {p.Step}\n{p.Detail}"; if(statusView!=null) statusView.Text=status; if(meter!=null) meter.Value=percent;}),
            Confirm=message=>Dispatcher.Invoke(()=>MessageBox.Show(this,message,"ROMEO OPS",MessageBoxButton.YesNo,MessageBoxImage.Question)==MessageBoxResult.Yes)
        });
        try
        {
            var result=await Task.Run(()=>engine.RunAsync(request,cancel.Token));
            lastLog=result.LogFile ?? "";
            status=result.Success ? (verify ? "Verification complete." : "Installation complete. Open Play to launch Mod Organizer.") : result.Error ?? "Installation stopped.";
        }
        catch(Exception ex){status=ex.Message;}
        finally {operation=null; if(closing) Close(); else Show(current);}
    }
    private string? RetainedXr(ExistingStack existing)
    {
        if (!existing.CoOp) return null;
        var retained = Path.Combine(existing.Layout.Gamma, "downloads", Pins.XrFileName);
        return File.Exists(retained) ? retained : File.Exists(settings.XrArchive) ? settings.XrArchive : null;
    }
    private void OpenLogs()
    {
        try {
            string? path=string.IsNullOrEmpty(lastLog) ? InstallLayout.Create(SetupGate.AnomalyFolder(settings),SetupGate.GammaFolder(settings)).Logs : Path.GetDirectoryName(lastLog);
            if(path!=null && Directory.Exists(path)) Open(path); else MessageBox.Show(this,"The installer has not created a log folder yet.","ROMEO OPS");
        } catch(ArgumentException ex) {MessageBox.Show(this,ex.Message,"ROMEO OPS");}
    }
    private void Open(string path)
    {
        try {Process.Start(new ProcessStartInfo(path){ UseShellExecute=true,WorkingDirectory=File.Exists(path) ? Path.GetDirectoryName(path)! : Environment.CurrentDirectory });}
        catch(Exception ex) when(ex is Win32Exception or IOException){MessageBox.Show(this,ex.Message,"ROMEO OPS");}
    }
    private void Save()
    {
        if (restartingForUpdate) return;
        try { SettingsStore.Save(settings); }
        catch(Exception ex) when(ex is IOException or UnauthorizedAccessException) {MessageBox.Show(this,$"Could not write {SettingsStore.FilePath}\n\n{ex.Message}","Could not save settings");}
    }
    protected override void OnClosing(CancelEventArgs e)
    {
        if (installingRelease) { e.Cancel = true; return; }
        if(operation!=null)
        {
            e.Cancel=true;
            if(!closing && MessageBox.Show(this,current=="Add-ons" ? "Cancel the add-on update and exit?" : scanning ? "Stop scanning and exit?" : "Save progress, stop the current operation and exit?","ROMEO OPS",MessageBoxButton.YesNo)==MessageBoxResult.Yes)
            {closing=true; operation.Cancel();}
        }
        else Save();
        base.OnClosing(e);
    }
}




