<#
.SYNOPSIS
    Locates the differing byte ranges in the BIN chunks of two .glb files.

.DESCRIPTION
    Answers "where in the buffer do these two exports diverge?" — which is what
    turns "the file differs" into "the difference is one contiguous run at the
    NORMAL accessor's offset". Pair it with GlbInfo.ps1 to map an offset back to
    a bufferView, and with glbcmp.py to interpret it per attribute.

    Compares only up to the shorter of the two BIN chunks; a length mismatch is
    reported rather than treated as a diff.

.EXAMPLE
    ./BinDiff.ps1 -A ./cue4out/Mesh.glb -B ./fmodel/Mesh.glb
#>
param(
    [Parameter(Mandatory)][string]$A,
    [Parameter(Mandatory)][string]$B,
    [int]$MaxRanges = 15
)

function Get-GlbChunks([string]$path) {
    $fs = [IO.File]::OpenRead($path)
    try {
        $br = New-Object IO.BinaryReader($fs)
        $null = $br.ReadUInt32()          # magic 'glTF'
        $null = $br.ReadUInt32()          # version
        $len = $br.ReadUInt32()           # total length
        $chunks = @{}
        while ($fs.Position -lt $len) {
            $cl = $br.ReadUInt32()
            $ct = $br.ReadUInt32()
            $data = $br.ReadBytes([int]$cl)
            $name = if ($ct -eq 0x4E4F534A) { 'JSON' } elseif ($ct -eq 0x004E4942) { 'BIN' } else { '0x{0:X}' -f $ct }
            $chunks[$name] = $data
        }
        return $chunks
    }
    finally { $fs.Close() }
}

$a = (Get-GlbChunks $A)['BIN']
$b = (Get-GlbChunks $B)['BIN']

if ($null -eq $a -or $null -eq $b) {
    Write-Error 'One of the files has no BIN chunk.'
    exit 1
}

if ($a.Length -ne $b.Length) {
    "BIN length differs: $($a.Length) vs $($b.Length) — comparing the common prefix only."
}

$n = [Math]::Min($a.Length, $b.Length)
$ranges = New-Object System.Collections.ArrayList
$start = -1

for ($i = 0; $i -lt $n; $i++) {
    if ($a[$i] -ne $b[$i]) {
        if ($start -lt 0) { $start = $i }
    }
    elseif ($start -ge 0) {
        [void]$ranges.Add([pscustomobject]@{ Start = $start; End = $i - 1; Len = $i - $start })
        $start = -1
    }
}
if ($start -ge 0) {
    [void]$ranges.Add([pscustomobject]@{ Start = $start; End = $n - 1; Len = $n - $start })
}

$totalDiff = ($ranges | Measure-Object Len -Sum).Sum
"A : $A"
"B : $B"
"BIN size $n, differing runs = $($ranges.Count), total differing bytes = $totalDiff"

if ($ranges.Count -gt 0) {
    "first/last differing offsets: $($ranges[0].Start) .. $($ranges[-1].End)"
    $ranges | Select-Object -First $MaxRanges | Format-Table -AutoSize
    if ($ranges.Count -gt $MaxRanges) {
        "... $($ranges.Count - $MaxRanges) more runs not shown (raise -MaxRanges)"
    }
}
