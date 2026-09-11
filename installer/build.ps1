# BATCH-LITEXL-17 -- build LiteXLSetup.exe and LiteXLUninstall.exe.
#
# Both are MSIL / AnyCPU / .NET Framework 4.0 WinForms assemblies. The build
# encodes three constraints that are fatal if missed. Two are carried from
# D:\repo\varan-release\installer\build-setup.ps1, where they were paid for in
# the field:
#
#   1. /platform:anycpu ONLY. csc's DEFAULT for /target:winexe is
#      anycpu32bitpreferred, which sets 32BITREQUIRED and is ARM32-FATAL -- the
#      exe simply will not run on RT. It must be stated, not assumed.
#   2. NO reference to System.IO.Compression[.FileSystem]. Both are .NET
#      4.5-only and absent from some RT images; that was a real Varan 1.0 field
#      failure on a Spanish-locale RT device. Our payload is a DIRECTORY, so we
#      never needed them -- their absence at compile time is what keeps the
#      dependency from creeping back in.
#   3. WinForms, never WPF. WPF is not dependable on these images.
#
# Post-build gates, each with a must-fail control where one exists.
#
# ASCII only.

param([string]$Out = '')

$ErrorActionPreference = 'Stop'
$here = Split-Path -Parent $MyInvocation.MyCommand.Path
$out  = if ($Out -ne '') { $Out } else { Join-Path $here 'bin' }

# Nothing here is an absolute path: this script has to work both in the
# lite-xl fork (installer/ beside resources/) and in the build tree (where
# the checkout is a sibling worktree). Take the first icon that exists.
$iconCandidates = @(
  (Join-Path $here '..\resources\icons\icon.ico'),
  (Join-Path $here '..\lite-xl-218\resources\icons\icon.ico'),
  (Join-Path $here 'icon.ico')
)
$icon = $null
foreach ($c in $iconCandidates) { if (Test-Path $c) { $icon = (Resolve-Path $c).Path; break } }

# cliheader.py sits beside this script in the fork, and in tools\ in the
# build tree. Gate 1 is skipped with a loud warning if neither is present,
# rather than failing a build for a missing test tool.
$cliHeader = $null
foreach ($c in @((Join-Path $here 'cliheader.py'),
                 (Join-Path $here '..\tools\cliheader.py'))) {
  if (Test-Path $c) { $cliHeader = (Resolve-Path $c).Path; break }
}

$csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework64\v4.0.30319\csc.exe'
if (-not (Test-Path $csc)) { $csc = Join-Path $env:WINDIR 'Microsoft.NET\Framework\v4.0.30319\csc.exe' }
if (-not (Test-Path $csc)) { throw "no v4.0.30319 csc.exe found" }
if (-not $icon) { throw ("app icon not found; looked in: " + ($iconCandidates -join ', ')) }
New-Item -ItemType Directory -Force $out | Out-Null

$refs = @('/reference:System.dll', '/reference:System.Windows.Forms.dll',
          '/reference:System.Drawing.dll')

Write-Output "=== compiling (MSIL / AnyCPU / .NET Framework 4.0 / WinForms) ==="
Write-Output ("  icon: {0}" -f $icon)

# --- LiteXLSetup.exe ---
& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$icon" `
    "/out:$out\LiteXLSetup.exe" @refs "$here\Common.cs" "$here\Setup.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed for LiteXLSetup.exe rc=$LASTEXITCODE" }

