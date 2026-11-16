# Embedded in the launcher and run by Windows PowerShell after handoff.
$ErrorActionPreference = 'Stop'
$target = $env:ROMEO_UPDATE_TARGET
$stage = $env:ROMEO_UPDATE_STAGE
$backup = "$target.previous"
$lock = $null
try {
    try { $parent = [Diagnostics.Process]::GetProcessById([int]$env:ROMEO_UPDATE_PID) }
    catch [ArgumentException] { $parent = $null }
    if ($parent -and !$parent.WaitForExit(60000)) { throw 'The launcher did not exit in time. Reopen it and try again.' }
    $lock = [IO.FileStream]::new("$target.update-lock", [IO.FileMode]::OpenOrCreate,
        [IO.FileAccess]::Write, [IO.FileShare]::None, 4096, [IO.FileOptions]::DeleteOnClose)
    if ([Diagnostics.FileVersionInfo]::GetVersionInfo($target).FileVersion -ne $env:ROMEO_UPDATE_FROM) {
        throw 'Another copy has already changed this launcher. Reopen it to see the installed version.'
    }
    function Assert-Digest($stream) {
        $sha = [Security.Cryptography.SHA256]::Create()
        try { $actual = [BitConverter]::ToString($sha.ComputeHash($stream)).Replace('-', '') }
        finally { $sha.Dispose() }
        if ($actual -ne $env:ROMEO_UPDATE_DIGEST) { throw 'The launcher failed its SHA-256 check.' }
    }
    $file = [IO.File]::OpenRead($stage)
    try { Assert-Digest $file } finally { $file.Dispose() }
    [IO.File]::Replace($stage, $target, $backup)
    try {
        $file = [IO.File]::OpenRead($target)
        try {
            Assert-Digest $file
            $start = [Diagnostics.ProcessStartInfo]::new()
            $start.FileName = $target
            $start.WorkingDirectory = $env:ROMEO_UPDATE_CWD
            $start.UseShellExecute = $false
            [Diagnostics.Process]::Start($start).Dispose()
        } finally { $file.Dispose() }
    } catch {
        [IO.File]::Replace($backup, $target, $stage)
        throw
    }
} catch {
    $detail = $_.Exception.GetBaseException().Message
    Add-Type -AssemblyName PresentationFramework
    [void][Windows.MessageBox]::Show("The launcher update could not finish. $detail", 'Romeo Ops Launcher')
    exit 1
} finally {
    if ($lock) { $lock.Dispose() }
    [IO.File]::Delete($stage)
}
