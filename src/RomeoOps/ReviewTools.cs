using System.Text.Json;
using System.Windows;
using System.Windows.Controls;
using Microsoft.Win32;
using RomeoOps.Installer;

namespace RomeoOps;

public partial class MainWindow
{
    private bool scanning;
    private Window ToolWindow(string title, StackPanel body)
    {
        var area = SystemParameters.WorkArea;
        return new Window { Owner=this, Title=title, Width=Math.Min(620,area.Width), MaxHeight=area.Height,
            SizeToContent=SizeToContent.Height, WindowStartupLocation=WindowStartupLocation.CenterOwner,
            FontFamily=Condensed,FontSize=17,Content=body };
    }
    private static Button DialogButton(string text, Action action)
    {
        var button=new Button {Content=text,Padding=new Thickness(12,5,12,5),Margin=new Thickness(0,4,8,0),MinHeight=32};
        button.Click+=(_,_)=>action();
        return button;
    }
    private void ShowRomeoDownloads()
    {
        var body = new StackPanel { Margin = new Thickness(18) };
        var dialog = ToolWindow("ROMEO downloads", body);
        dialog.Width = Math.Min(490, SystemParameters.WorkArea.Width);
        dialog.Content = new ScrollViewer { Content = body, VerticalScrollBarVisibility = ScrollBarVisibility.Auto };

        void Note(string text) => body.Children.Add(new TextBlock
        {
            Text = text, TextWrapping = TextWrapping.Wrap, Margin = new Thickness(0, 0, 0, 6)
        });

        void Links(params (string Caption, string Url)[] links)
        {
            var row = new WrapPanel { Margin = new Thickness(0, 0, 0, 10) };
            foreach (var link in links)
                row.Children.Add(DialogButton(link.Caption, () => Open(link.Url)));
            body.Children.Add(row);
        }

        Note("Download the ROMEO .7z, then select it with Browse in Settings. The torrent link opens with a torrent client.");
        Links(("GOOGLE DRIVE", Pins.RomeoDriveUrl), ("FUCKINGFAST", Pins.RomeoMirrorUrl), ("TORRENT MAGNET", Pins.RomeoTorrentUrl));
        Note("If Drive reaches its download limit, use another source above or see the download-limit guide on Discord.");
        body.Children.Add(new Separator { Margin = new Thickness(0, 6, 0, 12) });
        Note("New to the ROMEO Discord? Join the server and complete its first-time setup. Then open the release channel or the Drive guide.");
        Links(("JOIN DISCORD", Pins.DiscordUrl), ("RELEASE CHANNEL", Pins.RomeoChannelUrl), ("DRIVE LIMIT GUIDE", Pins.DriveLimitHelpUrl));
        var close = DialogButton("CLOSE", () => dialog.Close());
        close.IsCancel = true;
        close.HorizontalAlignment = HorizontalAlignment.Right;
        body.Children.Add(close);
        dialog.ShowDialog();
    }

    private bool Review(string title,string text,string? approve=null)
    {
        var body=new StackPanel {Margin=new Thickness(16)};
        var dialog=ToolWindow(title,body);
        body.Children.Add(new TextBox {Text=text,IsReadOnly=true,TextWrapping=TextWrapping.Wrap,Height=Math.Min(390,SystemParameters.WorkArea.Height*0.55),
            Padding=new Thickness(8),VerticalScrollBarVisibility=ScrollBarVisibility.Auto});
        var buttons=new StackPanel {Orientation=Orientation.Horizontal};
        body.Children.Add(buttons);
        buttons.Children.Add(DialogButton("CLOSE",()=>dialog.DialogResult=false));
        if(approve!=null) buttons.Children.Add(DialogButton(approve,()=>dialog.DialogResult=true));
        return dialog.ShowDialog()==true;
    }

    private async void Scan(ExistingStack stack)
    {
        if(operation!=null) return;
        using var stop=new CancellationTokenSource();
        operation=stop; scanning=true; percent=0; status="Reading installation files…"; journal=new RunJournal(); Show("Verify");
        string? findings=null;
        try
        {
            findings=await Task.Run(()=>InstallationScan.Inspect(stack,stop.Token,(value,file)=>Dispatcher.Invoke(()=>
            {
                percent=value;
                if(meter!=null) meter.Value=value;
                if(statusView!=null) statusView.Text=$"Scanning {value}%   {file}";
            })));
            journal.Add(findings);
            percent=100; status="Scan complete.";
        }
        catch(OperationCanceledException){status="Scan cancelled.";}
        catch(Exception ex){status="Scan incomplete. "+ex.Message;}
        finally { operation=null; scanning=false; }
        if(closing){Close();return;}
        Show("Verify");
        if(findings==null) return;
        bool full=stack.RomeoArchive(settings.RomeoArchive).Length>0;
        bool possible=full || File.Exists(stack.Profile);
        string scope=full ? "Full repair rechecks Anomaly, applies the selected GAMMA checks, reapplies ROMEO"+
            (stack.CoOp?" and xrRazom":"")+", and repairs the MO2 profile." : "Profile repair checks installed folders and repairs MO2 configuration. Restoring payload files requires the ROMEO archive.";
        if(full && !settings.VerifyCheckMd5 && !settings.VerifyForceGit && !settings.VerifyForceExtract && !settings.VerifyLargeFiles)
            scope+=" GAMMA addon checks are off.";
        if(full && settings.CreateSnapshots) scope+=" Missing per-mod reinstall archives will be created.";
        var review=findings+"\n\n"+scope+" The scan checks base checksums, recognized overlays and profile structure. Individual GAMMA mod contents are checked during full repair.";
        if(Review("Verify • findings and repair plan",review,possible ? (full?"RUN FULL REPAIR":"REPAIR PROFILE"):null))
            Run(true);
    }

