namespace RomeoOps.Installer;

public static class Pins
{
    public const string CliVersion = "1.41.1";
    public const string CliAsset = "stalker-gamma+win.x64.zip";
    public const string CliUrl = "https://github.com/FaithBeam/stalker-gamma-cli/releases/download/1.41.1/stalker-gamma%2Bwin.x64.zip";
    public const string CliHash = "e7fb059f564bb08da6dab0db98e3cb0f4ec5e47265a79794b4cbd6483c90b84e";
    public const string CliProfile = "stalker-romeo-xrrazom";

    public const string XrReferer = "https://www.moddb.com/mods/stalker-anomaly/addons/xrrazom-stalker-anomaly-co-op-slim";
    public const string XrMd5 = "9bf05e45157e29009446577a6a0d6b84";
    public const string XrFileName = "xrRazom-1.5-Slim.zip";

    // The archive still contains the Greek-delta profile. MO2 uses this ASCII alias.
    public const string RomeoProfile = "ROMEO - 2026-11-09-001";
    public const string RomeoArchiveFile = "ROMEO-2026-11-09-001.7z";
    public const string XrModName = "xrRazom Co-op";
    public const string SpeedFixName = "Teepo's Speed Fix";
    public static readonly string[] PriorityMods = { XrModName, SpeedFixName };

    public const string AnomalyUrl = "https://www.moddb.com/downloads/mirror/27740";
    public const string GammaUrl = "https://github.com/Grokitach/Stalker_GAMMA";
    public const string DiscordUrl = "https://discord.com/invite/t8m3pc67f6";
    public const string LauncherSupportUrl = "https://discord.com/channels/1186516148339081246/1572000028719972432";
    public const string PatreonUrl = "https://www.patreon.com/cw/clikradproductions";
    public const string RomeoDriveUrl = "https://drive.google.com/drive/folders/1ReY5hMyye4mC3VVYsWxCnJ81QXFsVEt7?usp=sharing";
    public const string RomeoMirrorUrl = "https://fuckingfast.net/nsueejm6073r";
    public const string RomeoTorrentUrl = "https://tinyurl.com/3vk7ay75";
    public const string DriveLimitHelpUrl = "https://discord.com/channels/1186516148339081246/1422283608529305735/1558432386637635605";
    public const string RomeoChannelUrl = "https://discord.com/channels/1186516148339081246/1554951582062215298";
    public const string ManualStep = "Launch Anomaly (DX11) through Mod Organizer, then set the xrRazom player name and hosting or network settings in the game Settings menu.";

    public const long MinimumFreeBytes = 150L * 1024 * 1024 * 1024;
    public const int ExpectedRomeoMods = 696;

    public static readonly string[] LegacyXrFiles =
    {
        @"db\mods\00_modded_exes_gamedata.db0",
        @"db\mods\12_xrmpe_anims_gamedata.db0",
        @"gamedata\scripts\tasks_pump_station_defense.script",
        @"gamedata\scripts\tasks_stash.script",
        @"gamedata\scripts\xrr_patch_quest_spawns.script"
    };
}