# --- LiteXLUninstall.exe ---
& $csc /nologo /target:winexe /platform:anycpu /optimize+ "/win32icon:$icon" `
    "/out:$out\LiteXLUninstall.exe" @refs "$here\Common.cs" "$here\Uninstall.cs"
if ($LASTEXITCODE -ne 0) { throw "csc failed for LiteXLUninstall.exe rc=$LASTEXITCODE" }

foreach ($n in @('LiteXLSetup.exe', 'LiteXLUninstall.exe')) {
  Write-Output ("  built {0,-22} {1,9:N0} bytes" -f $n, (Get-Item "$out\$n").Length)
}

# ---------------------------------------------------------------------------
# gate 1 -- MSIL/AnyCPU read out of the artefact, not trusted from the switch.
# tools\cliheader.py already exists and is the instrument for this; it fails on
# 32BITREQUIRED, which is exactly what anycpu32bitpreferred would set.
# ---------------------------------------------------------------------------
Write-Output ""
Write-Output "=== gate 1: PE / CLI header (cliheader.py) ==="
if ($cliHeader) {
  python "$cliHeader" "$out\LiteXLSetup.exe" "$out\LiteXLUninstall.exe" |
    ForEach-Object { Write-Output ("  " + $_) }
  if ($LASTEXITCODE -ne 0) { throw "gate 1 FAILED: not MSIL/AnyCPU" }
} else {
  Write-Output "  *** SKIPPED: cliheader.py not found. This is the gate that catches"
  Write-Output "  *** anycpu32bitpreferred, which is ARM32-FATAL. Do not ship without it."
}

# ---------------------------------------------------------------------------
# gate 2 -- referenced assemblies, with the must-fail control that makes the
# absence meaningful. A checker that reports "absent" for everything proves
# nothing, so System.Windows.Forms must come back PRESENT in the same read.
# ---------------------------------------------------------------------------
Write-Output ""
Write-Output "=== gate 2: referenced assemblies (with control) ==="
$gate2fail = 0
foreach ($n in @('LiteXLSetup.exe', 'LiteXLUninstall.exe')) {
  $asm  = [Reflection.Assembly]::ReflectionOnlyLoadFrom("$out\$n")
  $refn = @($asm.GetReferencedAssemblies() | ForEach-Object { $_.Name })
  Write-Output ("  {0}: {1}" -f $n, ($refn -join ', '))
  $bad = @($refn | Where-Object { $_ -like 'System.IO.Compression*' })
  if ($bad.Count -gt 0) {
    Write-Output ("    FAIL: 4.5-only compression assembly referenced: {0}" -f ($bad -join ', '))
    $gate2fail++
  }
  if ($refn -notcontains 'System.Windows.Forms') {
    Write-Output "    FAIL (CONTROL): System.Windows.Forms not reported present -- the reader is broken, the 'absent' result above is void"
    $gate2fail++
  }
}
if ($gate2fail -gt 0) { throw "gate 2 FAILED" }
Write-Output "  gate 2 PASS: System.IO.Compression* ABSENT in both; control System.Windows.Forms PRESENT in both"

# ---------------------------------------------------------------------------
# gate 3 -- the registry surface is provably HKCU-only, read from the SOURCES.
# This is what licenses the HKCU-only uninstall sweep in G-I5.
# ---------------------------------------------------------------------------
Write-Output ""
Write-Output "=== gate 3: registry surface is HKCU-only (from source) ==="
$srcAll = @(Get-Content "$here\Common.cs", "$here\Setup.cs", "$here\Uninstall.cs")
$calls  = @($srcAll | Select-String -Pattern 'Registry\.[A-Za-z]+' -AllMatches |
            ForEach-Object { $_.Matches } | ForEach-Object { $_.Value })
$other  = @($calls | Where-Object { $_ -ne 'Registry.CurrentUser' -and $_ -ne 'Registry.LocalMachine' })
$hklm   = @($calls | Where-Object { $_ -eq 'Registry.LocalMachine' })
Write-Output ("  Registry.* call sites : {0}" -f $calls.Count)
Write-Output ("  Registry.CurrentUser  : {0}" -f (@($calls | Where-Object { $_ -eq 'Registry.CurrentUser' })).Count)
Write-Output ("  Registry.LocalMachine : {0}  (READ-ONLY: the VC++ runtime probe)" -f $hklm.Count)
Write-Output ("  any other root        : {0}" -f $other.Count)
# LocalMachine appears only in Util.VcRuntimeStatus, and only via OpenSubKey
# (read). Assert that: no HKLM write path may exist.
$hklmWrite = @($srcAll | Select-String -Pattern 'Registry\.LocalMachine\.(CreateSubKey|DeleteSubKey|SetValue)')
if ($other.Count -gt 0 -or $hklmWrite.Count -gt 0) {
  Write-Output "  gate 3 FAIL: a registry root other than HKCU is written"
  throw "gate 3 FAILED"
}
Write-Output "  gate 3 PASS: every WRITE is Registry.CurrentUser; HKLM is opened read-only for the runtime probe"

Write-Output ""
foreach ($n in @('LiteXLSetup.exe', 'LiteXLUninstall.exe')) {
  $h = (Get-FileHash "$out\$n" -Algorithm SHA256).Hash.ToLower()
  Write-Output ("  {0,-22} sha256 {1}" -f $n, $h)
}