    private async void ShareSetup(ExistingStack stack,bool compare)
    {
        try
        {
            if(compare)
            {
                var open=new OpenFileDialog {Title="Choose another player's ROMEO setup",Filter="ROMEO setup (*.json)|*.json"};
                if(open.ShowDialog(this)!=true || operation!=null) return;
                var target=CoopSetup.Read(open.FileName);
                var plan=CoopMatch.Plan(stack,target);
                var findings=CoopSetup.Capture(stack).Compare(target);
                if (!Review("Co-op setup comparison",findings+"\n\n"+plan.Description,
                    plan.CanApply ? "MATCH LOADED JSON" : null)) return;

                using var stop=new CancellationTokenSource();
                operation=stop;
                Page.IsEnabled=false;
                foreach(var button in nav.Values) button.IsEnabled=false;
                var progress=Label(0,348,520,42,"Matching co-op setup…",18);
                string outcome="Match complete.";
                try
                {
                    await Task.Run(()=>CoopMatch.Apply(stack,target,
                        (addon,path,token)=>ModDbRelease.Fetch(addon,path,PickBrowserDownload,token),
                        message=>Dispatcher.Invoke(()=>progress.Text=message),stop.Token));
                }
                catch(OperationCanceledException){outcome="Match cancelled.";}
                catch(Exception ex){outcome="Match incomplete. "+ex.Message;}
                finally { operation=null; Page.IsEnabled=true; }
                if(closing){Close();return;}
                Show("Play");
                var current=DetectInstallation() ?? throw new IOException("Could not reopen the installation.");
                Review("Co-op setup result",outcome+"\n\n"+CoopSetup.Capture(current).Compare(target)+"\n\n"+CoopMatch.Plan(current,target).Description);
            }
            else
            {
                var save=new SaveFileDialog {Title="Export co-op setup",Filter="ROMEO setup (*.json)|*.json",FileName="ROMEO-coop-setup.json"};
                if(save.ShowDialog(this)==true)
                    File.WriteAllText(save.FileName,JsonSerializer.Serialize(CoopSetup.Capture(stack),new JsonSerializerOptions {WriteIndented=true}));
            }
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Co-op setup");}
    }

    private void PickBackup()
    {
        try
        {
            var stack=DetectInstallation() ?? throw new IOException("Select an installed ROMEO stack first.");
            var entries=PlayerOptions.Backups(stack.Layout);
            if(entries.Length==0){MessageBox.Show(this,"No player-settings backups exist for this installation yet.","Restore backup");return;}
            var body=new StackPanel {Margin=new Thickness(18)};
            var dialog=ToolWindow("Restore player settings",body);
            var dates=new ComboBox {ItemsSource=entries,DisplayMemberPath="Label",SelectedIndex=0,Margin=new Thickness(0,0,0,10)};
            var user=new CheckBox {Content="Restore user.ltx"};
            var mcm=new CheckBox {Content="Restore MCM options and keybinds"};
            body.Children.Add(dates); body.Children.Add(user); body.Children.Add(mcm);
            void SelectFiles()
            {
                var item=(PlayerBackup)dates.SelectedItem;
                user.IsChecked=user.IsEnabled=item.User;
                mcm.IsChecked=mcm.IsEnabled=item.Mcm;
            }
            dates.SelectionChanged+=(_,_)=>SelectFiles(); SelectFiles();
            var actions=new StackPanel {Orientation=Orientation.Horizontal}; body.Children.Add(actions);
            actions.Children.Add(DialogButton("RESTORE SELECTED",()=>
            {
                if(user.IsChecked!=true && mcm.IsChecked!=true) return;
                try {PlayerOptions.RestoreBackup(stack.Layout,(PlayerBackup)dates.SelectedItem,user.IsChecked==true,mcm.IsChecked==true);dialog.DialogResult=true;}
                catch(Exception ex){MessageBox.Show(dialog,ex.Message,"Restore backup");}
            }));
            actions.Children.Add(DialogButton("CANCEL",()=>dialog.DialogResult=false));
            if(dialog.ShowDialog()==true) MessageBox.Show(this,"Selected player settings restored.","ROMEO OPS");
        }
        catch(Exception ex){MessageBox.Show(this,ex.Message,"Restore backup");}
    }
}
